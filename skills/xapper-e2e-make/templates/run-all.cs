#:package YamlDotNet@18.1.0
#:property PublishAot=false
// Runs the E2E cases listed in cases.yaml: all of them, or only the ids given as arguments
// (dotnet run e2e/run-all.cs -- C02 C05). Nothing runs unless the list and the case files agree.
using System.Diagnostics;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

var folder = AppContext.GetData("EntryPointFileDirectoryPath") as string ?? Environment.CurrentDirectory;
var list = new DeserializerBuilder()
               .WithNamingConvention(CamelCaseNamingConvention.Instance)
               .IgnoreUnmatchedProperties()
               .Build()
               .Deserialize<CaseList>(File.ReadAllText(Path.Combine(folder, "cases.yaml")))
           ?? new CaseList();

var problems = new List<string>();
foreach (var twice in list.Cases.GroupBy(c => c.Id).Where(g => g.Count() > 1))
    problems.Add($"{twice.Key} is listed {twice.Count()} times");
foreach (var c in list.Cases)
{
    if (!c.File.StartsWith(c.Id + "-", StringComparison.Ordinal))
        problems.Add($"{c.Id}: its file {c.File} does not start with {c.Id}-");
    if (!File.Exists(Path.Combine(folder, c.File)))
        problems.Add($"{c.Id}: {c.File} does not exist");
}
var listed = list.Cases.Select(c => c.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
foreach (var path in Directory.GetFiles(folder, "C*.cs"))
{
    var name = Path.GetFileName(path);
    if (Regex.IsMatch(name, @"^C\d+-") && !listed.Contains(name))
        problems.Add($"{name} is not listed in cases.yaml");
}
foreach (var id in args.Where(id => list.Cases.All(c => c.Id != id)))
    problems.Add($"{id} is not listed in cases.yaml");

if (problems.Count > 0)
{
    Console.Error.WriteLine("cases.yaml and the case files disagree, so nothing was run:");
    foreach (var problem in problems)
        Console.Error.WriteLine("  " + problem);
    return 1;
}

var chosen = args.Length == 0 ? list.Cases : list.Cases.Where(c => args.Contains(c.Id)).ToList();
var results = new List<(Case Case, bool Passed, TimeSpan Took)>();
foreach (var c in chosen)
{
    Console.WriteLine($"=== {c.Id} [{c.Feature}] {c.Title}");
    var clock = Stopwatch.StartNew();
    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
    start.ArgumentList.Add("run");
    start.ArgumentList.Add(Path.Combine(folder, c.File));
    using var run = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start.");
    await run.WaitForExitAsync();
    results.Add((c, run.ExitCode == 0, clock.Elapsed));
}

Console.WriteLine();
Console.WriteLine("id    result  time    feature / title");
foreach (var (c, passed, took) in results)
    Console.WriteLine($"{c.Id,-5} {(passed ? "PASS" : "FAIL"),-6}  {took.TotalSeconds,5:F1}s  {c.Feature} / {c.Title}");
Console.WriteLine($"{results.Count(r => r.Passed)} of {results.Count} passed. Recordings and failure screenshots: {Path.Combine(folder, "results")}");

return results.All(r => r.Passed) ? 0 : 1;

/// <summary>The list in cases.yaml.</summary>
public sealed class CaseList
{
    public List<Case> Cases { get; set; } = [];
}

/// <summary>One case in cases.yaml.</summary>
public sealed class Case
{
    public string Id { get; set; } = "";
    public string Feature { get; set; } = "";
    public string Title { get; set; } = "";
    public string File { get; set; } = "";
    public List<string> Checks { get; set; } = [];
}
