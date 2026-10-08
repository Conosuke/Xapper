// Runs this app's E2E cases: every case in cases.json, or only the ids given as arguments
// (dotnet run --project e2e -- C02 C05). Nothing runs unless cases.json and the code agree.
// Each case names a setup; the runner runs a setup once and then every case that names it in the same
// running app, sets up again after a failed case or before a case marked "fresh", and closes the app at the end.
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using E2E.Common;

// The folder baked in at build time; when the build was copied to another machine that path is gone,
// so fall back to the folder the run was started from.
var baked = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "E2EFolder")?.Value;
var folder = baked is not null && Directory.Exists(baked) ? baked : Environment.CurrentDirectory;

try
{
    var loaded = EnvFile.Load(Path.Combine(folder, "e2e.env"), folder);
    if (loaded.Count > 0)
        Console.WriteLine($"e2e.env set {string.Join(", ", loaded)}");
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

CaseList list;
try
{
    list = JsonSerializer.Deserialize<CaseList>(
               File.ReadAllText(Path.Combine(folder, "cases.json")),
               new JsonSerializerOptions
               {
                   PropertyNameCaseInsensitive = true,
                   ReadCommentHandling = JsonCommentHandling.Skip,
                   AllowTrailingCommas = true
               })
           ?? new CaseList();
}
catch (Exception ex) when (ex is IOException or JsonException)
{
    Console.Error.WriteLine($"cases.json could not be read, so nothing was run: {ex.Message}");
    return 1;
}

// The cases and setups in code: static classes marked [Case("Cxx")] and [Setup("name")].
var coded = new List<(string Id, Type Type)>();
var setups = new List<(string Name, Type Type)>();
foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
{
    if (type.GetCustomAttribute<CaseAttribute>() is { } mark)
        coded.Add((mark.Id, type));
    if (type.GetCustomAttribute<SetupAttribute>() is { } setup)
        setups.Add((setup.Name, type));
}

var problems = new List<string>();
foreach (var twice in list.Cases.GroupBy(c => c.Id).Where(g => g.Count() > 1))
    problems.Add($"{twice.Key} is listed {twice.Count()} times in cases.json");
foreach (var twice in coded.GroupBy(c => c.Id).Where(g => g.Count() > 1))
    problems.Add($"{twice.Key} is marked on {twice.Count()} classes: {string.Join(", ", twice.Select(c => c.Type.Name))}");
foreach (var c in list.Cases)
{
    if (coded.All(k => k.Id != c.Id))
        problems.Add($"{c.Id} is listed in cases.json but no class is marked [Case(\"{c.Id}\")]");
    if (!c.File.StartsWith(c.Id + "-", StringComparison.OrdinalIgnoreCase))
        problems.Add($"{c.Id}: its file {c.File} does not start with {c.Id}-");
    if (!File.Exists(Path.Combine(folder, "Cases", c.File)))
        problems.Add($"{c.Id}: Cases/{c.File} does not exist");
    if (setups.All(s => s.Name != c.Setup))
        problems.Add($"{c.Id}: its setup \"{c.Setup}\" has no class marked [Setup(\"{c.Setup}\")]");
}
foreach (var twice in setups.GroupBy(s => s.Name).Where(g => g.Count() > 1))
    problems.Add($"setup {twice.Key} is marked on {twice.Count()} classes: {string.Join(", ", twice.Select(s => s.Type.Name))}");
foreach (var (name, type) in setups)
{
    if (RunOf(type) is null)
        problems.Add($"setup {name} ({type.Name}) has no public static Task Run(Session x)");
}
foreach (var (id, type) in coded)
{
    if (list.Cases.All(c => c.Id != id))
        problems.Add($"{id} ({type.Name}) is not listed in cases.json");
    if (RunOf(type) is null)
        problems.Add($"{id} ({type.Name}) has no public static Task Run(Session x)");
}
var listedFiles = list.Cases.Select(c => c.File).ToHashSet(StringComparer.OrdinalIgnoreCase);
var casesFolder = Path.Combine(folder, "Cases");
if (Directory.Exists(casesFolder))
{
    foreach (var path in Directory.GetFiles(casesFolder, "*.cs"))
    {
        var name = Path.GetFileName(path);
        if (Regex.IsMatch(name, @"^C\d+-", RegexOptions.IgnoreCase) && !listedFiles.Contains(name))
            problems.Add($"Cases/{name} is not listed in cases.json");
    }
}
foreach (var id in args.Where(id => list.Cases.All(c => c.Id != id)))
    problems.Add($"{id} is not listed in cases.json");

if (problems.Count > 0)
{
    Console.Error.WriteLine("cases.json and the cases in code disagree, so nothing was run:");
    foreach (var problem in problems)
        Console.Error.WriteLine("  " + problem);
    return 1;
}

var app = Environment.GetEnvironmentVariable("APP_EXE");
var server = Environment.GetEnvironmentVariable("XAPPER_MCP");
if (app is null || server is null)
{
    Console.Error.WriteLine("Set APP_EXE and XAPPER_MCP, in the environment or in e2e.env.");
    return 1;
}

// Cases that share a setup run together, so the app starts once for all of them; within a setup they
// keep their order in cases.json.
var chosen = (args.Length == 0 ? list.Cases : list.Cases.Where(c => args.Contains(c.Id)))
             .GroupBy(c => c.Setup)
             .SelectMany(group => group)
             .ToList();
var results = new List<(CaseEntry Case, bool Passed, TimeSpan Took, string Note)>();

await using var session = new Session(app);
try
{
    await session.ConnectXapperAsync(server);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"The Xapper server did not start, so no case ran: {ex.Message}");
    return 1;
}

