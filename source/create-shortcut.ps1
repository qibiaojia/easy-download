param(
    [string]$ApplicationDirectory = (Join-Path $PSScriptRoot '..'),
    [string]$ShortcutPath = (Join-Path ([Environment]::GetFolderPath('Desktop')) '轻松下载.lnk')
)
$ErrorActionPreference = 'Stop'
$exe = [System.IO.Path]::GetFullPath((Join-Path $ApplicationDirectory '轻松下载.exe'))
if (!(Test-Path -LiteralPath $exe)) { throw '未找到应用 EXE。' }
if (Test-Path -LiteralPath $ShortcutPath) { throw '快捷方式已存在，未覆盖。' }
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($ShortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = [IO.Path]::GetDirectoryName($exe)
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = '轻松下载：粘贴链接即可下载'
$shortcut.Save()
if (!(Test-Path -LiteralPath $ShortcutPath)) { throw '快捷方式未写入，请检查桌面目录权限。' }
$verify = $shell.CreateShortcut($ShortcutPath)
if ($verify.TargetPath -ne $exe) { throw '快捷方式目标校验失败。' }
Write-Output $ShortcutPath
