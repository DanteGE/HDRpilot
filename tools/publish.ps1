# Veröffentlicht HdrPilot als einzelne, eigenständige EXE (Windows x64), dazu die Kommandozeile und die README.
# Ergebnis (Standard): artifacts\publish\HDRpilot\HDRpilot.exe, ...\cli\hdrpilot.exe, ...\README.md
# und daneben die Setup-Datei artifacts\publish\HDRpilot-Setup-<Version>.exe (E83).
param([string]$Output)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Output) { $Output = Join-Path $root 'artifacts\publish\HDRpilot' }
$staging = Join-Path $root 'artifacts\publish-staging'
$setupDir = Split-Path $Output -Parent

foreach ($dir in $Output, $staging) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
}
# Ältere Setup-Dateien zuerst weg: Scheitert die Veröffentlichung, liegt kein veralteter Setup neben dem leeren Ordner.
Get-ChildItem $setupDir -Filter 'HDRpilot-Setup-*.exe' -File -ErrorAction SilentlyContinue | Remove-Item -Force

$projects = @(
    @{ Name = 'App'; Project = 'src\HdrPilot.App\HdrPilot.App.csproj'; Exe = 'HDRpilot.exe'; Target = '' },
    @{ Name = 'Kommandozeile'; Project = 'src\HdrPilot.Cli\HdrPilot.Cli.csproj'; Exe = 'hdrpilot.exe'; Target = 'cli' }
)

foreach ($p in $projects) {
    $out = Join-Path $staging $p.Name
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $ErrorActionPreference = 'Continue' # dotnet schreibt Meldungen auch auf stderr
    $log = & dotnet publish (Join-Path $root $p.Project) -p:PublishProfile=win-x64 -o $out -nologo -v q 2>&1
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -ne 0) {
        $log | Select-Object -Last 30 | ForEach-Object { Write-Output $_ }
        Write-Output "dotnet publish ($($p.Name)) exit=$LASTEXITCODE"
        exit 1
    }

    $files = @(Get-ChildItem $out -Recurse -File)
    $extra = @($files | Where-Object { $_.Name -ne $p.Exe })
    if ($extra.Count -gt 0 -or $files.Count -ne 1) {
        Write-Output ("Unerwartete Dateien neben {0}: {1}" -f $p.Exe, (($extra | ForEach-Object { $_.FullName.Substring($out.Length + 1) }) -join ', '))
        exit 1
    }

    $target = if ($p.Target) { Join-Path $Output $p.Target } else { $Output }
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item $files[0].FullName $target
    Write-Output ("{0} veröffentlicht in {1:N0} s" -f $p.Name, $sw.Elapsed.TotalSeconds)
}

# Lizenzhinweise der mitgelieferten Fremdbestandteile (MIT verlangt, den Hinweis mitzuliefern). Versionen aus der
# deps.json der Veröffentlichung, Texte aus den NuGet-Paketen, mit denen gebaut wurde.
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$deps = @{}
foreach ($depsFile in 'src\HdrPilot.App\obj\Release\net10.0-windows\win-x64\HDRpilot.deps.json', 'src\HdrPilot.Cli\obj\Release\net10.0-windows\win-x64\hdrpilot.deps.json') {
    $json = [IO.File]::ReadAllText((Join-Path $root $depsFile))
    foreach ($m in [regex]::Matches($json, '"(?:runtimepack\.)?((?:Microsoft\.NETCore\.App|Microsoft\.WindowsDesktop\.App)\.Runtime\.win-x64|Microsoft\.Extensions\.[A-Za-z.]+)/([0-9][^"]*)": \{')) {
        $deps[$m.Groups[1].Value] = $m.Groups[2].Value
    }
}
function Get-PackageText([string]$id, [string]$file) {
    $version = $deps[$id]
    if (-not $version) { throw "Lizenz: $id fehlt in deps.json" }
    $path = Join-Path $packages ("{0}\{1}\{2}" -f $id.ToLowerInvariant(), $version, $file)
    if (-not (Test-Path $path)) { throw "Lizenz: $path fehlt" }
    return [IO.File]::ReadAllText($path).Trim()
}
$core = 'Microsoft.NETCore.App.Runtime.win-x64'
$desktop = 'Microsoft.WindowsDesktop.App.Runtime.win-x64'
$extensions = @($deps.Keys | Where-Object { $_ -like 'Microsoft.Extensions.*' } | Sort-Object)
$rule = '=' * 100
$notice = @(
    'HDRpilot – Lizenzhinweise mitgelieferter Fremdbestandteile',
    '',
    'HDRpilot.exe und cli\hdrpilot.exe enthalten die folgenden Bestandteile von Microsoft und der .NET Foundation.',
    'Alle stehen unter der MIT-Lizenz; keiner steht unter GPL oder AGPL.',
    'Zusätzlich zeigt HDRpilot Angaben von PCGamingWiki an (CC BY-NC-SA 3.0, zur Laufzeit abgerufen; Abschnitt am Ende).',
    '',
    $rule,
    ".NET-Laufzeit ($core $($deps[$core]))"
) + @($extensions | ForEach-Object { "$_ $($deps[$_])" }) + @(
    'Lizenz:',
    $rule,
    (Get-PackageText $core 'LICENSE.TXT'),
    '',
    $rule,
    "Windows Desktop: WPF und Windows Forms ($desktop $($deps[$desktop]))",
    'Lizenz:',
    $rule,
    (Get-PackageText $desktop 'LICENSE'),
    '',
    $rule,
    'Hinweise zu Fremdcode innerhalb der .NET-Laufzeit (THIRD-PARTY-NOTICES.TXT aus dem Laufzeitpaket):',
    $rule,
    (Get-PackageText $core 'THIRD-PARTY-NOTICES.TXT'),
    '',
    # HDR-Liste (E73): Daten von PCGamingWiki, nicht mitgeliefert, sondern zur Laufzeit abgerufen.
    $rule,
    'Angaben zur HDR-Unterstützung: PCGamingWiki (https://www.pcgamingwiki.com/)',
    $rule,
    'HDRpilot zeigt in der Spieleliste an, welche Spiele laut PCGamingWiki HDR unterstützen ("HDR", eingeschränkt "HDR*").',
    'Diese Angaben stammen von PCGamingWiki (pcgamingwiki.com) und stehen unter der Lizenz Creative Commons',
    'Namensnennung - Nicht kommerziell - Weitergabe unter gleichen Bedingungen 3.0 (CC BY-NC-SA 3.0,',
    'https://creativecommons.org/licenses/by-nc-sa/3.0/).',
    'Die Daten sind nicht Teil dieser Auslieferung: HDRpilot ruft sie zur Laufzeit von pcgamingwiki.com ab und speichert',
    'nur das Ergebnis je Spiel lokal zwischen (%LOCALAPPDATA%\HDRpilot\hdr-support.json, mit Konto zusätzlich hdr-catalog.json).',
    'Verwendet werden nur die Felder "hdr", "steam appid" und "gogcom id"; HDRpilot ordnet sie den Spielen der Bibliothek zu.'
)
[IO.File]::WriteAllText((Join-Path $Output 'THIRD-PARTY-NOTICES.txt'), (($notice -join "`r`n") + "`r`n"), (New-Object Text.UTF8Encoding $true))

