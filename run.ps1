# 运行脚本：编译并启动桌面程序
# 用法：pwsh -File run.ps1 [-Release] [-- 程序参数]
param(
    [switch]$Release
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\DeepseekDesktopAgent\DeepseekDesktopAgent.csproj'
$configuration = if ($Release) { 'Release' } else { 'Debug' }

$runArgs = @('run', '--project', $project, '-c', $configuration)
if ($args.Count -gt 0) {
    $runArgs += '--'
    $runArgs += $args
}

& dotnet @runArgs
exit $LASTEXITCODE
