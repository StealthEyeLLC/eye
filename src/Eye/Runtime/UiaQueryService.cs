namespace StealthEye.Runtime;

public sealed class UiaQueryService(
    DesktopWindowStore windows,
    SessionWorkerManager workers,
    UiaElementStore elements)
{
    public async Task<UiaQuerySnapshot> QueryAsync(
        string windowId,
        int maxDepth = 4,
        int maxNodes = 200,
        CancellationToken cancellationToken = default)
    {
        var window = windows.ResolveActive(windowId);
        var observation = await workers.QueryUiaAsync(window.Hwnd, maxDepth, maxNodes, cancellationToken);
        return elements.Apply(window, observation);
    }
}