using System.Runtime.InteropServices;
using Mystia.Modding.Bridge;
using Xunit;

namespace Mystia.Tests;

public class DetourTests
{
    [Fact]
    public void Trampoline_reaches_the_original_and_apply_replaces_it()
    {
        var address = NativeLibrary.GetExport(NativeLibrary.Load("kernel32"), "GetCurrentProcessId");
        var direct = Marshal.GetDelegateForFunctionPointer<ProcessIdDelegate>(address);
        var pid = direct();
        Assert.Equal((uint)Environment.ProcessId, pid);

        var provider = new X64DetourProvider();
        var hook = provider.Create(address, (ProcessIdDelegate)(() => 1));
        try
        {
            var original = hook.GenerateTrampoline<ProcessIdDelegate>();
            Assert.Equal(pid, original());
            hook.Apply();
            Assert.Equal(1u, direct());
            Assert.Equal(pid, original());
        }
        finally
        {
            hook.Dispose();
        }

        Assert.Equal(pid, direct());
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint ProcessIdDelegate();
}
