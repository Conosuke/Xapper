using E2E.Common;

namespace E2E.Setups;

/// <summary>
/// The state the cases that name "app-started" begin from. It only launches the app; extend it, or add
/// another setup next to it, with whatever those cases need before their own steps - so that no case
/// repeats it and the app starts once for all of them.
/// </summary>
[Setup("app-started")]
public static class AppStarted
{
    public static async Task Run(Session x)
    {
        await x.Call("xapper_launch", new { exePath = x.App });
    }
}
