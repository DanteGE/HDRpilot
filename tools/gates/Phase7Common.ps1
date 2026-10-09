# Gemeinsame Hilfen für die Setup-Gates (Phase 7, E83; per Dot-Sourcing geladen, setzt keine Note/Check-Funktionen).
#   - Setup-Datei prüfen: Format wie src\HdrPilot.App\Setup\SetupBundle.cs (EXE | Zip | Abschluss von 64 Bytes)
#   - Testablage (--test-root): Orte, Registry-Zweig und Kennung der Einzelinstanz genau wie SetupLocations.ForTestRoot
#     und SingleInstance.IdFor
#   - Stand der echten Installation vor und nach einem Prüflauf (darf sich nie ändern)
# Fremde Prozesse werden nie geöffnet: Prozesse nur über ihren Namen (HDRpilot, HDRpilot-Uninstall) gesucht.
$script:Phase7Root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$script:PublishDir = Join-Path $Phase7Root 'artifacts\publish'
$script:PortableDir = Join-Path $PublishDir 'HDRpilot'
$script:PayloadEntries = @('README.md', 'THIRD-PARTY-NOTICES.txt', 'cli/hdrpilot.exe')
$script:InstalledFiles = @('HDRpilot.exe', 'README.md', 'THIRD-PARTY-NOTICES.txt', 'cli\hdrpilot.exe')
$script:AsInvokerText = 'requestedExecutionLevel level="asInvoker"'
$script:RunValueName = [regex]::Match([IO.File]::ReadAllText((Join-Path $Phase7Root 'src\HdrPilot.App\Services\AutostartService.cs')), 'ValueName = "([^"]+)"').Groups[1].Value

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
if (-not ('HdrPilotGates.SetupFile' -as [type])) {
    Add-Type -ReferencedAssemblies ([IO.Compression.ZipArchive].Assembly.Location) -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace HdrPilotGates
{
    public sealed class SetupFooter
    {
        public long FileLength;
        public string Magic = "";
        public long ExeLength;
        public long PayloadLength;
        public string ExeSha256 = "";
    }

    public static class SetupFile
    {
        public const int FooterLength = 64;

        private static FileStream Open(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
        }

        public static SetupFooter ReadFooter(string path)
        {
            using (var fs = Open(path))
            {
                var result = new SetupFooter();
                result.FileLength = fs.Length;
                if (fs.Length < FooterLength)
                {
                    return result;
                }

                var footer = new byte[FooterLength];
                fs.Seek(-FooterLength, SeekOrigin.End);
                ReadExactly(fs, footer, footer.Length);
                result.Magic = Encoding.ASCII.GetString(footer, 0, 16);
                result.ExeLength = BitConverter.ToInt64(footer, 16);
                result.PayloadLength = BitConverter.ToInt64(footer, 24);
                result.ExeSha256 = Hex(footer, 32, 32);
                return result;
            }
        }

        public static string Sha256(string path, long offset, long length)
        {
            using (var fs = Open(path))
            using (var sha = SHA256.Create())
            {
                fs.Seek(offset, SeekOrigin.Begin);
                var buffer = new byte[1 << 20];
                var remaining = length;
                while (remaining > 0)
                {
                    var read = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read <= 0)
                    {
                        throw new EndOfStreamException("Datei kürzer als erwartet.");
                    }

                    sha.TransformBlock(buffer, 0, read, null, 0);
                    remaining -= read;
                }

                sha.TransformFinalBlock(buffer, 0, 0);
                return Hex(sha.Hash, 0, sha.Hash.Length);
            }
        }

        /// <summary>Position eines ASCII-Texts im Bereich [offset, offset + length), sonst -1.</summary>
        public static long IndexOf(string path, long offset, long length, string text)
        {
            var pattern = Encoding.ASCII.GetBytes(text);
            using (var fs = Open(path))
            {
                fs.Seek(offset, SeekOrigin.Begin);
                var buffer = new byte[(1 << 20) + pattern.Length];
                var carried = 0;
                var bufferStart = offset;
                var remaining = length;
                while (remaining > 0)
                {
                    var read = fs.Read(buffer, carried, (int)Math.Min(buffer.Length - carried, remaining));
                    if (read <= 0)
                    {
                        break;
                    }

                    remaining -= read;
                    var filled = carried + read;
                    for (var i = 0; i <= filled - pattern.Length; i++)
                    {
                        if (buffer[i] != pattern[0])
                        {
                            continue;
                        }

                        var j = 1;
                        while (j < pattern.Length && buffer[i + j] == pattern[j])
                        {
                            j++;
                        }

                        if (j == pattern.Length)
                        {
                            return bufferStart + i;
                        }
                    }

                    var keep = Math.Min(pattern.Length - 1, filled);
                    Buffer.BlockCopy(buffer, filled - keep, buffer, 0, keep);
                    bufferStart += filled - keep;
                    carried = keep;
                }

                return -1;
            }
        }

        /// <summary>Einträge des Zips im Bereich als "Name|SHA-256" (Namen wie gespeichert).</summary>
        public static string[] ZipEntryHashes(string path, long offset, long length)
        {
            if (offset < 0 || length <= 0 || length > int.MaxValue)
            {
                throw new InvalidDataException("Paketlänge ungültig.");
            }

            var bytes = new byte[length];
            using (var fs = Open(path))
            {
                fs.Seek(offset, SeekOrigin.Begin);
                ReadExactly(fs, bytes, bytes.Length);
            }

            var result = new List<string>();
            using (var zip = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read))
            using (var sha = SHA256.Create())
            {
                foreach (var entry in zip.Entries)
                {
                    using (var stream = entry.Open())
                    {
                        result.Add(entry.FullName + "|" + Hex(sha.ComputeHash(stream), 0, 32));
                    }
                }
            }

            return result.ToArray();
        }

        public static void FlipByte(string path, long position)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                fs.Seek(position, SeekOrigin.Begin);
                var value = fs.ReadByte();
                fs.Seek(position, SeekOrigin.Begin);
                fs.WriteByte((byte)(value ^ 0xFF));
            }
        }

        public static void CopyPrefix(string source, string destination, long length)
        {
            using (var input = Open(source))
            using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[1 << 20];
                var remaining = length;
                while (remaining > 0)
                {
                    var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read <= 0)
                    {
                        break;
                    }

                    output.Write(buffer, 0, read);
                    remaining -= read;
                }
            }
        }

        /// <summary>
        /// Wie SetupLocations.ForTestRoot und SingleInstance.IdFor: SHA-256 über UTF-8 des vollen Pfads (ohne Trenner am
        /// Ende, in Großbuchstaben), die ersten 16 Hex-Zeichen.
        /// </summary>
        public static string PathHash(string path)
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? "";
            if (full.Length > root.Length && (full.EndsWith("\\") || full.EndsWith("/")))
            {
                full = full.Substring(0, full.Length - 1);
            }

            using (var sha = SHA256.Create())
            {
                return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(full.ToUpperInvariant())), 0, 32).Substring(0, 16);
            }
        }

        /// <summary>Langer Pfad statt 8.3-Kurznamen (sonst rechneten .NET Framework und .NET verschieden).</summary>
        public static string LongPath(string path)
        {
            var buffer = new StringBuilder(32768);
            var length = GetLongPathNameW(path, buffer, (uint)buffer.Capacity);
            return length == 0 || length >= buffer.Capacity ? path : buffer.ToString();
        }

        /// <summary>Nachrichtenfenster der Einzelinstanz (HdrPilot.Commands.&lt;Kennung&gt;) vorhanden, also bereit für Befehle.</summary>
        public static bool CommandWindowExists(string id)
        {
            return FindWindowExW(new IntPtr(-3), IntPtr.Zero, null, "HdrPilot.Commands." + id) != IntPtr.Zero;
        }

        private static void ReadExactly(Stream stream, byte[] buffer, int count)
        {
            var done = 0;
            while (done < count)
            {
                var read = stream.Read(buffer, done, count - done);
                if (read <= 0)
                {
                    throw new EndOfStreamException("Datei kürzer als erwartet.");
                }

                done += read;
            }
        }

        private static string Hex(byte[] bytes, int offset, int count)
        {
            var sb = new StringBuilder(count * 2);
            for (var i = offset; i < offset + count; i++)
            {
                sb.Append(bytes[i].ToString("X2"));
            }

            return sb.ToString();
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetLongPathNameW(string shortPath, StringBuilder longPath, uint bufferLength);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowExW(IntPtr parent, IntPtr childAfter, string className, string windowName);
    }
}
'@
}

