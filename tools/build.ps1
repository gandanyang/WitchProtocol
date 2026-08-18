# 魔女协议 — 地基一键构建（.NET C# 程序集）
# 使用：双击运行，或在 PowerShell 里执行本文件。
$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent
$Csproj = Join-Path $Root 'magicThunder.csproj'

Write-Host '== dotnet build (magicThunder.csproj) =='
dotnet build $Csproj -c Debug
if ($LASTEXITCODE -ne 0) {
    Write-Host '构建失败，请把输出贴给 AI 排查。' -ForegroundColor Red
}
exit $LASTEXITCODE