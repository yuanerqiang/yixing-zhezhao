@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "%CSC%" (
    echo 找不到 Windows 自带的 C# 编译器。
    pause
    exit /b 1
)

echo 正在生成学习遮罩...
"%CSC%" /nologo /codepage:65001 /target:winexe /out:"学习遮罩.exe" "Program.cs"

if errorlevel 1 (
    echo 生成失败，请把上面的错误信息发给开发者。
    pause
    exit /b 1
)

start "" "学习遮罩.exe"
endlocal