string? ready = null;               // the setup the running app is in; null when it must be set up again
var brokenSetups = new HashSet<string>();
try
{
    foreach (var c in chosen)
    {
        Console.WriteLine($"=== {c.Id} [{c.Feature}] {c.Title}");
        var clock = Stopwatch.StartNew();

        if (brokenSetups.Contains(c.Setup))
        {
            results.Add((c, false, clock.Elapsed, $"not run: setup {c.Setup} failed"));
            continue;
        }

        if (ready != c.Setup || c.Fresh)
        {
            Scenario.CloseApp(app);
            var setupRun = RunOf(setups.First(s => s.Name == c.Setup).Type)
                           ?? throw new InvalidOperationException($"setup {c.Setup} has no Run method.");
            var setUp = await Scenario.RunStepsAsync(session, $"setup {c.Setup}",
                                                     Path.Combine(folder, "results", "setup-" + c.Setup), setupRun);
            if (!setUp)
            {
                ready = null;
                brokenSetups.Add(c.Setup);
                results.Add((c, false, clock.Elapsed, $"not run: setup {c.Setup} failed"));
                continue;
            }
            ready = c.Setup;
        }

        var caseRun = RunOf(coded.First(k => k.Id == c.Id).Type)
                      ?? throw new InvalidOperationException($"{c.Id} has no Run method.");
        var passed = await Scenario.RunStepsAsync(session, c.Id, Path.Combine(folder, "results", c.Id), caseRun);

        // A failed case can leave the app anywhere, so the next case starts from a fresh setup.
        if (!passed)
            ready = null;
        results.Add((c, passed, clock.Elapsed, ""));
    }
}
finally
{
    Scenario.CloseApp(app);
}

Console.WriteLine();
Console.WriteLine("id    result  time    setup / feature / title");
foreach (var (c, passed, took, note) in results)
{
    var line = $"{c.Id,-5} {(passed ? "PASS" : "FAIL"),-6}  {took.TotalSeconds,5:F1}s  {c.Setup} / {c.Feature} / {c.Title}";
    Console.WriteLine(note.Length == 0 ? line : $"{line}  ({note})");
}
Console.WriteLine($"{results.Count(r => r.Passed)} of {results.Count} passed. Recordings and failure screenshots: {Path.Combine(folder, "results")}");

return results.All(r => r.Passed) ? 0 : 1;

// The steps of a case or setup as a delegate, or null when the class has no public static Task Run(Session).
static Func<Session, Task>? RunOf(Type type)
{
    var method = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static, [typeof(Session)]);
    return method is not null && method.ReturnType == typeof(Task)
        ? method.CreateDelegate<Func<Session, Task>>()
        : null;
}

/// <summary>The list in cases.json.</summary>
public sealed class CaseList
{
    public List<CaseEntry> Cases { get; set; } = [];
}

/// <summary>One case in cases.json.</summary>
public sealed class CaseEntry
{
    public string Id { get; set; } = "";
    public string Feature { get; set; } = "";
    public string Title { get; set; } = "";
    public string File { get; set; } = "";
    public string Setup { get; set; } = "";
    public bool Fresh { get; set; }
    public List<string> Checks { get; set; } = [];
}
