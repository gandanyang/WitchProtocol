# 魔女协议 — M1-PERF：headless 弹幕压力基准（自动化 Benchmark）
# 用法：
#   .\tools\bench.ps1                      # 全预设矩阵 sweep（100→5000 弹 + 6/20/50 敌机 + 混合压力）
#   .\tools\bench.ps1 -Mode bullets -Count 1000 -Seconds 4   # 单档：维持 1000 颗敌弹
#   .\tools\bench.ps1 -Mode enemies -Count 50 -Seconds 4     # 单档：维持 50 只敌机
# 输出：user://test_reports/benchmark_latest.txt（沙箱下 .godot/userhome/...，不入库）
param(
  [string]$Mode = 'sweep',
  [int]$Count = -1,
  [int]$Enemies = -1,
  [int]$Bullets = -1,
  [float]$Seconds = 4
)
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent

# Godot 本机路径（每台机器不同；如果移动了 Godot，改这里即可）
$GodotRoot = 'G:\Godot_v4.7.1-stable_mono_win64'
$Godot = Join-Path $GodotRoot 'Godot_v4.7.1-stable_mono_win64.exe'

if (-not (Test-Path $Godot)) {
    Write-Host "找不到 Godot：$Godot，请确认路径或更新 tools\*.ps1 顶部 GodotRoot。" -ForegroundColor Red
    exit 2
}

# 1) 构建 .NET 程序集
Write-Host '== step 1/2 : 构建 .NET 程序集 =='
Push-Location $Root
dotnet build (Join-Path $Root 'magicThunder.csproj') -c Debug | Out-Host
$code = $LASTEXITCODE
Pop-Location
if ($code -ne 0) { Write-Host '构建失败，已中止。' -ForegroundColor Red; exit $code }

# 2) headless 压测
Write-Host "== step 2/2 : headless 压测 [mode=$Mode count=$Count enemies=$Enemies bullets=$Bullets seconds=$Seconds] =="

# 沙箱/CI 兼容：把 Godot 的 user:// 重定向到工程内 .godot/userhome（不入库）
$UserHome = Join-Path $Root '.godot\userhome'
$env:APPDATA = $UserHome
Write-Host "user:// 重定向到 $UserHome"

# 注意1：必须用数组逐元素传参（不能拼成单个空格字符串）——否则 Godot 把整串当作一个参数，
#        C# 端 OS.GetCmdlineUserArgs() 解析不到 --sweep 等标记。
# 注意2：Godot.exe 是 GUI 子系统程序，`& $Godot` 不会等待、也拿不到退出码（进程被剥离后台）。
#        必须用 Start-Process -Wait -PassThru 同步跑完，否则脚本提前返回、报告还没写出来。
$userArgs = @('--headless', '--path', $Root, 'res://tests/StressBench.tscn', '--', "--mode=$Mode")
if ($Mode -eq 'sweep') { $userArgs += '--sweep' }
if ($Count -ge 0) { $userArgs += "--count=$Count" }
if ($Enemies -ge 0) { $userArgs += "--enemies=$Enemies" }
if ($Bullets -ge 0) { $userArgs += "--bullets=$Bullets" }
$userArgs += "--seconds=$Seconds"

$benchLog = Join-Path $UserHome 'bench_out.log'
$p = Start-Process -FilePath $Godot -ArgumentList $userArgs -Wait -PassThru `
     -RedirectStandardOutput $benchLog -RedirectStandardError ($benchLog + '.err') -WindowStyle Hidden
Write-Host "Godot exit code: $($p.ExitCode)"
if (Test-Path $benchLog) { Get-Content $benchLog -Tail 40 }
exit $p.ExitCode
