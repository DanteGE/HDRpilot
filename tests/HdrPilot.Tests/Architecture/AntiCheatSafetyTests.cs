using System.Text;
using System.Text.RegularExpressions;

namespace HdrPilot.Tests.Architecture;

/// <summary>
/// HdrPilot darf nichts tun, was Anti-Cheat-Systeme als Eingriff in ein Spiel werten (E65–E67): kein Speicherzugriff,
/// kein Einschleusen, keine Hooks, keine Debug- oder Treiber-Schnittstellen, keine Handles auf fremde Prozesse außer dem
/// Rückfall der Pfadabfrage, kein Ändern von Priorität oder Prozessorzuteilung, kein Beenden oder Anhalten fremder
/// Prozesse, künstliche Tastendrücke nur in der abschaltbaren Notlösung. Der Test durchsucht den Quelltext unter src.
/// </summary>
public sealed class AntiCheatSafetyTests
{
    /// <summary>Regel, Begründung und ein Beispiel, das die Regel finden muss (Gegenprobe).</summary>
    private static readonly (string Pattern, string Why, string Sample)[] Forbidden =
    [
        (@"(Read|Write)ProcessMemory\b", "liest oder schreibt fremden Speicher", "Toolhelp32ReadProcessMemory(pid, a, b, n, out _);"),
        (@"\b(Nt|Zw)(Read|Write)VirtualMemory\b", "liest oder schreibt fremden Speicher", "NtReadVirtualMemory(h, a, b, n, out _);"),
        (@"\bVirtualAllocEx\b", "reserviert Speicher in fremden Prozessen", "VirtualAllocEx(h, 0, 4096, 0x3000, 0x40);"),
        (@"\b(CreateRemoteThread|NtCreateThreadEx|RtlCreateUserThread|QueueUserAPC)\b", "startet Code in fremden Prozessen", "CreateRemoteThread(h, 0, 0, f, 0, 0, out _);"),
        (@"\bSetWindowsHookEx", "globaler Hook", "SetWindowsHookExW(13, proc, mod, 0);"),
        (@"\bSetWinEventHook\b", "Hook auf fremde Fenster", "SetWinEventHook(3, 3, 0, cb, 0, 0, 0);"),
        (@"\bDebugActiveProcess\b", "Debugger", "DebugActiveProcess(pid);"),
        (@"\bNtQueryInformationProcess\b", "Abfrage über ein Prozess-Handle", "NtQueryInformationProcess(h, 0, p, n, out _);"),
        (@"\bEnumProcessModules", "Modulliste braucht PROCESS_VM_READ", "EnumProcessModulesEx(h, m, n, out _, 3);"),
        (@"\bTH32CS_SNAP(MODULE|ALL)", "Modulliste braucht PROCESS_VM_READ", "CreateToolhelp32Snapshot(TH32CS_SNAPMODULE32, pid);"),
        (@"\bDeviceIoControl\b", "Treiberzugriff", "DeviceIoControl(dev, code, i, n, o, m, out _, 0);"),
        (@"\b(CreateService[AW]?|NtLoadDriver|ZwLoadDriver)\b", "Dienst oder Treiber installieren", "CreateServiceA(scm, name, name, 0xF01FF, 1, 3, 1, path, null, 0, null, null, null);"),
        (@"\b(SeDebugPrivilege|AdjustTokenPrivileges|RtlAdjustPrivilege)\b", "Rechte erhöhen", "RtlAdjustPrivilege(20, true, false, out _);"),
        (@"\bPROCESS_VM_(READ|WRITE|OPERATION)\b", "Speicherrechte auf fremde Prozesse", "OpenProcess(PROCESS_VM_READ, false, pid);"),
        (@"\bPROCESS_(ALL_ACCESS|SET_INFORMATION|SUSPEND_RESUME|TERMINATE|DUP_HANDLE|CREATE_THREAD)\b", "mehr Rechte auf fremde Prozesse", "const uint PROCESS_TERMINATE = 1;"),
        (@"\bPROCESS_QUERY_INFORMATION\b", "mehr als das kleinste Abfragerecht", "OpenProcess(PROCESS_QUERY_INFORMATION, false, pid);"),
        (@"\b(Nt|Zw)OpenProcess\b", "Handle auf fremde Prozesse am Rückfall vorbei", "NtOpenProcess(out h, 0x1000, ref attr, ref cid);"),
        (@"\b(Nt)?OpenThread\b", "Handle auf fremde Threads", "OpenThread(0x2, false, tid);"),
        (@"\b(DuplicateHandle|NtDuplicateObject)\b", "fremde Handles übernehmen", "DuplicateHandle(src, h, me, out _, 0, false, 2);"),
        (@"\b(Nt)?TerminateProcess\b", "beendet fremde Prozesse", "TerminateProcess(h, 1);"),
        (@"\.Kill\s*\(", "beendet fremde Prozesse", "game.Kill();"),
        (@"\b(Nt|Zw)(Suspend|Resume)Process\b|\b(Suspend|Resume)Thread\b", "hält fremde Prozesse an", "NtSuspendProcess(h);"),
        (@"\b(Nt)?SetPriorityClass\b|\bSetProcessAffinityMask\b|\bSetProcessDefaultCpuSets\b|\bSetThread(Priority|AffinityMask|SelectedCpuSets)\b|\bNtSetInformationProcess\b",
            "ändert Priorität oder Prozessorzuteilung", "SetPriorityClass(h, 0x80);"),
        (@"\.(PriorityClass|ProcessorAffinity|PriorityBoostEnabled)\b", "ändert Priorität oder Prozessorzuteilung", "Process.Start(info)!.PriorityClass = ProcessPriorityClass.High;"),
        (@"\bProcess\.GetProcess(es|ById|esByName)\b", "System.Diagnostics.Process öffnet fremde Prozesse mit mehr Rechten", "using var p = Process.GetProcessById(42);"),
        (@"\.MainModule\b|\.Modules\b", "liest Module eines Prozesses", "var m = p.MainModule;"),
        (@"\bWaitForExit", "hält ein Handle auf einen fremden Prozess offen", "await Process.Start(info)!.WaitForExitAsync();"),
        (@"\.Exited\s*\+=", "hält ein Handle auf einen fremden Prozess offen", "game.Exited += OnExit;"),
        (@"\b(keybd_event|mouse_event)\b", "künstliche Eingaben", "keybd_event(0x5B, 0, 0, 0);"),
        (@"\b(GetAsyncKeyState|RegisterRawInputDevices)\b", "liest Eingaben mit", "GetAsyncKeyState(0x01);"),
    ];

