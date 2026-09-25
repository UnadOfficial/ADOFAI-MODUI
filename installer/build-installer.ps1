<#
.SYNOPSIS
    编译 Inno Setup 安装程序（本地与 CI 共用）。

.DESCRIPTION
    做三件事：
      1. 定位 ISCC.exe（runner 预装 → 常见安装路径 → Chocolatey 兜底）
      2. 准备简体中文语言文件（Inno Setup 6.5+ 随附；缺失则回退到官方 issrc 仓库，
         再失败就只编译英文界面）
      3. 用 /D 覆盖版本号与路径，调用 ADOFAI.ModManager.iss 完成编译并校验产物

.PARAMETER Version
    展示版本号，如 1.2.3 或 1.2.3-beta.1（对应 AppVersion）。

.PARAMETER Numeric
    四段纯数字版本号，如 1.2.3.0（安装程序 FileVersion 资源需要，对应 VersionInfoVersion）。

.PARAMETER PayloadDir
    已 publish 的应用文件目录，默认 dist/app。

.PARAMETER OutDir
    安装程序输出目录，默认 dist。

.EXAMPLE
    pwsh -File installer/build-installer.ps1 -Version 1.2.3 -Numeric 1.2.3.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$Numeric,
    [string]$PayloadDir = 'dist/app',
    [string]$OutDir = 'dist'
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Find-Iscc {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @()
    foreach ($root in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, 'C:\ProgramData\chocolatey')) {
        if ($root) { $candidates += Join-Path $root 'Inno Setup 6\ISCC.exe' }
    }
    # Chocolatey 的 shim 在当前会话里不一定已进入 PATH，直接查 shim 目录
    $candidates += 'C:\ProgramData\chocolatey\bin\ISCC.exe'

    foreach ($c in $candidates) { if (Test-Path $c) { return $c } }
    return $null
}

function Resolve-ChineseLanguageFile([string]$Iscc) {
    $zh = Join-Path (Split-Path -Parent $Iscc) 'Languages\ChineseSimplified.isl'
    if (Test-Path $zh) { return $zh }

    $url = 'https://raw.githubusercontent.com/jrsoftware/issrc/main/Files/Languages/ChineseSimplified.isl'
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) 'ChineseSimplified.isl'
    try {
        Invoke-WebRequest -Uri $url -OutFile $tmp -ErrorAction Stop
        return $tmp
    } catch {
        Write-Warning "简体中文语言文件不可用，安装程序将只有英文界面：$_"
        return $null
    }
}

# ---- 1. 载荷与输出目录（统一转成绝对路径，避免 Inno 相对路径歧义） ----------
$payload = (Resolve-Path -Path $PayloadDir -ErrorAction SilentlyContinue).Path
if (-not $payload) { throw "载荷目录不存在：$PayloadDir" }
if (-not (Get-ChildItem -Path $payload -ErrorAction SilentlyContinue)) {
    throw "载荷目录为空：$payload（请先 publish 应用并复制产物）"
}
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir -Force | Out-Null }
$outFull = (Resolve-Path -Path $OutDir).Path

# ---- 2. 编译器 --------------------------------------------------------------
$iscc = Find-Iscc
if (-not $iscc) {
    Write-Host '未找到 Inno Setup，改用 Chocolatey 安装…'
    choco install innosetup --no-progress -y
    $iscc = Find-Iscc
}
if (-not $iscc) { throw '找不到 ISCC.exe（Inno Setup 编译器）' }
Write-Host "ISCC: $iscc"

# ---- 3. 语言文件 ------------------------------------------------------------
$zh = Resolve-ChineseLanguageFile $iscc
if ($zh) {
    Copy-Item $zh (Join-Path $PSScriptRoot 'ChineseSimplified.isl') -Force
    Write-Host "已启用简体中文界面：$zh"
}

# ---- 4. 编译 ----------------------------------------------------------------
& $iscc (Join-Path $PSScriptRoot 'ADOFAI.ModManager.iss') `
    "/DMyAppVersion=$Version" `
    "/DMyAppVersionNumeric=$Numeric" `
    "/DAppPayloadDir=$payload" `
    "/DOutDir=$outFull"
if ($LASTEXITCODE -ne 0) { throw "ISCC 编译失败，退出码 $LASTEXITCODE" }

$setup = @(Get-ChildItem -Path $outFull -Filter '*-Setup.exe' -File)
if ($setup.Count -eq 0) { throw "未在 $outFull 生成安装程序" }
foreach ($s in $setup) {
    Write-Host ("安装程序：{0}（{1:N1} MB）" -f $s.Name, ($s.Length / 1MB))
}
