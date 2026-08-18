# 魔女协议 — headless 跑地基探针（自动化验证）
# 用法：
#   .\tools\run_tests.ps1                    # 跑默认 6 个探针
#   .\tools\run_tests.ps1 boot,pool          # 只跑指定探针
# 输出：tests/reports/（不入库）；退出码 0 = 全部通过。
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent

# Godot 本机路径（每台机器不同；如果移动了 Godot，改这里即可）
$GodotRoot = 'G:\Godot_v4.7.1-stable_mono_win64'
$Godot = Join-Path $GodotRoot 'Godot_v4.7.1-stable_mono_win64.exe'

if (-not (Test-Path $Godot)) {
    Write-Host "找不到 Godot：$Godot，请确认路径或更新 tools\*.ps1 顶部 GodotRoot。" -ForegroundColor Red
    exit 2
}

# 1) 构建 .NET 程序集（Godot C# 必须先 build 才能跑）
Write-Host '== step 1/2 : 构建 .NET 程序集 =='
Push-Location $Root
dotnet build (Join-Path $Root 'magicThunder.csproj') -c Debug | Out-Host
$code = $LASTEXITCODE
Pop-Location
if ($code -ne 0) { Write-Host '构建失败，已中止。' -ForegroundColor Red; exit $code }

# 2) headless 跑探针
$probes = 'boot,event,save,pool,pattern,enemy_config'
if ($args.Count -gt 0) { $probes = $args[0] }
Write-Host "== step 2/2 : headless 跑探针 [$probes] =="

# 沙箱/CI 兼容：把 Godot 的 user:// 重定向到工程内 .godot/userhome，
# 避免写系统 AppData 被沙箱拦截（存档/日志/测试报告都走这里，不入库）。
$UserHome = Join-Path $Root '.godot\userhome'
$env:APPDATA = $UserHome
Write-Host "user:// 重定向到 $UserHome"
& $Godot --headless --path $Root 'res://tests/TestSuite.tscn' '--' "--probes=$probes"
exit $LASTEXITCODE