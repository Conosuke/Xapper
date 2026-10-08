using System.Diagnostics;

namespace E2E.Common;

/// <summary>
/// Runs one setup or one case inside the run's session: records it, runs its steps, saves a failure
/// screenshot when a step fails, and stops the recording. Launching and closing the app belong to the
/// setups and the runner, not to the steps of a case.
/// </summary>
public static class Scenario
{
    /// <summary>Runs one setup or case and returns whether every step passed.</summary>
    /// <param name="session">The run's session.</param>
    /// <param name="label">The case id or "setup &lt;name&gt;"; it labels output lines.</param>
    /// <param name="output">The folder for the recording and the failure screenshot.</param>
    /// <param name="steps">The steps to run.</param>
    public static async Task<bool> RunStepsAsync(Session session, string label, string output, Func<Session, Task> steps)
    {
        session.Label = label;
        session.Output = output;
        Directory.CreateDirectory(output);
        try
        {
            await session.Call("xapper_record_start", new { path = Path.Combine(output, "run.mp4") });
            await steps(session);
            Console.WriteLine($"[{label}] PASS");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{label}] FAIL {ex.Message}");
            // Screen mode, because a failing step often leaves a dialog or popup the render mode leaves out.
            await session.TryAsync("xapper_screenshot", new { savePath = Path.Combine(output, "failure.png"), mode = "screen" });
            return false;
        }
        finally
        {
            await session.TryAsync("xapper_record_stop");
            await session.EndStepAsync();
        }
    }

    /// <summary>
    /// Closes every instance of the app by name: launch can start the app and still report an error,
    /// and then there is no pid to go by. Only one instance runs at a time, so this is safe.
    /// </summary>
    public static void CloseApp(string app)
    {
        foreach (var leftover in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(app)))
        {
            try
            {
                leftover.Kill();
                leftover.WaitForExit(5000);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // It exited between being listed and being killed, or it cannot be closed from here.
                Console.Error.WriteLine($"could not close {leftover.ProcessName} ({leftover.Id}): {ex.Message}");
            }
        }
    }
}
