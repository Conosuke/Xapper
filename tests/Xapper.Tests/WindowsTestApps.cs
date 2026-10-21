using System.Runtime.InteropServices;

namespace Xapper.Tests;

/// <summary>Finds real solution outputs and checks desktop cleanup without exposing production handles.</summary>
internal static class WindowsTestApps
{
    #region Helpers

    /// <summary>Finds a built project output, failing when the prerequisite solution build was omitted.</summary>
    public static string Find(string project, string framework = "net9.0-windows")
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = current.Parent?.Name ?? "Debug";
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Xapper.slnx")))
            current = current.Parent;
        if (current is null)
            throw new InvalidOperationException("Cannot locate Xapper.slnx from the test output directory.");
        var folder = project == "Xapper.TestApp" ? "tests" : "src";
        var exe = Path.Combine(current.FullName, folder, project, "bin", configuration, framework, project + ".exe");
        Assert.True(File.Exists(exe), $"Build Xapper.slnx before running integration tests: missing {exe}");
        return exe;
    }

    /// <summary>Tests whether any process or handle still retains a desktop of this name.</summary>
    public static bool DesktopExists(string name)
    {
        var desktop = OpenDesktopW(name, 0, false, 1);
        if (desktop == IntPtr.Zero)
            return false;
        CloseDesktop(desktop);
        return true;
    }

    #endregion

    #region Interop

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenDesktopW(string name, uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);

    #endregion
}
