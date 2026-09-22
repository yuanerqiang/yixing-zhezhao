@echo off
chcp 65001 >nul
title 异性遮罩 - 带日志启动
cd /d "C:\Users\yuan\Documents\ChatGPT\异性遮罩"

echo ====================================
echo   异性遮罩 启动器（带日志）
echo ====================================
echo.
echo [1/2] 结束旧的实例...
python .\masker.pyw --stop

echo.
echo [2/2] 启动遮罩程序...
echo  - 日志会实时显示在下面，并保存到 logs\ 目录
echo  - 按 Ctrl+C 可强制停止
echo  - Alt+ESC 可在任何模式下退出遮罩
echo.
echo ------------------------------------
python .\run_with_logs.py

echo.
echo ====================================
echo   程序已退出，按任意键关闭窗口
echo ====================================
pause >nul
