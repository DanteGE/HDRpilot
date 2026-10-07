namespace HdrPilot.Cli;

/// <summary>Einfacher Argumentleser: Positionsargumente plus --schalter und --name wert.</summary>
public sealed class CliArguments
{
    private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
    {
        "--json", "--hotkey-fallback", "--verbose", "--help", "-h", "--pretty", "--candidates",
    };

    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    private CliArguments(List<string> positional) => Positional = positional;

    public IReadOnlyList<string> Positional { get; }

    public static CliArguments Parse(IReadOnlyList<string> args, out string? error)
    {
        error = null;
        var positional = new List<string>();
        var result = new CliArguments(positional);
        for (var i = 0; i < args.Count; i++)
        {
            var a = args[i];
            if (a.StartsWith('-') && a.Length > 1)
            {
                if (Flags.Contains(a))
                {
                    result._flags.Add(a);
                    continue;
                }

                var eq = a.IndexOf('=', StringComparison.Ordinal);
                if (eq > 0)
                {
                    result._options[a[..eq]] = a[(eq + 1)..];
                    continue;
                }

                if (i + 1 >= args.Count)
                {
                    error = $"Option {a} braucht einen Wert.";
                    return result;
                }

                result._options[a] = args[++i];
                continue;
            }

            positional.Add(a);
        }

        return result;
    }

    public bool Has(string flag) => _flags.Contains(flag);

    public string? Get(string option) => _options.TryGetValue(option, out var v) ? v : null;

    public IEnumerable<string> UnknownOptions(params string[] known) =>
        _options.Keys.Where(k => !known.Contains(k, StringComparer.OrdinalIgnoreCase));
}
