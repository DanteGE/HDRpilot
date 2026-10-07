namespace HdrPilot.Core.Games;

// PLATZHALTER für die Arbeitskopie phase3/session. Die echte Fassung kommt aus phase2/xbox-manual;
// beim Zusammenführen wird diese Datei durch sie ersetzt.
public static class HelperExecutables
{
    public static IReadOnlyList<string> DefaultPatterns { get; } = ["*crash*.exe", "vc_redist*.exe", "unins*.exe"];

    public static bool IsHelper(string exeFileNameOrPath, IEnumerable<string>? patterns = null)
    {
        var name = Path.GetFileName(exeFileNameOrPath);
        foreach (var pattern in patterns ?? DefaultPatterns)
        {
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$";
            if (System.Text.RegularExpressions.Regex.IsMatch(name, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