# --- Setup-Datei ---

function Get-ProjectVersion {
    $props = [xml](Get-Content (Join-Path $Phase7Root 'Directory.Build.props') -Raw)
    $v = [Version](@($props.Project.PropertyGroup) | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
    return '{0}.{1}.{2}' -f $v.Major, $v.Minor, [Math]::Max(0, $v.Build)
}

function Get-SetupPath([string]$version) { Join-Path $PublishDir "HDRpilot-Setup-$version.exe" }

function Get-Sha([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return '' }
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}

# Neu veröffentlichen, wenn die Setup-Datei fehlt oder älter ist als der Quelltext (src ohne bin/obj, README als
# Beigabe, Version, Veröffentlichungsskript). Liefert die Zeilen der Veröffentlichung.
function Update-SetupFile([string]$setup) {
    $inputs = @(Get-ChildItem (Join-Path $Phase7Root 'src') -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' })
    $inputs += @(Get-Item (Join-Path $Phase7Root 'README.md'), (Join-Path $Phase7Root 'Directory.Build.props'), (Join-Path $Phase7Root 'tools\publish.ps1'))
    $newest = $inputs | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    $file = Get-Item -LiteralPath $setup -ErrorAction SilentlyContinue
    if ($file -and (Test-Path (Join-Path $PortableDir 'HDRpilot.exe')) -and $file.LastWriteTimeUtc -ge $newest.LastWriteTimeUtc) {
        return @(("Setup-Datei aktuell: {0} ({1:yyyy-MM-dd HH:mm}, neueste Quelle {2} {3:yyyy-MM-dd HH:mm})" -f $file.Name, $file.LastWriteTime, $newest.Name, $newest.LastWriteTime))
    }

    $reason = if ($file) { "älter als $($newest.FullName.Substring($Phase7Root.Length + 1))" } else { 'fehlt' }
    $lines = @("Setup-Datei $reason, veröffentliche neu (tools\publish.ps1)")
    $lines += @(& (Join-Path $Phase7Root 'tools\publish.ps1') | ForEach-Object { "  $_" })
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $setup)) { throw "Veröffentlichung fehlgeschlagen (exit=$LASTEXITCODE) oder ohne Setup-Datei $setup" }
    return $lines
}

