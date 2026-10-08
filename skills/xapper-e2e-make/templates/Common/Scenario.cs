using System.Diagnostics;

namespace E2E.Common;

/// <summary>
/// Runs one E2E case from start to finish: checks the environment, connects to Xapper, records the
/// desktop, runs the case's own steps, and on the way out saves a failure screenshot, stops the
/// recording and closes the app. A case holds only its steps.
/// </summary>
public static class Scenario
{
    /// <summary>Runs one case and returns its exit code: 0 passed, 1 failed.</summary>
    /// <param name="caseId">The case's id, for example "C01".</param>
    /// <param name="e2eFolder">The e2e folder; the case's output goes to results/&lt;id&gt; under it.</param>
    /// <param name="steps">The case's own steps.</param>
    public static async Task<int> RunAsync(string caseId, string e2eFolder, Func<Session, Task> steps)
    {
        var app = Environment.GetEnvironmentVariable("APP_EXE");
        var server = Environment.GetEnvironmentVariable("XAPPER_MCP");
        if (app is null || server is null)
        {
            Console.Error.WriteLine($"[{caseId}] Set APP_EXE and XAPPER_MCP, in the environment or in e2e.env.");
            return 1;
        }

        var output = Path.Combine(e2eFolder, "results", caseId);
        Directory.CreateDirectory(output);

        await using var session = new Session(caseId, app, output);
        var exitCode = 0;
        try
        {
            await session.ConnectXapperAsync(server);
            await session.Call("xapper_record_start", new { path = Path.Combine(output, "run.mp4") });
            await steps(session);
            Console.WriteLine($"[{caseId}] PASS");
        }
        catch (Exception ex)
        {
            exitCode = 1;
            Console.Error.WriteLine($"[{caseId}] FAIL {ex.Message}");
            // Screen mode, because a failing step often leaves a dialog or popup the render mode leaves out.
            await session.TryAsync("xapper_screenshot", new { savePath = Path.Combine(output, "failure.png"), mode = "screen" });
        }
        finally
        {
            await session.TryAsync("xapper_record_stop");
            CloseApp(app);
        }

        return exitCode;
    }

    /// <summary>
    /// Closes every instance of the app by name: launch can start the app and still report an error,
    /// and then there is no pid to go by. Only one instance runs at a time, so this is safe.
    /// </summary>
    private static void CloseApp(string app)
    {
        foreach (var leftover in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(app)))
        {
            try
            {
                leftover.Kill();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // It exited between being listed and being killed, or it cannot be closed from here.
                Console.Error.WriteLine($"could not close {leftover.ProcessName} ({leftover.Id}): {ex.Message}");
            }
        }
    }
}