    [Fact]
    public void Source_contains_no_forbidden_techniques()
    {
        var violations = Scan(SourceFiles());
        Assert.True(violations.Count == 0, "Verbotene Techniken:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Every_rule_finds_its_sample()
    {
        // Gegenprobe: jede Regel findet ihr Beispiel, auch hinter einer Zeichenkette mit "//".
        foreach (var (pattern, _, sample) in Forbidden)
        {
            var line = "var url = \"https://example.org\"; " + sample;
            var violations = Scan(new Dictionary<string, string> { ["Sample.cs"] = line });
            Assert.True(violations.Count > 0, $"Regel {pattern} findet ihr Beispiel nicht: {sample}");
        }

        Assert.Empty(Scan(new Dictionary<string, string> { ["Sample.cs"] = "// ReadProcessMemory steht nur im Kommentar\n/* game.Kill(); */ var x = 1;" }));
    }

    [Theory]
    [InlineData("var a = 1; // Kommentar", "var a = 1; ")]
    [InlineData("var u = \"https://x\"; Foo();", "var u = \"https://x\"; Foo();")]
    [InlineData("var p = @\"C:\\a\\\"; // x", "var p = @\"C:\\a\\\"; ")]
    [InlineData("var c = '/'; var d = 2; // x", "var c = '/'; var d = 2; ")]
    [InlineData("var s = \"a\\\"//b\"; Bar(); // c", "var s = \"a\\\"//b\"; Bar(); ")]
    public void Comments_are_stripped_but_strings_kept(string line, string expected)
    {
        Assert.Equal(expected, WithoutComments(line));
    }

    [Fact]
    public void OpenProcess_only_in_path_fallback_and_only_with_smallest_right()
    {
        var calls = SourceFiles()
            .SelectMany(f => Regex.Matches(WithoutComments(f.Value), @"\bOpenProcess\s*\((?<args>[^)]*)\)").Select(m => (File: f.Key, Args: m.Groups["args"].Value)))
            .ToList();

        // Deklaration in ToolhelpNative und genau ein Aufruf im Rückfall der Pfadabfrage.
        var uses = calls.Where(c => !c.File.EndsWith("ToolhelpNative.cs", StringComparison.OrdinalIgnoreCase)).ToList();
        var use = Assert.Single(uses);
        Assert.EndsWith("ProcessImagePathResolver.cs", use.File, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("PROCESS_QUERY_LIMITED_INFORMATION, false,", use.Args.Trim(), StringComparison.Ordinal);
        Assert.Equal(0x1000u, HdrPilot.Core.Native.ToolhelpNative.PROCESS_QUERY_LIMITED_INFORMATION);
    }

    [Fact]
    public void Synthetic_input_only_in_the_hotkey_fallback()
    {
        var files = SourceFiles().Where(f => Regex.IsMatch(WithoutComments(f.Value), @"\bSendInput\s*\(")).Select(f => Path.GetFileName(f.Key)).ToList();
        Assert.Equal(["InputNative.cs"], files);
    }

    [Fact]
    public void App_runs_without_admin_rights()
    {
        var manifest = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "HdrPilot.App", "app.manifest"));
        Assert.Matches(@"requestedExecutionLevel\s+level=""asInvoker""\s+uiAccess=""false""", manifest);
    }

    private static List<string> Scan(IReadOnlyDictionary<string, string> files)
    {
        var violations = new List<string>();
        foreach (var (file, text) in files)
        {
            var lines = WithoutComments(text).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var (pattern, why, _) in Forbidden)
                {
                    if (Regex.IsMatch(lines[i], pattern))
                    {
                        violations.Add($"{Path.GetFileName(file)}:{i + 1}: {why}: {lines[i].Trim()}");
                    }
                }
            }
        }

