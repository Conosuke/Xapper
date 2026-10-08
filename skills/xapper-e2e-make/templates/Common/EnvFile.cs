namespace E2E.Common;

/// <summary>
/// Reads e2e.env into this process's environment, so a developer machine can keep the paths a run needs in
/// a file while CI passes real environment variables. A variable that is already set wins over the file,
/// and every server the run starts afterwards inherits what was set here.
/// </summary>
public static class EnvFile
{
    /// <summary>Loads the file if it exists and returns the names it set.</summary>
    /// <param name="path">The e2e.env file.</param>
    /// <param name="e2eFolder">The folder that ${E2E} in a value stands for.</param>
    /// <exception cref="InvalidOperationException">A line that is neither a comment nor NAME=value.</exception>
    public static IReadOnlyList<string> Load(string path, string e2eFolder)
    {
        if (!File.Exists(path))
            return [];

        var set = new List<string>();
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var equals = line.IndexOf('=');
            if (equals <= 0)
                throw new InvalidOperationException($"{path}: \"{raw}\" is not NAME=value.");

            var name = line[..equals].Trim();
            if (Environment.GetEnvironmentVariable(name) is not null)
                continue;

            var value = line[(equals + 1)..].Trim().Trim('"').Replace("${E2E}", e2eFolder, StringComparison.Ordinal);
            Environment.SetEnvironmentVariable(name, value);
            set.Add(name);
        }

        return set;
    }
}
