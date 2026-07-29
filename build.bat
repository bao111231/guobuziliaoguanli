@echo off
chcp 65001 >nul
echo ===============================================
echo         国补资料管理系统 - 重新编译
echo ===============================================
echo.

echo 正在停止运行中的程序...
taskkill /F /IM GuoBuZiLiaoGuanLi.exe >nul 2>&1
echo 程序已停止。

echo.
echo 正在编译项目...
dotnet build --configuration Debug

echo.
if %errorlevel% equ 0 (
    echo ===============================================
    echo              编译成功！
    echo ===============================================
    echo 可执行文件位置: bin\Debug\net8.0-windows\GuoBuZiLiaoGuanLi.exe
    echo.
    set /p run="是否立即运行程序？(Y/N): "
    if /i "%run%"=="Y" (
        start "" "bin\Debug\net8.0-windows\GuoBuZiLiaoGuanLi.exe"
        echo 程序已启动。
    )
) else (
    echo ===============================================
    echo              编译失败！
    echo ===============================================
    echo 请检查错误信息并修复问题。
)

echo.
echo 按任意键退出...
pause >nul