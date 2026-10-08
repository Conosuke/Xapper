namespace E2E.Common;

/// <summary>
/// Marks a static class as the E2E case with the given id. The class holds the case's steps in
/// <c>public static Task Run(Session x)</c>, and the id must also be listed in cases.json.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CaseAttribute(string id) : Attribute
{
    /// <summary>The case's id, for example "C01".</summary>
    public string Id { get; } = id;
}
