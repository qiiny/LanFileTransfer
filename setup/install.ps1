# LanFileTransfer Installer
# 以管理员身份运行此脚本

$ErrorActionPreference = "Stop"

$AppName = "LanFileTransfer"
$InstallDir = "$env:LocalAppData\Programs\$AppName"
$SourceDir = "$PSScriptRoot\app"

if (-not (Test-Path "$SourceDir\$AppName.exe")) {
    Write-Host "错误: 未找到发布文件，请确保 publish\app\ 目录存在" -ForegroundColor Red
    exit 1
}

Write-Host "===================================" -ForegroundColor Cyan
Write-Host "  LanFileTransfer 安装程序" -ForegroundColor Cyan
Write-Host "===================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "[1/4] 创建安装目录: $InstallDir"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null

Write-Host "[2/4] 复制程序文件..."
Copy-Item -Path "$SourceDir\*" -Destination $InstallDir -Recurse -Force

Write-Host "[3/4] 创建快捷方式..."

$WshShell = New-Object -ComObject WScript.Shell

$StartMenuDir = "$env:AppData\Microsoft\Windows\Start Menu\Programs\$AppName"
New-Item -ItemType Directory -Force -Path $StartMenuDir | Out-Null

$Shortcut = $WshShell.CreateShortcut("$StartMenuDir\$AppName.lnk")
$Shortcut.TargetPath = "$InstallDir\$AppName.exe"
$Shortcut.WorkingDirectory = $InstallDir
$Shortcut.Description = "局域网文件传输工具"
$Shortcut.Save()

$DesktopPath = [Environment]::GetFolderPath("Desktop")
$DesktopShortcut = $WshShell.CreateShortcut("$DesktopPath\$AppName.lnk")
$DesktopShortcut.TargetPath = "$InstallDir\$AppName.exe"
$DesktopShortcut.WorkingDirectory = $InstallDir
$DesktopShortcut.Description = "局域网文件传输工具"
$DesktopShortcut.Save()

Write-Host "[4/4] 注册卸载信息..."
$UninstallKey = "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"
New-Item -Path $UninstallKey -Force | Out-Null
Set-ItemProperty -Path $UninstallKey -Name "DisplayName" -Value "LanFileTransfer"
Set-ItemProperty -Path $UninstallKey -Name "DisplayVersion" -Value "1.0.0"
Set-ItemProperty -Path $UninstallKey -Name "Publisher" -Value "qiiny"
Set-ItemProperty -Path $UninstallKey -Name "UninstallString" -Value "powershell.exe -Command `"Remove-Item -Recurse -Force '$InstallDir', '$StartMenuDir', '$DesktopPath\$AppName.lnk', 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$AppName'`""
Set-ItemProperty -Path $UninstallKey -Name "InstallLocation" -Value $InstallDir
Set-ItemProperty -Path $UninstallKey -Name "NoModify" -Value 1
Set-ItemProperty -Path $UninstallKey -Name "NoRepair" -Value 1

Write-Host ""
Write-Host "安装完成!" -ForegroundColor Green
Write-Host "  程序位置: $InstallDir\$AppName.exe" -ForegroundColor Yellow
Write-Host "  开始菜单: $AppName" -ForegroundColor Yellow
Write-Host "  桌面快捷方式已创建" -ForegroundColor Yellow
Write-Host ""
Write-Host "卸载方法: 控制面板 → 程序和功能 → 卸载" -ForegroundColor Gray
Write-Host "         或直接删除 $InstallDir 文件夹" -ForegroundColor Gray
