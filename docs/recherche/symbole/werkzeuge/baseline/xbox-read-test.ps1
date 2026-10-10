param([string]$Out)
$p = 'C:\XboxGames\Wolkenhof\Content\Hof\Binaries\WinGDK\Wolkenhof-WinGDK-Shipping.exe'
$r = @()
$r += "user=$(whoami) time=$(Get-Date -Format o)"
try { $fs = [IO.File]::OpenRead($p); $b = New-Object byte[] 2; [void]$fs.Read($b,0,2); $fs.Close(); $r += "OpenRead ok, header=" + [Text.Encoding]::ASCII.GetString($b) } catch { $r += "OpenRead failed: " + $_.Exception.InnerException.Message }
Add-Type -AssemblyName System.Drawing
try { $ic = [System.Drawing.Icon]::ExtractAssociatedIcon($p); $r += "ExtractAssociatedIcon: " + ($(if ($ic) { "$($ic.Width)x$($ic.Height)" } else { 'null' })) } catch { $r += "ExtractAssociatedIcon failed: " + $_.Exception.Message }
$q = 'C:\XboxGames\Wolkenhof\Content\gamelaunchhelper.exe'
try { $fs = [IO.File]::OpenRead($q); $fs.Close(); $r += "gamelaunchhelper OpenRead ok" } catch { $r += "gamelaunchhelper OpenRead failed: " + $_.Exception.InnerException.Message }
$r | Set-Content -Path $Out -Encoding UTF8