# Prüft eine Setup-Datei gegen das Format und die portable Auslieferung. Problems leer = in Ordnung.
function Test-SetupBundle([string]$setup, [string]$portableDir) {
    $problems = New-Object System.Collections.ArrayList
    $info = [pscustomobject]@{ Problems = $problems; ExeLength = [long]0; PayloadLength = [long]0; ExeHash = ''; Entries = @(); AsInvoker = $false }
    if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) { [void]$problems.Add("Setup-Datei fehlt: $setup"); return $info }

    $footer = [HdrPilotGates.SetupFile]::ReadFooter($setup)
    if ($footer.Magic -cne 'HDRPILOT-SETUP01') { [void]$problems.Add('Abschluss: Kennung HDRPILOT-SETUP01 fehlt am Dateiende'); return $info }
    $info.ExeLength = $footer.ExeLength
    $info.PayloadLength = $footer.PayloadLength
    if ($footer.ExeLength -le 0 -or $footer.PayloadLength -le 0 -or $footer.ExeLength + $footer.PayloadLength + 64 -ne $footer.FileLength) {
        [void]$problems.Add(('Abschluss: Längen passen nicht zur Datei (EXE {0} + Paket {1} + 64 <> {2} Bytes)' -f $footer.ExeLength, $footer.PayloadLength, $footer.FileLength))
        return $info
    }

    $info.ExeHash = [HdrPilotGates.SetupFile]::Sha256($setup, 0, $footer.ExeLength)
    if ($info.ExeHash -ne $footer.ExeSha256) { [void]$problems.Add('EXE-Teil: SHA-256 weicht vom Abschluss ab') }
    if ($info.ExeHash -ne (Get-Sha (Join-Path $portableDir 'HDRpilot.exe'))) { [void]$problems.Add('EXE-Teil: SHA-256 weicht von der portablen HDRpilot.exe ab') }

    try {
        $pairs = @([HdrPilotGates.SetupFile]::ZipEntryHashes($setup, $footer.ExeLength, $footer.PayloadLength))
        $info.Entries = @($pairs | ForEach-Object { ($_ -split '\|')[0] })
        $expected = (@($PayloadEntries) | Sort-Object) -join ', '
        $actual = (@($info.Entries) | Sort-Object) -join ', '
        if ($actual -cne $expected) { [void]$problems.Add("Paket: Einträge $actual statt genau $expected") }
        foreach ($pair in $pairs) {
            $name, $hash = $pair -split '\|'
            $portable = Join-Path $portableDir ($name -replace '/', '\')
            if ($hash -ne (Get-Sha $portable)) { [void]$problems.Add("Paket: $name weicht von der portablen Datei ab") }
        }
    } catch {
        [void]$problems.Add("Paket: Zip nicht lesbar ($($_.Exception.Message))")
    }

    $info.AsInvoker = [HdrPilotGates.SetupFile]::IndexOf($setup, 0, $footer.ExeLength, $AsInvokerText) -ge 0
    if (-not $info.AsInvoker) { [void]$problems.Add("EXE-Teil: Manifest ohne $AsInvokerText") }
    foreach ($elevated in 'level="requireAdministrator"', 'level="highestAvailable"') {
        if ([HdrPilotGates.SetupFile]::IndexOf($setup, 0, $footer.ExeLength, $elevated) -ge 0) { [void]$problems.Add("EXE-Teil: Manifest verlangt erhöhte Rechte ($elevated)") }
    }
    return $info
}

