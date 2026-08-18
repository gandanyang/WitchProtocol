# 魔女协议 — 打开 Godot 编辑器（开发入口）
$Root = Split-Path $PSScriptRoot -Parent
$GodotRoot = 'G:\Godot_v4.7.1-stable_mono_win64'
$Godot = Join-Path $GodotRoot 'Godot_v4.7.1-stable_mono_win64.exe'

if (-not (Test-Path $Godot)) {
    Write-Host "找不到 Godot：$Godot，请确认路径或更新 tools\*.ps1 顶部 GodotRoot。" -ForegroundColor Red
    exit 2
}
Start-Process -FilePath $Godot -ArgumentList "--path `"$Root`""
Write-Host "已启动 Godot 编辑器：" $Godot