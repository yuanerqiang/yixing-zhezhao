$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

if (-not (Test-Path -LiteralPath $csc)) {
    throw "找不到 Windows 自带的 C# 编译器。"
}

$assets = Join-Path $root "assets"
$build = Join-Path $root "build"
$release = Join-Path $root "release"
$icon = Join-Path $assets "学习遮罩.ico"
$cover = Join-Path $assets "学习遮罩.png"
$appOutput = Join-Path $build "学习遮罩.exe"
$installerOutput = Join-Path $release "学习遮罩安装程序.exe"

if (-not (Test-Path -LiteralPath $icon) -or -not (Test-Path -LiteralPath $cover)) {
    & $csc /nologo /target:exe `
        /out:"$(Join-Path $root 'tools\IconBuilder.exe')" `
        /reference:System.Drawing.dll `
        "$(Join-Path $root 'tools\IconBuilder.cs')"
    if ($LASTEXITCODE -ne 0) {
        throw "图标生成器编译失败。"
    }

    & "$(Join-Path $root 'tools\IconBuilder.exe')" $cover $icon
    if ($LASTEXITCODE -ne 0) {
        throw "图标生成失败。"
    }
}

New-Item -ItemType Directory -Path $build -Force | Out-Null
New-Item -ItemType Directory -Path $release -Force | Out-Null

Write-Host "正在生成主程序..."
& $csc /nologo /codepage:65001 /target:winexe `
    /win32icon:$icon `
    /out:$appOutput `
    "$(Join-Path $root 'app\Program.cs')"
if ($LASTEXITCODE -ne 0) {
    throw "主程序编译失败。"
}

Write-Host "正在生成安装和卸载程序..."
& $csc /nologo /codepage:65001 /target:winexe `
    /win32icon:$icon `
    /reference:System.Drawing.dll `
    "/resource:$appOutput,StudyMaskApp.exe" `
    "/resource:$cover,AppCover.png" `
    "/resource:$icon,AppIcon.ico" `
    /out:$installerOutput `
    "$(Join-Path $root 'installer\Setup.cs')"
if ($LASTEXITCODE -ne 0) {
    throw "安装程序编译失败。"
}

Copy-Item -LiteralPath $appOutput -Destination (Join-Path $release "学习遮罩.exe") -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "使用说明.txt") -Destination (Join-Path $release "使用说明.txt") -Force

$packageFiles = @(
    $installerOutput,
    (Join-Path $release "学习遮罩.exe"),
    (Join-Path $release "使用说明.txt")
)
$zipPath = Join-Path $release "学习遮罩-发布包.zip"
Compress-Archive -Path $packageFiles -DestinationPath $zipPath -Force

Write-Host ""
Write-Host "打包完成。"
Write-Host "安装程序：$installerOutput"
Write-Host "发布压缩包：$zipPath"