# Schutz vor dem gefährlichsten Fehler eines Prüflaufs: Kann die gebaute EXE den Abschluss oder --uninstall noch nicht
# auswerten, liefe die Setup-Datei als normale App mit der ECHTEN Ablage (--test-root wäre ein unbekannter Schalter).
# Deshalb vor jedem Start prüfen, dass der Quelltext, aus dem die Setup-Datei gebaut ist, den Setup-Modus enthält.
# Liefert die fehlenden Teile (leer = vorhanden).
function Get-MissingSetupMode {
    $missing = @()
    $setupDir = Join-Path $Phase7Root 'src\HdrPilot.App\Setup'
    if (Test-Path (Join-Path $setupDir 'SetupEngines.Stub.cs')) { $missing += 'Platzhalter SetupEngines.Stub.cs ist noch da (Engines fehlen)' }
    $sources = @(Get-ChildItem (Join-Path $Phase7Root 'src\HdrPilot.App') -Recurse -Include *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Name -notin 'SetupBundle.cs', 'SetupArguments.cs' })
    $text = ($sources | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n"
    if ($text -notmatch 'SetupBundle\.TryOpen\(') { $missing += 'kein Aufruf von SetupBundle.TryOpen (Start als Setup)' }
    if ($text -notmatch 'SetupArguments\.Parse\(') { $missing += 'kein Aufruf von SetupArguments.Parse (--test-root, --uninstall)' }
    return $missing
}