$readme = Join-Path $root 'README.md'
if (Test-Path $readme) {
    Copy-Item $readme $Output
} else {
    Write-Output 'Hinweis: README.md fehlt im Repository und wurde nicht beigelegt.'
}

Remove-Item $staging -Recurse -Force

# Setup-Datei (E83, Format wie src\HdrPilot.App\Setup\SetupBundle.cs): die veröffentlichte HDRpilot.exe unverändert,
# dahinter ein Zip mit README.md, THIRD-PARTY-NOTICES.txt und cli/hdrpilot.exe und ein Abschluss von 64 Bytes:
# 16 Bytes ASCII "HDRPILOT-SETUP01" | Int64 Länge der EXE | Int64 Länge des Zips | 32 Bytes SHA-256 der EXE (little endian).
# Das Zip entsteht erst im Speicher: Direkt hinter die EXE geschrieben, stünden im Zip Positionen ab Dateianfang.
$appExe = Join-Path $Output 'HDRpilot.exe'
$appInfo = (Get-Item $appExe).VersionInfo
$setupVersion = '{0}.{1}.{2}' -f $appInfo.FileMajorPart, $appInfo.FileMinorPart, $appInfo.FileBuildPart
$setup = Join-Path $setupDir "HDRpilot-Setup-$setupVersion.exe"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$zipBuffer = New-Object IO.MemoryStream
$zip = New-Object IO.Compression.ZipArchive($zipBuffer, [IO.Compression.ZipArchiveMode]::Create, $true)
try {
    foreach ($entry in 'README.md', 'THIRD-PARTY-NOTICES.txt', 'cli/hdrpilot.exe') {
        $source = Join-Path $Output ($entry -replace '/', '\')
        if (-not (Test-Path $source)) { throw "Setup: $source fehlt" }
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $source, $entry, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally {
    $zip.Dispose()
}
$sha = [Security.Cryptography.SHA256]::Create()
$exeStream = [IO.File]::OpenRead($appExe)
try { $exeHash = $sha.ComputeHash($exeStream) } finally { $exeStream.Dispose(); $sha.Dispose() }
$setupStream = New-Object IO.FileStream($setup, [IO.FileMode]::Create, [IO.FileAccess]::Write,[IO.FileShare]::None)
try {
    $exeStream = [IO.File]::OpenRead($appExe)
    try { $exeStream.CopyTo($setupStream, 1MB) } finally { $exeStream.Dispose() }
    $exeLength = $setupStream.Length
    $zipBuffer.WriteTo($setupStream)
    $payloadLength = $setupStream.Length - $exeLength
    $footer = New-Object byte[] 64
    [Text.Encoding]::ASCII.GetBytes('HDRPILOT-SETUP01').CopyTo($footer, 0)
    [BitConverter]::GetBytes([long]$exeLength).CopyTo($footer, 16)
    [BitConverter]::GetBytes([long]$payloadLength).CopyTo($footer, 24)
    $exeHash.CopyTo($footer, 32)
    $setupStream.Write($footer, 0, $footer.Length)
} catch {
    $setupStream.Dispose()
    Remove-Item $setup -Force -ErrorAction SilentlyContinue
    throw
} finally {
    $setupStream.Dispose()
    $zipBuffer.Dispose()
}

Get-ChildItem $Output -Recurse -File | ForEach-Object {
    $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
    Write-Output ("{0,-18} {1,8:N1} MB  SHA256 {2}" -f $_.FullName.Substring($Output.Length + 1), ($_.Length / 1MB), $hash)
}
$setupFile = Get-Item $setup
Write-Output ("{0,-18} {1,8:N1} MB  SHA256 {2} (EXE {3:N0} Bytes, Paket {4:N0} Bytes)" -f $setupFile.Name, ($setupFile.Length / 1MB), (Get-FileHash $setup -Algorithm SHA256).Hash, $exeLength, $payloadLength)
Write-Output "PUBLISH_DONE $Output"
