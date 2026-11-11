using System.Globalization;

namespace Bomca.Cli;

/// <summary>Small dependency-free argument parser: verb + --key value / --flag pairs.</summary>
public sealed class Args
{
    public string Verb { get; private init; } = "";
    public List<string> Positional { get; } = new();
    private readonly Dictionary<string, string> _opts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public static Args Parse(string[] argv)
    {
        if (argv.Length == 0)
            return new Args { Verb = "help" };

        var args = new Args { Verb = argv[0].ToLowerInvariant() };
        for (var i = 1; i < argv.Length; i++)
        {
            var token = argv[i];
            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var name = token[2..];
                string? value = null;
                var eq = name.IndexOf('=');
                if (eq >= 0)
                {
                    value = name[(eq + 1)..];
                    name = name[..eq];
                }
                else if (i + 1 < argv.Length && !argv[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    value = argv[++i];
                }

                if (value is null) args._flags.Add(name);
                else args._opts[name] = value;
            }
            else
            {
                args.Positional.Add(token);
            }
        }
        return args;
    }

    public bool Has(string name) => _flags.Contains(name) || _opts.ContainsKey(name);

    public string Get(string name, string fallback = "")
        => _opts.TryGetValue(name, out var v) ? v : fallback;

    public string? GetOrNull(string name) => _opts.TryGetValue(name, out var v) ? v : null;

    public double GetDouble(string name, double fallback)
        => _opts.TryGetValue(name, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : fallback;

    public int GetInt(string name, int fallback)
        => _opts.TryGetValue(name, out var v) && int.TryParse(v, out var n) ? n : fallback;

    public bool GetBool(string name, bool fallback)
    {
        if (_flags.Contains(name)) return true;
        if (!_opts.TryGetValue(name, out var v)) return false;
        return v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1" || v.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> GetList(string name, char sep = ',')
    {
        var raw = GetOrNull(name);
        if (string.IsNullOrWhiteSpace(raw)) return Array.Empty<string>();
        return raw.Split(sep, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public IReadOnlyList<double> GetDoubleList(string name, char sep = ',')
        => GetList(name, sep).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToList();
}
