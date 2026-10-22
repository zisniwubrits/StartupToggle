param(
    [switch]$SelfContained,
    [string]$Output = "",
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

if (-not $SkipTests) {
    Write-Host '== 自测 ==' -ForegroundColor Cyan
    dotnet build 'tests\StartupToggle.SelfTest\StartupToggle.SelfTest.csproj' -m:1 -v q --nologo
    & 'tests\StartupToggle.SelfTest\bin\Debug\net9.0-windows\StartupToggle.SelfTest.exe'
    if ($LASTEXITCODE -ne 0) {
        throw "自测失败（退出码 $LASTEXITCODE）"
    }
}

$project = 'src\StartupToggle.App\StartupToggle.App.csproj'
$target = if ($Output) { $Output } else { if ($SelfContained) { 'dist' } else { 'dist-fd' } }
$selfContainedValue = if ($SelfContained) { 'true' } else { 'false' }

$publishArgs = @(
    'publish',
    $project,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', $selfContainedValue,
    '-m:1',
    '-v', 'q',
    '--nologo',
    "-p:PublishSingleFile=$selfContainedValue",
    '-p:DebugType=none',
    '-o', $target
)

if ($SelfContained) {
    Write-Host '== 发布：自包含单文件（约 57 MB，首次需要联网下载运行时包）==' -ForegroundColor Cyan
    $publishArgs += '-p:IncludeNativeLibrariesForSelfExtract=true'
    $publishArgs += '-p:EnableCompressionInSingleFile=true'
} else {
    Write-Host '== 发布：框架依赖（约 240 KB，目标机器需 .NET 9 Desktop Runtime）==' -ForegroundColor Cyan
}

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "发布失败（退出码 $LASTEXITCODE）"
}

$exe = Join-Path $target 'StartupToggle.exe'
if (-not (Test-Path $exe)) {
    throw "发布输出里没有 $exe"
}

$size = [math]::Round((Get-Item $exe).Length / 1MB, 2)
$count = (Get-ChildItem $target -Recurse -File | Measure-Object).Count
Write-Host "== 完成：$exe（$size MB，目录内 $count 个文件）==" -ForegroundColor Green
Write-Host '提示：exe 清单要求管理员权限，双击会弹 UAC。'