        return violations;
    }

    /// <summary>
    /// Entfernt //- und /* */-Kommentare (die Klassenbeschreibungen nennen bewusst, was nicht passiert), lässt
    /// Zeichenketten und Zeichen stehen, damit Code hinter einer URL nicht übersehen wird. Zeilenumbrüche bleiben
    /// erhalten, damit Zeilennummern stimmen.
    /// </summary>
    private static string WithoutComments(string text)
    {
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            var next = i + 1 < text.Length ? text[i + 1] : '\0';
            if (c == '/' && next == '/')
            {
                while (i < text.Length && text[i] != '\n')
                {
                    i++;
                }
            }
            else if (c == '/' && next == '*')
            {
                i += 2;
                while (i < text.Length && !(text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/'))
                {
                    if (text[i] == '\n')
                    {
                        sb.Append('\n');
                    }

                    i++;
                }

                i += 2;
            }
            else if (c == '"' || c == '\'')
            {
                var verbatim = c == '"' && i > 0 && (text[i - 1] == '@' || (text[i - 1] == '$' && i > 1 && text[i - 2] == '@'));
                sb.Append(c);
                i++;
                while (i < text.Length)
                {
                    var s = text[i];
                    sb.Append(s);
                    i++;
                    if (!verbatim && s == '\\' && i < text.Length)
                    {
                        sb.Append(text[i]);
                        i++;
                    }
                    else if (s == c)
                    {
                        if (verbatim && i < text.Length && text[i] == '"')
                        {
                            sb.Append('"');
                            i++;
                            continue;
                        }

                        break;
                    }
                    else if (s == '\n' && !verbatim)
                    {
                        break;
                    }
                }
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

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