function Test-Elevated {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# --- Testablage ---

function Get-TempBase { [HdrPilotGates.SetupFile]::LongPath([IO.Path]::GetTempPath()).TrimEnd('\') }

function New-SetupTestLayout([string]$prefix) {
    $root = Join-Path (Get-TempBase) ('{0}-{1}' -f $prefix, [guid]::NewGuid().ToString('N').Substring(0, 8))
    New-Item -ItemType Directory -Force $root | Out-Null
    $root = [HdrPilotGates.SetupFile]::LongPath($root).TrimEnd('\')
    if ($root.Contains('~')) { throw "Testordner mit Kurznamen: $root" }
    $hash = [HdrPilotGates.SetupFile]::PathHash($root)
    $config = Join-Path $root 'Config'
    $install = Join-Path $root 'Programs\HDRpilot'
    $branch = "HKCU:\Software\HDRpilot.Tests\Setup\$hash"
    return [pscustomobject]@{
        Root = $root
        Hash = $hash
        Config = $config
        InstallDir = $install
        Exe = Join-Path $install 'HDRpilot.exe'
        StartMenuLnk = Join-Path $root 'StartMenu\Programs\HDRpilot.lnk'
        DesktopLnk = Join-Path $root 'Desktop\HDRpilot.lnk'
        RegistryBranch = $branch
        UninstallKey = "$branch\Uninstall\HDRpilot"
        RunKey = "$branch\Run"
        InstanceId = [HdrPilotGates.SetupFile]::PathHash($config)
        SetupLog = Join-Path $root 'setup.log'
    }
}

# Test-Ablage wie in den anderen Gates (TestConfigSeed.ps1) und ohne Hinweise; dazu eine Kanarien-Datei, die kein Setup
# anfassen darf. Liefert deren SHA-256.
function Initialize-SetupTestConfig([string]$cfg) {
    Initialize-TestConfig $cfg
    $file = Join-Path $cfg 'config.json'
    $json = [IO.File]::ReadAllText($file) | ConvertFrom-Json
    $json.settings | Add-Member -NotePropertyName 'notifications' -NotePropertyValue $false -Force
    [IO.File]::WriteAllText($file, ($json | ConvertTo-Json -Depth 20), (New-Object Text.UTF8Encoding $false))
    $bytes = New-Object byte[] 65536
    (New-Object Random).NextBytes($bytes)
    [IO.File]::WriteAllBytes((Join-Path $cfg 'canary.bin'), $bytes)
    return Get-Sha (Join-Path $cfg 'canary.bin')
}

function Test-SamePath([string]$a, [string]$b) {
    if (-not $a -or -not $b) { return $false }
    $norm = { param($p) [IO.Path]::GetFullPath($p.Trim().Trim('"')).TrimEnd('\') }
    return [string]::Equals((& $norm $a), (& $norm $b), [StringComparison]::OrdinalIgnoreCase)
}

function Test-Contains([string]$text, [string]$part) { $text -and $part -and $text.IndexOf($part, [StringComparison]::OrdinalIgnoreCase) -ge 0 }

function Get-RegValues([string]$keyPath) {
    if (-not (Test-Path $keyPath)) { return $null }
    $key = Get-Item $keyPath
    $values = @{}
    foreach ($name in $key.GetValueNames()) { $values[$name] = [pscustomobject]@{ Value = $key.GetValue($name); Kind = [string]$key.GetValueKind($name) } }
    return $values
}

function Get-RegValue([string]$keyPath, [string]$name) {
    if (-not (Test-Path $keyPath)) { return $null }
    return (Get-Item $keyPath).GetValue($name)
}

function Get-ShortcutInfo([string]$lnk) {
    if (-not (Test-Path -LiteralPath $lnk)) { return $null }
    $shell = New-Object -ComObject WScript.Shell
    try {
        $s = $shell.CreateShortcut($lnk)
        return [pscustomobject]@{ Target = $s.TargetPath; Arguments = $s.Arguments }
    } finally {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)
    }
}

function Test-InstanceRunning([string]$id) {
    $mutex = $null
    try {
        if ([Threading.Mutex]::TryOpenExisting("Local\HdrPilot.Instance.$id", [ref]$mutex)) { $mutex.Dispose(); return $true }
        return $false
    } catch {
        return $true # vorhanden, nur ohne Zugriff (wie SingleInstance.IsRunning)
    }
}

function Get-AppLogText([string]$cfg) {
    $text = Get-ChildItem (Join-Path $cfg 'logs') -Filter '*.log' -ErrorAction SilentlyContinue | ForEach-Object {
        $fs = New-Object IO.FileStream($_.FullName, 'Open', 'Read', 'ReadWrite, Delete')
        $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
        $sr.ReadToEnd(); $sr.Dispose()
    } | Out-String
    return $text
}

# Startzeilen der App im Protokoll der Ablage ("HDRpilot 1.1.0.0 startet. Konfiguration ...").
function Get-StartCount([string]$cfg) { ([regex]::Matches((Get-AppLogText $cfg), 'HDRpilot \S+ startet\.')).Count }

function Get-FileLength([string]$path) {
    $f = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
    if ($f) { return $f.Length }
    return [long]0
}

# Setup-Protokoll ab einer Byte-Position (frühere Läufe in derselben Datei zählen nicht).
function Get-LogSince([string]$path, [long]$offset) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    $fs = New-Object IO.FileStream($path, 'Open', 'Read', 'ReadWrite, Delete')
    try {
        if ($offset -gt $fs.Length) { $offset = 0 }
        [void]$fs.Seek($offset, 'Begin')
        $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
        return $sr.ReadToEnd()
    } finally {
        $fs.Dispose()
    }
}

function Get-UninstallTempDirs { @(Get-ChildItem (Get-TempBase) -Directory -Filter 'HDRpilot-Uninstall-*' -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }) }

# Prozesse mit diesem Namen, deren EXE unter dem Ordner liegt (nur eigene Namen, nie fremde Prozesse öffnen).
function Get-ProcessesUnder([string]$name, [string]$directory) {
    $prefix = $directory.TrimEnd('\') + '\'
    return @(Get-Process -Name $name -ErrorAction SilentlyContinue | Where-Object {
        $path = $null
        try { $path = $_.Path } catch {}
        $path -and $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)
    })
}

# --- Prozesse starten (Arbeitsordner %TEMP%, damit kein Programm- oder Testordner gesperrt bleibt) ---

$script:GateProcesses = New-Object System.Collections.ArrayList

function Join-Arguments([string[]]$arguments) {
    return (@($arguments) | ForEach-Object {
        if ($_ -eq '' -or $_ -match '[\s"]') { '"' + (($_ -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"' } else { $_ }
    }) -join ' '
}

function Start-Exe([string]$exe, [string[]]$arguments) {
    $psi = New-Object Diagnostics.ProcessStartInfo($exe)
    $psi.Arguments = Join-Arguments $arguments
    # Über die Shell: Die Prozesse erben keine Handles des Skripts (sonst hielte etwa die nach dem Setup gestartete App
    # die Ausgabe des Aufrufers offen). Eine UAC-Abfrage ist ausgeschlossen, weil die Gates vorher asInvoker prüfen.
    $psi.UseShellExecute = $true
    $psi.WorkingDirectory = Get-TempBase
    $p = [Diagnostics.Process]::Start($psi)
    [void]$GateProcesses.Add($p)
    return $p
}

function Invoke-Exe([string]$exe, [string[]]$arguments, [int]$timeoutMs) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Exe $exe $arguments
    $exited = $p.WaitForExit($timeoutMs)
    return [pscustomobject]@{ Exited = $exited; ExitCode = $(if ($exited) { $p.ExitCode } else { $null }); Seconds = $sw.Elapsed.TotalSeconds; Process = $p }
}

function Wait-Until([scriptblock]$condition, [int]$timeoutMs, [int]$intervalMs = 200) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($true) {
        if (& $condition) { return $true }
        if ($sw.ElapsedMilliseconds -ge $timeoutMs) { return $false }
        Start-Sleep -Milliseconds $intervalMs
    }
}

# --- Echter Stand (Eintrag, Autostart, Verknüpfungen, installierte EXE, config.json, laufende Instanz) ---

function Get-RealState {
    $state = New-Object System.Collections.Generic.List[string]
    $uninstall = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HDRpilot'
    $values = Get-RegValues $uninstall
    if ($null -eq $values) { $state.Add('Installierte Apps: kein Eintrag HDRpilot') }
    else {
        $state.Add("Installierte Apps: Eintrag HDRpilot mit $($values.Count) Werten")
        foreach ($name in @($values.Keys | Sort-Object)) { $state.Add(("Installierte Apps: {0} = {1} ({2})" -f $name, $values[$name].Value, $values[$name].Kind)) }
    }
    $run = Get-RegValue 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' 'HDRpilot'
    $state.Add("Autostart HKCU Run HDRpilot = $(if ($null -eq $run) { '(fehlt)' } else { $run })")
    foreach ($lnk in (Join-Path ([Environment]::GetFolderPath('Programs')) 'HDRpilot.lnk'), (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'HDRpilot.lnk')) {
        $f = Get-Item -LiteralPath $lnk -ErrorAction SilentlyContinue
        $state.Add("Verknüpfung $lnk = $(if ($f) { 'vorhanden, geändert ' + $f.LastWriteTimeUtc.ToString('o') } else { '(fehlt)' })")
    }
    $realDir = Join-Path $env:LOCALAPPDATA 'Programs\HDRpilot'
    $exe = Get-Item -LiteralPath (Join-Path $realDir 'HDRpilot.exe') -ErrorAction SilentlyContinue
    $state.Add("Installierte EXE = $(if ($exe) { (Get-Sha $exe.FullName) + ', geändert ' + $exe.LastWriteTimeUtc.ToString('o') } else { '(fehlt)' })")
    $config = Join-Path $env:APPDATA 'HDRpilot\config.json'
    $state.Add("config.json = $(if (Test-Path -LiteralPath $config) { Get-Sha $config } else { '(fehlt)' })")
    $state.Add("Echte Instanz läuft = $(Test-InstanceRunning 'default')")
    $pids = @(Get-ProcessesUnder 'HDRpilot' $realDir | ForEach-Object { $_.Id } | Sort-Object)
    $state.Add("Prozesse aus dem echten Programmordner = $(if ($pids.Count) { $pids -join ', ' } else { 'keine' })")
    return ,$state.ToArray()
}

function Compare-RealState([string[]]$before, [string[]]$after) {
    $diff = New-Object System.Collections.ArrayList
    foreach ($line in $before) { if ($line -notin $after) { [void]$diff.Add("vorher: $line") } }
    foreach ($line in $after) { if ($line -notin $before) { [void]$diff.Add("nachher: $line") } }
    return $diff.ToArray()
}

# --- Aufräumen (nur die eigene Testablage, ihr Registry-Zweig und die Temp-Kopien dieses Laufs) ---

# Beendet eine noch laufende Testinstanz über den Befehlsweg. Was danach noch läuft (selbst gestartete Prozesse, Prozesse
# aus der Testablage oder aus den Temp-Kopien dieses Laufs), wird zuletzt hart beendet und als Fehler gemeldet; alle
# laufen nur mit --test-root oder --config-dir. Liefert Meldungen.
function Stop-TestLeftovers($layout, [string[]]$tempDirs) {
    $notes = @()
    if (Test-InstanceRunning $layout.InstanceId) {
        $exe = if (Test-Path -LiteralPath $layout.Exe) { $layout.Exe } else { Join-Path $PortableDir 'HDRpilot.exe' }
        $notes += "Testinstanz läuft noch, beende über --exit"
        [void](Invoke-Exe $exe @('--config-dir', $layout.Config, '--exit') 15000)
        [void](Wait-Until { -not (Test-InstanceRunning $layout.InstanceId) } 15000)
    }
    $rest = @($GateProcesses | Where-Object { -not $_.HasExited })
    $rest += @(Get-ProcessesUnder 'HDRpilot' $layout.Root)
    foreach ($dir in @($tempDirs)) { $rest += @(Get-ProcessesUnder 'HDRpilot-Uninstall' $dir) }
    foreach ($id in @($rest | ForEach-Object { $_.Id } | Sort-Object -Unique)) {
        $notes += "FEHLER Rest-Prozess $id aus dem Prüflauf hart beendet"
        Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
    }
    return $notes
}

function Remove-TestLayout($layout, [string[]]$tempDirs) {
    $notes = @()
    if (Test-Path $layout.RegistryBranch) { Remove-Item $layout.RegistryBranch -Recurse -Force; $notes += "Registry-Zweig $($layout.RegistryBranch) gelöscht" }
    foreach ($dir in @($layout.Root) + @($tempDirs)) {
        if (-not $dir -or -not (Test-Path -LiteralPath $dir)) { continue }
        for ($i = 0; $i -lt 20 -and (Test-Path -LiteralPath $dir); $i++) {
            try { Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction Stop } catch { Start-Sleep -Milliseconds 500 }
        }
        $notes += $(if (Test-Path -LiteralPath $dir) { "FEHLER $dir ließ sich nicht löschen" } else { "$dir gelöscht" })
    }
    return $notes
}

# --- Oberflächentexte ---

function Read-Resx([string]$name) {
    $path = Join-Path $Phase7Root "src\HdrPilot.App\Resources\$name.resx"
    if (-not (Test-Path -LiteralPath $path)) { throw "$name.resx fehlt ($path)" }
    $values = @{}
    foreach ($d in ([xml][IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)).root.data) { $values[$d.name] = [string]$d.value }
    return $values
}

# True, wenn der Text dem Ressourcen-Muster entspricht ({0}, {1} ... beliebig, Leerraum zusammengefasst).
function Test-Template([string]$text, [string]$template) {
    if ($null -eq $text -or -not $template) { return $false }
    $pattern = '^' + ([regex]::Escape(($template -replace '\s+', ' ').Trim()) -replace '\\\{\d+}', '.+') + '$'
    return ($text -replace '\s+', ' ').Trim() -cmatch $pattern
}
