namespace DominioDeLaSierra.Infrastructure.Persistence;

internal static class LocalEnvFile
{
    public static string? Get(string key)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment;
        }

        foreach (var directory in WalkDirectories())
        {
            var envPath = Path.Combine(directory, ".env");
            if (!File.Exists(envPath))
            {
                continue;
            }

            foreach (var rawLine in File.ReadAllLines(envPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                var name = line[..separator].Trim();
                if (!string.Equals(name, key, StringComparison.Ordinal))
                {
                    continue;
                }

                var value = line[(separator + 1)..].Trim().Trim('"');
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
    }

    private static IEnumerable<string> WalkDirectories()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            yield return current.FullName;
            current = current.Parent;
        }
    }
}
