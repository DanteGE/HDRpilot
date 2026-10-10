function Get-IcoInfo([string]$path) {
  try {
    $fs = [System.IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
    try {
      $br = New-Object System.IO.BinaryReader($fs)
      $res = $br.ReadUInt16(); $type = $br.ReadUInt16(); $count = $br.ReadUInt16()
      if ($res -ne 0 -or $type -ne 1 -or $count -eq 0 -or $count -gt 64) { return "not-ico" }
      $frames = @()
      for ($i=0; $i -lt $count; $i++) {
        $w = $br.ReadByte(); $h = $br.ReadByte(); $cc=$br.ReadByte(); $r=$br.ReadByte(); $planes=$br.ReadUInt16(); $bpp=$br.ReadUInt16(); $size=$br.ReadUInt32(); $off=$br.ReadUInt32()
        if ($w -eq 0) { $w = 256 }
        $pos = $fs.Position; $fs.Position = $off; $sig = $br.ReadUInt32(); $fs.Position = $pos
        $png = ($sig -eq 0x474E5089)
        $frames += ("{0}{1}{2}" -f $w, $(if($bpp){"x$bpp"}else{""}), $(if($png){"p"}else{""}))
      }
      return ($frames -join ',')
    } finally { $fs.Dispose() }
  } catch { return "err:" + $_.Exception.Message }
}
