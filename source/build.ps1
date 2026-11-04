param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'),
    [string]$EngineDirectory = ''
)
$ErrorActionPreference = 'Stop'
if (!$EngineDirectory) {
    $EngineDirectory = if (Test-Path -LiteralPath (Join-Path $PSScriptRoot '..\engine\aria2c.exe')) {
        Join-Path $PSScriptRoot '..\engine'
    } else { Join-Path $PSScriptRoot '..\work\easy-download\engine\aria2-1.37.0-win-64bit-build1' }
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '需要 Windows 自带的 .NET Framework 4.8。' }
if (!(Test-Path -LiteralPath (Join-Path $EngineDirectory 'aria2c.exe'))) { throw '缺少 aria2c.exe，请指定官方引擎目录。' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
Add-Type -AssemblyName System.Drawing
$iconPath = Join-Path $OutputDirectory 'app.ico'
$images = @()
foreach ($size in @(32,48,64,256)) {
    $bitmap = [System.Drawing.Bitmap]::new($size,$size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::FromArgb(21,111,84))
    $pen = [System.Drawing.Pen]::new([System.Drawing.Color]::White, [single]($size * 0.075))
    $pen.StartCap = $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($pen, [single]($size*.5), [single]($size*.22), [single]($size*.5), [single]($size*.61))
    $graphics.DrawLine($pen, [single]($size*.32), [single]($size*.45), [single]($size*.5), [single]($size*.63))
    $graphics.DrawLine($pen, [single]($size*.68), [single]($size*.45), [single]($size*.5), [single]($size*.63))
    $graphics.DrawLine($pen, [single]($size*.27), [single]($size*.77), [single]($size*.73), [single]($size*.77))
    $stream = [System.IO.MemoryStream]::new()
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += @{Size=$size; Bytes=$stream.ToArray()}
    $stream.Dispose(); $pen.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
}
$file = [System.IO.File]::Create($iconPath)
$writer = [System.IO.BinaryWriter]::new($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    foreach ($entry in $images) {
        $dimension = if ($entry.Size -eq 256) {0} else {$entry.Size}
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$entry.Bytes.Length); $writer.Write([uint32]$offset)
        $offset += $entry.Bytes.Length
    }
    foreach ($entry in $images) { $writer.Write([byte[]]$entry.Bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
$exe = Join-Path $OutputDirectory '轻松下载.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warn:4 /warnaserror+ /utf8output "/out:$exe" "/win32icon:$iconPath" "/win32manifest:$PSScriptRoot\app.manifest" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'App.cs') (Join-Path $PSScriptRoot 'DownloadCore.cs') (Join-Path $PSScriptRoot 'AssemblyInfo.cs')
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'app.config') -Destination ($exe + '.config') -Force
New-Item -ItemType Directory -Path (Join-Path $OutputDirectory 'engine') -Force | Out-Null
Copy-Item -Path (Join-Path $EngineDirectory '*') -Destination (Join-Path $OutputDirectory 'engine') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '使用说明.html') -Destination $OutputDirectory -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'THIRD-PARTY-NOTICES.txt') -Destination $OutputDirectory -Force
Copy-Item -LiteralPath (Join-Path $EngineDirectory 'COPYING') -Destination (Join-Path $OutputDirectory 'LICENSE.txt') -Force
$sourceOut = Join-Path $OutputDirectory 'source'
if ([System.IO.Path]::GetFullPath($sourceOut) -ne [System.IO.Path]::GetFullPath($PSScriptRoot)) {
    New-Item -ItemType Directory -Path $sourceOut -Force | Out-Null
    Copy-Item -Path (Join-Path $PSScriptRoot '*.cs'), (Join-Path $PSScriptRoot '*.ps1'), (Join-Path $PSScriptRoot '*.config'), (Join-Path $PSScriptRoot '*.manifest'), (Join-Path $PSScriptRoot '*.txt'), (Join-Path $PSScriptRoot '*.html') -Destination $sourceOut -Force
    Copy-Item -Path (Join-Path $PSScriptRoot '*.mjs') -Destination $sourceOut -Force
}
Get-Item -LiteralPath $exe | Select-Object FullName,Length,LastWriteTime
