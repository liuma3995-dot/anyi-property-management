<#
    M7 阶段 ① 单元测试一键执行（T7-9-6）
    ---------------------------------------------------------------
    行为：可选依赖还原 → 编译解决方案 → xunit.console 串行执行 → 汇总并留档 XML
    用法：
        powershell -ExecutionPolicy Bypass -File .\PropertyManagement.Tests\run-tests.ps1
        powershell -ExecutionPolicy Bypass -File .\PropertyManagement.Tests\run-tests.ps1 -Clean
    隔离：测试库固定落在 %TEMP%\pm-tests\{时间戳}-{PID}\，不触碰 C:\ProgramData\PropertyManagement。
#>
[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$Clean,
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$testsDir = $PSScriptRoot
$repoRoot = Split-Path -Parent $testsDir
$solution = Join-Path $repoRoot 'PropertyManagement.sln'
$msbuild = 'F:\.NET\MSBuild\Current\Bin\MSBuild.exe'
$runner = Join-Path $repoRoot 'packages\xunit.runner.console.2.4.2\tools\net472\xunit.console.exe'
$testDll = Join-Path $testsDir "bin\$Configuration\PropertyManagement.Tests.dll"
$resultXml = Join-Path $repoRoot 'tmp\unit_test_result.xml'
$tempRoot = Join-Path $env:TEMP 'pm-tests'

Write-Host '== M7 阶段① 单元测试 ==' -ForegroundColor Cyan

if ($Clean) {
    Get-ChildItem $tempRoot -Directory -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host ("已清理临时测试库目录：" + $tempRoot) -ForegroundColor Yellow
}

if (-not (Test-Path $runner)) {
    Write-Host '未找到 xunit.runner.console，执行 nuget restore ...' -ForegroundColor Yellow
    $nuget = Join-Path $repoRoot 'tmp\tools\nuget.exe'
    if (Test-Path $nuget) { & $nuget restore $solution | Out-Null }
    else { throw '缺少 xunit.runner.console 且未找到 tmp\tools\nuget.exe，请先执行 nuget restore PropertyManagement.sln' }
}

if (-not $SkipBuild) {
    if (-not (Test-Path $msbuild)) { throw ("未找到 MSBuild：" + $msbuild) }
    Get-Process -Name 'PropertyManagement*' -ErrorAction SilentlyContinue | Stop-Process -Force
    Write-Host '编译中 ...' -ForegroundColor Cyan
    & $msbuild $solution "/p:Configuration=$Configuration" /m /v:m /nologo
    if ($LASTEXITCODE -ne 0) { throw ("编译失败，退出码 " + $LASTEXITCODE) }
}

if (-not (Test-Path $testDll)) { throw ("未找到测试程序集：" + $testDll) }
if (-not (Test-Path (Split-Path -Parent $resultXml))) { New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resultXml) | Out-Null }

Write-Host '执行用例（-parallel none，串行保证临时库无写竞争）...' -ForegroundColor Cyan
& $runner $testDll -parallel none -xml $resultXml
$exitCode = $LASTEXITCODE

if (Test-Path $resultXml) {
    [xml]$xml = Get-Content $resultXml -Encoding UTF8
    $total = 0; $failed = 0; $skipped = 0
    foreach ($assembly in $xml.assemblies.assembly) {
        $total += [int]$assembly.total
        $failed += [int]$assembly.failed
        $skipped += [int]$assembly.skipped
    }
    Write-Host ''
    Write-Host ("用例总数：{0}；失败：{1}；跳过：{2}" -f $total, $failed, $skipped) -ForegroundColor Green
    Write-Host ("结果留档：" + $resultXml)
    Write-Host 'BR 覆盖校验：执行 python tmp\gen_mapping.py 可重建《M7-阶段①单元测试覆盖矩阵与执行记录（v0.1）.md》'

    # 验收标准 §七-4：运行后临时目录清理干净（失败时保留现场供排查）
    $leftover = Get-ChildItem $tempRoot -Directory -ErrorAction SilentlyContinue
    if ($leftover) {
        if ($failed -eq 0) {
            $leftover | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host ("已清理本次运行的临时测试库目录（" + @($leftover).Count + " 个）：" + $tempRoot) -ForegroundColor Green
        }
        else {
            Write-Host ("用例失败，保留临时目录供排查（" + @($leftover).Count + " 个）：" + $tempRoot) -ForegroundColor Yellow
        }
    }
}

exit $exitCode
