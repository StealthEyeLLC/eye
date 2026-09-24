using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using WinRT;
using StealthEye.Contract;

namespace StealthEye.Worker;

internal static class DesktopWgcCapture
{
    private const uint D3d11CreateDeviceBgraSupport = 0x20;
    private const int D3dDriverTypeHardware = 1;
    private const int D3dDriverTypeWarp = 5;
    private const uint D3d11SdkVersion = 7;
    private static readonly int[] FeatureLevels = [0xb100, 0xb000, 0xa100, 0xa000];
    private static readonly Guid DxgiDeviceIid = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    internal static async Task<WorkerWindowCaptureResult> CaptureAsync(
        WorkerWindowCaptureRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Hwnd == 0)
            throw new ArgumentException("hwnd is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.DestinationPath))
            throw new ArgumentException("destination_path is required.", nameof(request));
        if (request.TimeoutMs is < 1 or > 60_000)
            throw new ArgumentException("timeout_ms must be between 1 and 60000.", nameof(request));
        if (!GraphicsCaptureSession.IsSupported())
            throw new InvalidOperationException("Windows Graphics Capture is not supported on this system.");

        var destination = Path.GetFullPath(request.DestinationPath);
        var directory = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException("destination_path must have a parent directory.", nameof(request));
        Directory.CreateDirectory(directory);
        var temporary = destination + ".tmp";
        try { File.Delete(temporary); } catch (FileNotFoundException) { }

        var item = CreateItem(new IntPtr(request.Hwnd));
        if (item.Size.Width <= 0 || item.Size.Height <= 0)
            throw new InvalidOperationException("Capture item has an invalid size.");

        var device = CreateDevice();
        using var pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            device,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            item.Size);
        using var session = pool.CreateCaptureSession(item);
        session.IsCursorCaptureEnabled = false;
        session.IsBorderRequired = false;
        session.DirtyRegionMode = GraphicsCaptureDirtyRegionMode.ReportOnly;

        var frameReady = new TaskCompletionSource<Direct3D11CaptureFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFrame(Direct3D11CaptureFramePool sender, object args)
        {
            try
            {
                var frame = sender.TryGetNextFrame();
                if (frame is not null && !frameReady.TrySetResult(frame))
                    frame.Dispose();
            }
            catch (Exception ex)
            {
                frameReady.TrySetException(ex);
            }
        }

        pool.FrameArrived += OnFrame;
        try
        {
            session.StartCapture();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(request.TimeoutMs);
            using var frame = await frameReady.Task.WaitAsync(timeout.Token);
            var width = frame.ContentSize.Width;
            var height = frame.ContentSize.Height;
            var dirtyRegionCount = frame.DirtyRegions.Count;

            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
            string? ocrText = null;
            if (request.RecognizeText)
            {
                var ocr = OcrEngine.TryCreateFromUserProfileLanguages()
                    ?? throw new InvalidOperationException("No Windows OCR recognizer is available for the active user.");
                var recognized = await ocr.RecognizeAsync(bitmap);
                ocrText = recognized.Text;
            }
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();

            stream.Seek(0);
            var size = checked((uint)stream.Size);
            using var input = stream.GetInputStreamAt(0);
            using var reader = new DataReader(input);
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
            File.Move(temporary, destination, overwrite: true);
            return new WorkerWindowCaptureResult(width, height, dirtyRegionCount, bytes.LongLength, ocrText);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"No Windows Graphics Capture frame arrived within {request.TimeoutMs} ms.");
        }
        finally
        {
            pool.FrameArrived -= OnFrame;
            try { File.Delete(temporary); } catch { }
        }
    }

    private static GraphicsCaptureItem CreateItem(IntPtr hwnd)
    {
        var factory = (IGraphicsCaptureItemInterop)ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var iid = GuidGenerator.GetIID(typeof(GraphicsCaptureItem));
        var hr = factory.CreateForWindow(hwnd, in iid, out var abi);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            return MarshalInspectable<GraphicsCaptureItem>.FromAbi(abi);
        }
        finally
        {
            Marshal.Release(abi);
        }
    }

    private static IDirect3DDevice CreateDevice()
    {
        var hr = CreateNativeDevice(D3dDriverTypeHardware, out var d3d, out var context);
        if (hr < 0)
            hr = CreateNativeDevice(D3dDriverTypeWarp, out d3d, out context);
        Marshal.ThrowExceptionForHR(hr);

        try
        {
            var iid = DxgiDeviceIid;
            hr = Marshal.QueryInterface(d3d, in iid, out var dxgiDevice);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable);
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    return MarshalInspectable<IDirect3DDevice>.FromAbi(inspectable);
                }
                finally
                {
                    Marshal.Release(inspectable);
                }
            }
            finally
            {
                Marshal.Release(dxgiDevice);
            }
        }
        finally
        {
            if (context != IntPtr.Zero) Marshal.Release(context);
            if (d3d != IntPtr.Zero) Marshal.Release(d3d);
        }
    }

    private static int CreateNativeDevice(int driverType, out IntPtr device, out IntPtr context) =>
        D3D11CreateDevice(
            IntPtr.Zero,
            driverType,
            IntPtr.Zero,
            D3d11CreateDeviceBgraSupport,
            FeatureLevels,
            (uint)FeatureLevels.Length,
            D3d11SdkVersion,
            out device,
            out _,
            out context);

    [DllImport("d3d11.dll", PreserveSig = true)]
    private static extern int D3D11CreateDevice(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        [In] int[] featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out IntPtr device,
        out int featureLevel,
        out IntPtr immediateContext);

    [DllImport("d3d11.dll", PreserveSig = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(IntPtr hwnd, in Guid iid, out IntPtr result);

        [PreserveSig]
        int CreateForMonitor(IntPtr hmon, in Guid iid, out IntPtr result);
    }
}
