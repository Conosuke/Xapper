namespace E2E.Common;

/// <summary>
/// Marks a static class as a setup: the steps that bring the app to the state a group of cases starts
/// from - launching it, and whatever every one of those cases needs before its own steps. The class
/// holds them in <c>public static Task Run(Session x)</c>, and a case names its setup in cases.json.
/// The runner runs a setup once and then every case that names it in the same running app.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class SetupAttribute(string name) : Attribute
{
    /// <summary>The setup's name, as the "setup" field in cases.json refers to it.</summary>
    public string Name { get; } = name;
}
