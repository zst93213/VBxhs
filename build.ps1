# Vista 无障碍版微博 & 小红书聚合客户端 —— 发布脚本
# 适用环境：Windows 10 1809+ 或 Windows 11，已安装 .NET 8 SDK (SDK-style csproj 用)
# 命令行：powershell -ExecutionPolicy Bypass -File .\build.ps1
#
# 产物目录：publish/Vista-net481-win-x64/framework-dependent/
# 或 self-contained（包含 .NET Framework 运行时，体积较大但免依赖）

param(
    [ValidateSet("Debug","Release")]
    [string]$Config = "Release",
    [ValidateSet("framework-dependent","self-contained")]
    [string]$Mode = "framework-dependent",
    [string]$OutputDir = "$PSScriptRoot\publish\Vista-net481-win-x64"
)

$ErrorActionPreference = "Stop"

Write-Host "=== Vista 发布脚本 ===" -ForegroundColor Cyan
Write-Host "配置: $Config  |  模式: $Mode"
Write-Host "输出: $OutputDir"
Write-Host ""

$solution = Join-Path $PSScriptRoot "Vista.sln"
$mainProject = Join-Path $PSScriptRoot "src\Vista.Presentation\Vista.Presentation.csproj"

if (-not (Test-Path $solution)) {
    Write-Error "未找到解决方案文件: $solution"
    exit 1
}

if (-not (Test-Path $mainProject)) {
    Write-Error "未找到主项目: $mainProject"
    exit 1
}

Write-Host "[1/4] dotnet restore ..." -ForegroundColor Yellow
dotnet restore $solution --verbosity minimal
if ($LASTEXITCODE -ne 0) { Write-Error "restore 失败"; exit 1 }

Write-Host "[2/4] dotnet build ($Config) ..." -ForegroundColor Yellow
dotnet build $solution -c $Config --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { Write-Error "build 失败"; exit 1 }

Write-Host "[3/4] dotnet publish ..." -ForegroundColor Yellow
$runDir = Join-Path $OutputDir ($Mode -replace "-","_")

# net481 在 Windows 10 1809+ 已内置运行时，framework-dependent 即可
# self-contained 需要额外安装 .NET Framework 4.8.1 Developer Pack
dotnet publish $mainProject -c $Config `
    -o $runDir `
    --self-contained ($Mode -eq "self-contained") `
    -r win-x64 `
    --verbosity minimal
if ($LASTEXITCODE -ne 0) { Write-Error "publish 失败"; exit 1 }

Write-Host "[4/4] 打包 zip ..." -ForegroundColor Yellow
$zipDir = Split-Path $runDir -Parent
$zipFile = Join-Path $zipDir "Vista-net481-win-x64-$Mode.zip"
if (Test-Path $zipFile) { Remove-Item $zipFile -Force }
Compress-Archive -Path (Join-Path $runDir "*") -DestinationPath $zipFile -Force

Write-Host ""
Write-Host "=== 发布完成 ===" -ForegroundColor Green
Write-Host "产物目录: $runDir"
Write-Host "压缩包:   $zipFile"
Write-Host ""
Write-Host "注意事项：" -ForegroundColor Magenta
Write-Host "  - 目标机器需安装 .NET Framework 4.8.1（Windows 10 1809+ / Windows 11 已内置）"
Write-Host "  - 首次运行 Vista.exe 时 Windows SmartScreen 可能提示未知发布者，选择「仍要运行」"
Write-Host "  - 朗读功能需系统安装中文 TTS 语音包（设置 -> 时间和语言 -> 语音）"
