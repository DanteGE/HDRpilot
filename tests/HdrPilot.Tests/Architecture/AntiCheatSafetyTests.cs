using System.Text.RegularExpressions;

namespace HdrPilot.Tests.Architecture;

/// <summary>
/// HdrPilot darf nichts tun, was Anti-Cheat-Systeme als Eingriff in ein Spiel werten (E65, E66): kein Speicherzugriff,
/// kein Einschleusen, keine Hooks, keine Debug- oder Treiber-Schnittstellen, keine Handles auf fremde Prozesse außer dem
/// Rückfall der Pfadabfrage, künstliche Tastendrücke nur in der abschaltbaren Notlösung. Der Test durchsucht den
/// Quelltext unter src.
/// </summary>
public sealed class AntiCheatSafetyTests
{
    private static readonly (string Pattern, string Why)[] Forbidden =
    [
        (@"\bReadProcessMemory\b", "liest fremden Speicher"),
        (@"\bWriteProcessMemory\b", "schreibt fremden Speicher"),
        (@"\bVirtualAllocEx\b", "reserviert Speicher in fremden Prozessen"),
        (@"\bCreateRemoteThread\b", "startet Code in fremden Prozessen"),
        (@"\bNtCreateThreadEx\b", "startet Code in fremden Prozessen"),
        (@"\bSetWindowsHookEx", "globaler Hook"),
        (@"\bSetWinEventHook\b", "Hook auf fremde Fenster"),
        (@"\bDebugActiveProcess\b", "Debugger"),
        (@"\bNtQueryInformationProcess\b", "Abfrage über ein Prozess-Handle"),
        (@"\bEnumProcessModules", "Modulliste braucht PROCESS_VM_READ"),
        (@"\bTH32CS_SNAPMODULE\b", "Modulliste braucht PROCESS_VM_READ"),
        (@"\bDeviceIoControl\b", "Treiberzugriff"),
        (@"\bCreateServiceW?\b", "Dienst oder Treiber installieren"),
        (@"\bSeDebugPrivilege\b", "Debug-Recht"),
        (@"\bAdjustTokenPrivileges\b", "Rechte erhöhen"),
        (@"\bPROCESS_VM_(READ|WRITE|OPERATION)\b", "Speicherrechte auf fremde Prozesse"),
        (@"\bPROCESS_ALL_ACCESS\b", "Vollzugriff auf fremde Prozesse"),
        (@"\bPROCESS_QUERY_INFORMATION\b", "mehr als das kleinste Abfragerecht"),
        (@"\bProcess\.GetProcess(es|ById|esByName)\b", "System.Diagnostics.Process öffnet fremde Prozesse mit mehr Rechten"),
        (@"\.MainModule\b", "liest Module eines Prozesses"),
        (@"\.Modules\b", "liest Module eines Prozesses"),
        (@"\bWaitForExit\b", "hält ein Handle auf einen fremden Prozess offen"),
        (@"\bkeybd_event\b", "künstliche Tastendrücke"),
        (@"\bmouse_event\b", "künstliche Mausbewegungen"),
        (@"\bGetAsyncKeyState\b", "liest die Tastatur mit"),
        (@"\bRegisterRawInputDevices\b", "liest Eingabegeräte mit"),
    ];

    [Fact]
    public void Source_contains_no_forbidden_techniques()
    {
        var violations = Scan(SourceFiles(), Forbidden);
        Assert.True(violations.Count == 0, "Verbotene Techniken:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Scanner_detects_forbidden_techniques()
    {
        // Gegenprobe: der Scanner findet, was er finden soll.
        var sample = new Dictionary<string, string>
        {
            ["Sample.cs"] = "var ok = ReadProcessMemory(h, a, b, c, out _);\nusing var p = Process.GetProcessById(42);\nvar m = p.MainModule;",
        };
        var violations = Scan(sample, Forbidden);
        Assert.Equal(3, violations.Count);
    }

    [Fact]
    public void OpenProcess_only_in_path_fallback_and_only_with_smallest_right()
    {
        var calls = SourceFiles()
            .SelectMany(f => Regex.Matches(WithoutComments(f.Value), @"\bOpenProcess\((?<args>[^)]*)\)").Select(m => (File: f.Key, Args: m.Groups["args"].Value)))
            .ToList();

        // Deklaration in ToolhelpNative und genau ein Aufruf im Rückfall der Pfadabfrage.
        var uses = calls.Where(c => !c.File.EndsWith("ToolhelpNative.cs", StringComparison.OrdinalIgnoreCase)).ToList();
        var use = Assert.Single(uses);
        Assert.EndsWith("ProcessImagePathResolver.cs", use.File, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("PROCESS_QUERY_LIMITED_INFORMATION, false,", use.Args.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public void Synthetic_input_only_in_the_hotkey_fallback()
    {
        var files = SourceFiles().Where(f => Regex.IsMatch(WithoutComments(f.Value), @"\bSendInput\(")).Select(f => Path.GetFileName(f.Key)).ToList();
        Assert.Equal(["InputNative.cs"], files);
    }

    [Fact]
    public void App_runs_without_admin_rights()
    {
        var manifest = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "HdrPilot.App", "app.manifest"));
        Assert.Matches(@"requestedExecutionLevel\s+level=""asInvoker""\s+uiAccess=""false""", manifest);
    }

    private static List<string> Scan(IReadOnlyDictionary<string, string> files, (string Pattern, string Why)[] rules)
    {
        var violations = new List<string>();
        foreach (var (file, text) in files)
        {
            var lines = text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var code = StripComment(lines[i]);
                foreach (var (pattern, why) in rules)
                {
                    if (Regex.IsMatch(code, pattern))
                    {
                        violations.Add($"{Path.GetFileName(file)}:{i + 1}: {why}: {code.Trim()}");
                    }
                }
            }
        }

        return violations;
    }

    /// <summary>Kommentare zählen nicht (die Klassenbeschreibungen nennen bewusst, was nicht passiert).</summary>
    private static string StripComment(string line)
    {
        var index = line.IndexOf("//", StringComparison.Ordinal);
        return index < 0 ? line : line[..index];
    }

    private static string WithoutComments(string text) => string.Join("\n", text.Split('\n').Select(StripComment));

    private static Dictionary<string, string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => f, File.ReadAllText);

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HdrPilot.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("HdrPilot.slnx nicht gefunden.");
    }
}
