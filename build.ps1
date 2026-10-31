# 打包脚本：Release 构建并生成自包含版与依赖版单文件 exe
# 用法：pwsh -File build.ps1 [-SelfContainedOnly | -FrameworkDependentOnly]
param(
    [switch]$SelfContainedOnly,
    [switch]$FrameworkDependentOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\DeepseekDesktopAgent\DeepseekDesktopAgent.csproj'
$publishRoot = Join-Path $root 'publish'
$selfDir = Join-Path $publishRoot 'self-contained'
$depDir = Join-Path $publishRoot 'framework-dependent'

if ($SelfContainedOnly -and $FrameworkDependentOnly) {
    throw '不能同时指定 -SelfContainedOnly 与 -FrameworkDependentOnly'
}

function Publish-App {
    param(
        [string]$OutDir,
        [bool]$SelfContained
    )

    if (Test-Path $OutDir) { Remove-Item $OutDir -Recurse -Force }
    New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

    dotnet publish $project -c Release -r win-x64 `
        --self-contained $SelfContained `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=$SelfContained `
        -o $OutDir
    if ($LASTEXITCODE -ne 0) { throw "发布失败：$OutDir" }
}

New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

if (-not $FrameworkDependentOnly) {
    Write-Host '正在打包自包含版（无需安装 .NET 运行时）...'
    Publish-App -OutDir $selfDir -SelfContained $true
    Copy-Item (Join-Path $selfDir 'Deepseek-Desktop-Agent.exe') `
        (Join-Path $publishRoot 'Deepseek-Desktop-Agent-self-contained.exe') -Force
}

if (-not $SelfContainedOnly) {
    Write-Host '正在打包依赖版（需要已安装 .NET 运行时）...'
    Publish-App -OutDir $depDir -SelfContained $false
    Copy-Item (Join-Path $depDir 'Deepseek-Desktop-Agent.exe') `
        (Join-Path $publishRoot 'Deepseek-Desktop-Agent-framework-dependent.exe') -Force
}

Write-Host ''
Write-Host '打包完成：'
Get-ChildItem $publishRoot -Filter '*.exe' |
    Sort-Object Name |
    ForEach-Object { '{0}  ({1:N1} MB)' -f $_.FullName, ($_.Length / 1MB) }
