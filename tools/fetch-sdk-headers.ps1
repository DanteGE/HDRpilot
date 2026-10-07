# Holt wingdi.h und WinUser.h aus dem NuGet-Paket Microsoft.Windows.SDK.CPP nach .sdk\
# (für WingdiHeaderConformanceTests). Nur nötig, wenn kein Windows SDK installiert ist.
# Lädt ca. 153 MB von nuget.org. Die Header stehen unter Microsoft-Lizenz und kommen nicht ins Repository.
param([string]$Version = '10.0.26100.9169')
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$root = Split-Path $PSScriptRoot -Parent
$target = Join-Path $root '.sdk'
New-Item -ItemType Directory -Force $target | Out-Null
$pkg = Join-Path ([IO.Path]::GetTempPath()) "sdkcpp.$Version.zip"
Invoke-WebRequest -UseBasicParsing "https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.cpp/$Version/microsoft.windows.sdk.cpp.$Version.nupkg" -OutFile $pkg
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($pkg)
try {
    foreach ($name in 'wingdi.h', 'WinUser.h') {
        $entry = $zip.Entries | Where-Object { $_.FullName -match "/um/$([regex]::Escape($name))$" } | Select-Object -First 1
        if (-not $entry) { throw "$name nicht im Paket" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $target $name), $true)
        Write-Output "$($entry.FullName) -> .sdk\$name"
    }
} finally {
    $zip.Dispose()
    Remove-Item $pkg -Force
}
Set-Content (Join-Path $target 'QUELLE.txt') -Encoding utf8 -Value "microsoft.windows.sdk.cpp $Version (nuget.org), c/Include/<ver>/um/"
