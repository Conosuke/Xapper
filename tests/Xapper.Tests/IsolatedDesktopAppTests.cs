using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Xapper.McpServer.Infrastructure;
using Xapper.Protocol;

namespace Xapper.Tests;

/// <summary>Exercises the native owner, including failure cleanup and inherited job membership.</summary>
[Collection(DesktopWindowCollection.Name)]
public sealed class IsolatedDesktopAppTests
{
    #region Tests

    [Fact]
    public void EnvironmentBlock_OverridesIgnoringCaseAndEndsWithTwoNuls()
    {
        var key = "XAPPER_TEST_" + Guid.NewGuid().ToString("N");
        try
        {
            Environment.SetEnvironmentVariable(key, "old");
            var block = new string(IsolatedDesktopApp.EnvironmentBlock(new Dictionary<string, string>
            {
                [key.ToLowerInvariant()] = "한 글",
                [IsolatedDesktopEnvironment.Variable] = "1"
            }));
            Assert.EndsWith("\0\0", block);
            var entries = block.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(entries, e => e.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(entries, e => e.Equals(key + "=한 글", StringComparison.OrdinalIgnoreCase));
            var names = entries.Select(e => e[..e.IndexOf('=', e.StartsWith('=') ? 1 : 0)]).ToArray();
            Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), names);
        }
        finally { Environment.SetEnvironmentVariable(key, null); }
    }

    [Fact]
    public void Names_AreUniqueAndContainTheServerPid()
    {
        var first = IsolatedDesktopApp.NextDesktopName();
        Assert.StartsWith($"xapper-{Environment.ProcessId}-", first);
        Assert.NotEqual(first, IsolatedDesktopApp.NextDesktopName());
    }

    [Fact]
    public void MissingExecutable_DoesNotLeaveADesktop()
    {
        var name = IsolatedDesktopApp.NextDesktopName();
        Assert.Throws<Win32Exception>(() => IsolatedDesktopApp.Start(name,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"), null,
            Path.GetTempPath(), new Dictionary<string, string>()));
        Assert.False(WindowsTestApps.DesktopExists(name));
    }

    [Fact]
    public async Task VisibleOnlyOnItsDesktop_AndDisposeEndsTheAppAndRemovesDesktop()
    {
        var exe = WindowsTestApps.Find("Xapper.TestApp");
        var name = IsolatedDesktopApp.NextDesktopName();
        using var app = IsolatedDesktopApp.Start(name, exe, null, Path.GetDirectoryName(exe) ?? ".",
            new Dictionary<string, string> { ["DOTNET_STARTUP_HOOKS"] = "" });
        using var observer = Process.GetProcessById(app.Process.Id);
        _ = observer.Handle;
        var watch = Stopwatch.StartNew();
        while (app.FindVisibleWindow() == IntPtr.Zero && watch.Elapsed < TimeSpan.FromSeconds(10))
            await Task.Delay(50);
        Assert.NotEqual(IntPtr.Zero, app.FindVisibleWindow());
        observer.Refresh();
        Assert.Equal(IntPtr.Zero, observer.MainWindowHandle);
        app.Dispose();
        app.Dispose();
        Assert.True(observer.WaitForExit(5000));
        Assert.False(WindowsTestApps.DesktopExists(name));
    }

    [Fact]
    public async Task ArgumentsEnvironmentAndWorkingDirectory_SurviveUnicodeAndSpaces()
    {
        var folder = Path.Combine(Path.GetTempPath(), "xapper 한 글 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var script = Path.Combine(folder, "probe.ps1");
        var result = Path.Combine(folder, "result.txt");
        await File.WriteAllTextAsync(script,
            "param($value)\n[IO.File]::WriteAllLines((Join-Path $pwd 'result.txt'), @($value, $pwd.Path, $env:XAPPER_TEST_VALUE))\nStart-Sleep 60",
            new UTF8Encoding(true));
        try
        {
            var powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            using var app = IsolatedDesktopApp.Start(IsolatedDesktopApp.NextDesktopName(), powershell,
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\" \"한 글\"", folder,
                new Dictionary<string, string> { ["XAPPER_TEST_VALUE"] = "값" });
            var watch = Stopwatch.StartNew();
            while (!File.Exists(result) && watch.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(50);
            Assert.Equal(new[] { "한 글", folder, "값" }, await File.ReadAllLinesAsync(result));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task ClosingTheJob_AlsoTerminatesChildren()
    {
        var result = Path.Combine(Path.GetTempPath(), "xapper-child-" + Guid.NewGuid() + ".txt");
        var powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        var script = $"$p=Start-Process ($env:SystemRoot+'\\System32\\ping.exe') -ArgumentList '-t','127.0.0.1' -PassThru -WindowStyle Hidden; [IO.File]::WriteAllText('{result.Replace("'", "''")}',[string]$p.Id); Start-Sleep 60";
        try
        {
            using var app = IsolatedDesktopApp.Start(IsolatedDesktopApp.NextDesktopName(), powershell,
                "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                Path.GetTempPath(), new Dictionary<string, string>());
            var watch = Stopwatch.StartNew();
            while (!File.Exists(result) && watch.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(50);
            using var child = Process.GetProcessById(int.Parse(await File.ReadAllTextAsync(result)));
            _ = child.Handle;
            app.Dispose();
            Assert.True(child.WaitForExit(5000));
        }
        finally { File.Delete(result); }
    }

    #endregion
}
