using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.System.JobObjects;

namespace StealthEye.Runtime;

internal static class Win32JobApi
{
    internal static SafeFileHandle CreateKillOnCloseJob()
    {
        var job = PInvoke.CreateJobObject(lpJobAttributes: null, lpName: null);
        if (job.IsInvalid)
        {
            job.Dispose();
            ProcessRunner.ThrowWin32("CreateJobObject(CsWin32)");
        }

        var limits = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        limits.BasicLimitInformation.LimitFlags =
            JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        var bytes = MemoryMarshal.AsBytes(
            MemoryMarshal.CreateReadOnlySpan(ref limits, 1));

        if (!PInvoke.SetInformationJobObject(
                job,
                JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                bytes))
        {
            job.Dispose();
            ProcessRunner.ThrowWin32("SetInformationJobObject(CsWin32)");
        }

        return job;
    }

    internal static void AssignProcess(SafeFileHandle job, SafeHandle process)
    {
        if (!PInvoke.AssignProcessToJobObject(job, process))
            ProcessRunner.ThrowWin32("AssignProcessToJobObject(CsWin32)");
    }

    internal static void AssignProcess(SafeFileHandle job, IntPtr process)
    {
        using var borrowed = new SafeFileHandle(process, ownsHandle: false);
        AssignProcess(job, borrowed);
    }

    internal static void Terminate(SafeFileHandle job, uint exitCode = 1)
    {
        if (!PInvoke.TerminateJobObject(job, exitCode))
            ProcessRunner.ThrowWin32("TerminateJobObject(CsWin32)");
    }
}