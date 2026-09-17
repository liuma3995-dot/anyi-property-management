<#
.SYNOPSIS
    安怡物业管理系统 · 安装包构建入口（M8 D8-1 / T8-1-3）。

.DESCRIPTION
    前置检查（ISCC / .NET 4.8 离线包 / Release 产物）→ 生成向导品牌图 →
    ISCC 编译 Installer\setup.iss → 产出安装包 SHA256 与文件清单、并做「打包完整性」静态校验。

    纳管口径（§九 第 9 项）：本脚本与 setup.iss 纳入版本控制；
    Installer\redist\ 与 output\ 产物不纳管。

.PARAMETER Rehearsal
    生成并编译「非提权彩排版」安装包（PrivilegesRequired=lowest、装到 tmp 目录、不写 Run 键），
    用于在无管理员权限的构建机上端到端验证打包树可运行（T8-1-4 的可执行部分）。

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Installer\build-setup.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File Installer\build-setup.ps1 -Rehearsal
#>
param(
    [string]$Version = '1.0.0',
    [string]$Configuration = 'Release',
    [switch]$Rehearsal,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$installerDir = $PSScriptRoot
$buildDir = Join-Path $installerDir 'build'
$outputDir = Join-Path $root 'output'
$issPath = Join-Path $installerDir 'setup.iss'
$clientBin = Join-Path $root ("PropertyManagement.Client\bin\" + $Configuration)
$serverBin = Join-Path $root ("PropertyManagement.Server\bin\" + $Configuration)
$redistExe = Join-Path $installerDir 'redist\ndp48-x86-x64-allos-enu.exe'

function Say([string]$t) { Write-Host $t }
function Fail([string]$t) {
    Write-Host ('ERROR  ' + $t) -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------- ISCC 解析
function Resolve-Iscc {
    if ($env:ISCC_PATH -and (Test-Path -LiteralPath $env:ISCC_PATH)) { return $env:ISCC_PATH }

    foreach ($key in @(
            'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
            'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
            'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1')) {
        try {
            $loc = (Get-ItemProperty -Path $key -ErrorAction Stop).InstallLocation
            if ($loc) {
                $candidate = Join-Path $loc 'ISCC.exe'
                if (Test-Path -LiteralPath $candidate) { return $candidate }
            }
        }
        catch { }
    }

    foreach ($candidate in @(
            'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
            'C:\Program Files\Inno Setup 6\ISCC.exe',
            (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'))) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }

    $cmd = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    return $null
}

$iscc = Resolve-Iscc
if (-not $iscc) {
    Fail ('未找到 Inno Setup 6 的 ISCC.exe（M8 T8-1-1）。请先安装：' +
          'winget install --id JRSoftware.InnoSetup -e --source winget；' +
          '或显式指定 $env:ISCC_PATH。')
}
Say ('ISCC        : ' + $iscc)

# ---------------------------------------------------------------- 前置检查
if (-not (Test-Path -LiteralPath $redistExe)) {
    Fail ('缺少 .NET Framework 4.8 离线安装器（M8 T8-1-1）：' + $redistExe)
}
$redistHash = (Get-FileHash -LiteralPath $redistExe -Algorithm SHA256).Hash
Say ('运行时离线包: ' + $redistExe)
Say ('              SHA256=' + $redistHash)

if (-not $SkipBuild) {
    Say ''
    Say ('===== 编译 ' + $Configuration + ' 产物 =====')
    $msbuild = 'F:\.NET\MSBuild\Current\Bin\MSBuild.exe'
    if (-not (Test-Path -LiteralPath $msbuild)) { $msbuild = 'MSBuild.exe' }
    & $msbuild (Join-Path $root 'PropertyManagement.sln') ('/p:Configuration=' + $Configuration) /m /v:m /nologo
    if ($LASTEXITCODE -ne 0) { Fail ($Configuration + ' 编译失败') }
}

foreach ($dir in @($clientBin, $serverBin)) {
    if (-not (Test-Path -LiteralPath $dir)) { Fail ('缺少产物目录：' + $dir) }
}

# ------------------------------------------------- 产物新鲜度闸门（防「发旧包」）
# 教训（2026-09-15）：曾用 -SkipBuild 连续打包，而 bin\Release 停留在旧代码，
# 导致安装包缺少后端隐藏窗口等修复。此处强制校验「Release 产物不早于源码」。
function Get-NewestSourceTime([string[]]$projectDirs) {
    $newest = [DateTime]::MinValue
    foreach ($dir in $projectDirs) {
        if (-not (Test-Path -LiteralPath $dir)) { continue }
        $files = Get-ChildItem -LiteralPath $dir -Recurse -File -Include '*.cs', '*.xaml', '*.config', '*.csproj', '*.sql' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
        foreach ($f in $files) { if ($f.LastWriteTime -gt $newest) { $newest = $f.LastWriteTime } }
    }
    return $newest
}

$contractDir = Join-Path $root 'PropertyManagement.Contract'
$freshness = @(
    @{ Name = 'Client'; Exe = (Join-Path $clientBin 'PropertyManagement.Client.exe');
       Sources = @((Join-Path $root 'PropertyManagement.Client'), $contractDir) },
    @{ Name = 'Server'; Exe = (Join-Path $serverBin 'PropertyManagement.Server.exe');
       Sources = @((Join-Path $root 'PropertyManagement.Server'), $contractDir) }
)
foreach ($item in $freshness) {
    if (-not (Test-Path -LiteralPath $item.Exe)) { Fail ('缺少可执行文件：' + $item.Exe) }
    $exeTime = (Get-Item -LiteralPath $item.Exe).LastWriteTime
    $srcTime = Get-NewestSourceTime $item.Sources
    if ($srcTime -gt $exeTime) {
        Fail ($item.Name + ' 产物过期：最新源码改动 ' + $srcTime.ToString('yyyy-MM-dd HH:mm:ss') +
              ' 晚于产物构建时间 ' + $exeTime.ToString('yyyy-MM-dd HH:mm:ss') +
              '。请去掉 -SkipBuild 重新编译（' + $Configuration + '），避免把旧代码打进安装包。')
    }
    Say ('产物新鲜度  : ' + $item.Name + ' OK（产物 ' + $exeTime.ToString('yyyy-MM-dd HH:mm:ss') +
         ' ≥ 最新源码 ' + $srcTime.ToString('yyyy-MM-dd HH:mm:ss') + '）')
}

$clientFiles = @(Get-ChildItem -LiteralPath $clientBin -Recurse -File)
$serverFiles = @(Get-ChildItem -LiteralPath $serverBin -Recurse -File)
Say ''
Say ('Client 产物 : ' + $clientFiles.Count + ' 个文件')
Say ('Server 产物 : ' + $serverFiles.Count + ' 个文件')

# ---------------------------------------------------- 向导品牌图（BMP / Inno 要求）
if (-not (Test-Path -LiteralPath $buildDir)) { New-Item -ItemType Directory -Force -Path $buildDir | Out-Null }

function New-WizardImages {
    Add-Type -AssemblyName System.Drawing
    $badge = Join-Path $root 'docs\brand\anyi-logo-square-color-512.png'
    if (-not (Test-Path -LiteralPath $badge)) { $badge = Join-Path $root 'docs\brand\anyi-logo-square-color-256.png' }
    if (-not (Test-Path -LiteralPath $badge)) { Fail '缺少品牌徽记资产 docs\brand\anyi-logo-square-color-*.png' }

    $pageBg = [System.Drawing.ColorTranslator]::FromHtml('#FAF8F4')
    $ink = [System.Drawing.ColorTranslator]::FromHtml('#1F4B43')
    $accent = [System.Drawing.ColorTranslator]::FromHtml('#8A6E3C')
    $src = [System.Drawing.Image]::FromFile($badge)
    try {
        $fontName = '微软雅黑'

        # 以 2 倍分辨率绘制，交给 Inno 按目标 DPI 缩放显示：
        # ① 避免 96 DPI 下小字号直接绘制的发虚；② 高 DPI 时是「缩小」而非「放大」，字迹保持锐利
        $scale = 2
        $w = 164 * $scale
        $h = 314 * $scale

        # ① 侧栏大图（Inno 经典向导 164x314 @2x = 328x628）
        $big = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
        $g = [System.Drawing.Graphics]::FromImage($big)
        try {
            $g.SmoothingMode = 'HighQuality'
            $g.InterpolationMode = 'HighQualityBicubic'
            $g.PixelOffsetMode = 'HighQuality'
            $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
            $g.Clear($pageBg)
            $g.DrawImage($src, (22 * $scale), (36 * $scale), (120 * $scale), (120 * $scale))

            # 字号/坐标均取整，避免半像素导致的模糊
            $f1 = New-Object System.Drawing.Font $fontName, (12 * $scale), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
            $f2 = New-Object System.Drawing.Font $fontName, (10 * $scale), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
            $f3 = New-Object System.Drawing.Font $fontName, (8 * $scale), ([System.Drawing.FontStyle]::Regular), ([System.Drawing.GraphicsUnit]::Pixel)
            $b1 = New-Object System.Drawing.SolidBrush $ink
            $b2 = New-Object System.Drawing.SolidBrush $accent
            # 小字在米白底上需提高对比度才不显“糊”：版本串用主色，标语用中性墨绿灰
            $b3 = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml('#5C6B66'))
            $g.DrawString('安怡物业管理系统', $f1, $b1, (14 * $scale), (172 * $scale))
            # 版本串随 -Version 参数走，避免安装包向导图仍显示旧版本号
            $g.DrawString(('ANYI  V' + $Version), $f2, $b1, (15 * $scale), (197 * $scale))
            $g.DrawString('社区运营 · 财务收费 · 应急协同', $f3, $b3, (15 * $scale), (219 * $scale))
            $f1.Dispose(); $f2.Dispose(); $f3.Dispose(); $b1.Dispose(); $b2.Dispose(); $b3.Dispose()
        }
        finally { $g.Dispose() }
        $big.Save((Join-Path $buildDir 'wizard-image.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
        $big.Dispose()

        # ② 顶部小图（58x58 @2x = 116x116，方形徽记等比）
        $small = New-Object System.Drawing.Bitmap (58 * $scale), (58 * $scale), ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
        $g2 = [System.Drawing.Graphics]::FromImage($small)
        try {
            $g2.SmoothingMode = 'HighQuality'
            $g2.InterpolationMode = 'HighQualityBicubic'
            $g2.Clear($pageBg)
            $g2.DrawImage($src, 0, 0, (58 * $scale), (58 * $scale))
        }
        finally { $g2.Dispose() }
        $small.Save((Join-Path $buildDir 'wizard-small.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
        $small.Dispose()
    }
    finally { $src.Dispose() }
}

Say ''
Say '===== 生成向导品牌图 ====='
New-WizardImages
Get-ChildItem -LiteralPath $buildDir -Filter '*.bmp' | ForEach-Object { Say ('  ' + $_.Name + '  ' + $_.Length + ' B') }

# ---------------------------------------------------------------- 编译安装包
$defines = @('/DAppVersion=' + $Version)
$workIss = $issPath
$outBase = '安怡物业管理系统-Setup-' + $Version

if ($Rehearsal) {
    # 彩排版：非提权 + 装到 tmp + 不写 Run 键 + 独立 AppId，仅用于构建机端到端验证
    $workIss = Join-Path $root 'tmp\setup-rehearsal.iss'
    $rehearsalDir = Join-Path $root 'tmp\rehearsal-install'
    $text = [IO.File]::ReadAllText($issPath)
    $text = $text.Replace('PrivilegesRequired=admin', 'PrivilegesRequired=lowest')
    $text = $text.Replace('DefaultDirName={autopf}\PropertyManagement', 'DefaultDirName=' + $rehearsalDir)
    $text = $text.Replace('AppId={{2B7DE9A1-0005-4A5B-9C05-123456789ABC}',
                          'AppId={{2B7DE9A1-0005-4A5B-9C05-123456789ABD}')
    $text = $text.Replace('OutputBaseFilename=安怡物业管理系统-Setup-{#AppVersion}',
                          'OutputBaseFilename=安怡物业管理系统-Setup-Rehearsal-{#AppVersion}')
    $text = $text.Replace('OutputManifestFile=..\output\安怡物业管理系统-Setup-{#AppVersion}-文件清单.txt',
                          'OutputManifestFile=..\output\安怡物业管理系统-Setup-Rehearsal-{#AppVersion}-文件清单.txt')
    # 彩排版脚本位于 tmp\，相对路径会按脚本所在目录解析 → 统一改写为绝对路径
    $text = $text.Replace('#define ClientBin "..\PropertyManagement.Client\bin\Release"',
                          '#define ClientBin "' + $clientBin + '"')
    $text = $text.Replace('#define ServerBin "..\PropertyManagement.Server\bin\Release"',
                          '#define ServerBin "' + $serverBin + '"')
    $text = $text.Replace('#define RedistDir "redist"',
                          '#define RedistDir "' + (Join-Path $installerDir 'redist') + '"')
    $text = $text.Replace('OutputDir=..\output', 'OutputDir=' + $outputDir)
    $text = $text.Replace('OutputManifestFile=..\output\安怡物业管理系统-Setup-Rehearsal-{#AppVersion}-文件清单.txt',
                          'OutputManifestFile=' + (Join-Path $outputDir '安怡物业管理系统-Setup-Rehearsal-{#AppVersion}-文件清单.txt'))
    $text = $text.Replace('SetupIconFile=..\PropertyManagement.Client\Assets\app.ico',
                          'SetupIconFile=' + (Join-Path $root 'PropertyManagement.Client\Assets\app.ico'))
    $text = $text.Replace('WizardImageFile=build\wizard-image.bmp',
                          'WizardImageFile=' + (Join-Path $buildDir 'wizard-image.bmp'))
    $text = $text.Replace('WizardSmallImageFile=build\wizard-small.bmp',
                          'WizardSmallImageFile=' + (Join-Path $buildDir 'wizard-small.bmp'))
    $text = $text.Replace('Source: "launch-server-hidden.vbs"',
                          'Source: "' + (Join-Path $installerDir 'launch-server-hidden.vbs') + '"')
    # 彩排不写自启项、不建桌面快捷方式（避免污染构建机）
    $text = [regex]::Replace($text, '(?ms)^\[Registry\].*?(?=^\[)', '')
    $text = $text.Replace('Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："', '')
    # 同时移除引用该任务的桌面快捷方式条目，避免 "unknown task"
    $text = [regex]::Replace($text, '(?m)^Name: "\{autodesktop\}.*\r?\n', '')
    # 彩排不自动拉起客户端（安装后由验证脚本按隔离数据目录手动启动）
    $text = [regex]::Replace($text, '(?m)^Filename: "\{app\}\\Client\\PropertyManagement\.Client\.exe".*(\r?\n\s+Flags:[^\r\n]*)?\r?\n', '')
    [IO.File]::WriteAllText($workIss, $text, (New-Object System.Text.UTF8Encoding($true)))
    $outBase = '安怡物业管理系统-Setup-Rehearsal-' + $Version
    Say ''
    Say ('===== 彩排版安装包（非提权，装入 ' + $rehearsalDir + '） =====')
}

Say ''
Say '===== ISCC 编译 ====='
if (-not (Test-Path -LiteralPath $outputDir)) { New-Item -ItemType Directory -Force -Path $outputDir | Out-Null }
$logPath = Join-Path $outputDir ($outBase + '-build.log')

$isccOut = & $iscc @defines $workIss 2>&1
$isccExit = $LASTEXITCODE
$isccOut | Set-Content -LiteralPath $logPath -Encoding UTF8
$isccOut | Where-Object { $_ -match 'Compressing:|Successful compile|Error|Warning' } | Select-Object -Last 10 | ForEach-Object { Say ('  ' + $_) }
if ($isccExit -ne 0) { Fail ('ISCC 编译失败（退出码 ' + $isccExit + '），详见 ' + $logPath) }

$setupExe = Join-Path $outputDir ($outBase + '.exe')
$manifest = Join-Path $outputDir ($outBase + '-文件清单.txt')
if (-not (Test-Path -LiteralPath $setupExe)) { Fail ('未找到安装包产物：' + $setupExe) }

# ------------------------------------------------- 打包完整性静态校验（T8-1-3）
$packed = @($isccOut | ForEach-Object {
        if ($_ -match '^\s*Compressing:\s*(.+?)\s*$') {
            # 带版本资源的文件会附带 " (1.2.3.4)" 后缀，比对前剔除
            ($Matches[1] -replace '\s+\([\d\.]+\)$', '').Trim()
        }
    })
Say ''
Say '===== 打包完整性校验 ====='
Say ('  压缩条目  : ' + $packed.Count)

$expectedNames = @()
$expectedNames += $clientFiles | ForEach-Object { $_.Name }
$expectedNames += $serverFiles | ForEach-Object { $_.Name }
$expectedNames += 'launch-server-hidden.vbs'
$expectedNames += 'ndp48-x86-x64-allos-enu.exe'
$expectedNames = $expectedNames | Sort-Object -Unique

$missing = @()
foreach ($name in $expectedNames) {
    $hit = $packed | Where-Object { (Split-Path -Leaf $_ ) -eq $name }
    if (-not $hit) { $missing += $name }
}
if ($missing.Count -gt 0) {
    Say ('  缺失      : ' + ($missing -join ', '))
    Fail ('安装包缺少 ' + $missing.Count + ' 个预期文件（打包不完整）')
}
Say ('  预期文件全部命中 : ' + $expectedNames.Count + ' 个')

foreach ($critical in @('PropertyManagement.Client.exe', 'PropertyManagement.Client.exe.config',
                        'PropertyManagement.Server.exe', 'PropertyManagement.Server.exe.config',
                        'NLog.config', 'SQLite.Interop.dll', 'Newtonsoft.Json.dll', 'System.Data.SQLite.dll')) {
    $hit = $packed | Where-Object { (Split-Path -Leaf $_) -eq $critical }
    if (-not $hit) { Fail ('关键文件未打包：' + $critical) }
}
Say '  关键文件（客户端/后端/配置/SQLite/JSON）全部命中'

$hash = (Get-FileHash -LiteralPath $setupExe -Algorithm SHA256).Hash
$size = (Get-Item -LiteralPath $setupExe).Length

# ---------------------------------------------------------------- 构建记录
$record = Join-Path $outputDir ($outBase + '-构建记录.txt')
@(
    '产品名称     : 安怡物业管理系统',
    ('版本         : ' + $Version),
    ('构建时间     : ' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')),
    ('ISCC         : ' + $iscc),
    ('安装包       : ' + $setupExe),
    ('安装包大小   : ' + $size + ' B'),
    ('安装包SHA256 : ' + $hash),
    ('文件清单     : ' + $manifest),
    ('压缩条目     : ' + $packed.Count),
    ('Client 产物  : ' + $clientFiles.Count + ' 个文件'),
    ('Server 产物  : ' + $serverFiles.Count + ' 个文件'),
    ('运行时离线包 : ' + $redistExe),
    ('运行时SHA256 : ' + $redistHash),
    ('编译日志     : ' + $logPath)
) | Set-Content -LiteralPath $record -Encoding UTF8

Say ''
Say '===== 产物 ====='
Say ('  安装包    : ' + $setupExe)
Say ('  大小      : ' + [math]::Round($size / 1MB, 2) + ' MB')
Say ('  SHA256    : ' + $hash)
Say ('  文件清单  : ' + $manifest)
Say ('  构建记录  : ' + $record)
Say 'BUILD OK'
