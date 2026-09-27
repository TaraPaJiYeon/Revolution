@echo off
rem ============================================================
rem  本地预览这套文档（等价于 GitHub Pages 上线的效果）
rem  双击本文件即可：它会起一个本地小服务并自动打开浏览器。
rem  关掉这个黑窗口 = 停止服务。没有 Python 也能用（直接打开 index.html）。
rem ============================================================
chcp 65001 >nul
cd /d "%~dp0"

set PORT=8777
echo.
echo   正在启动本地文档服务...
echo   地址： http://localhost:%PORT%/index.html
echo   关闭本窗口即停止。
echo.

start "" "http://localhost:%PORT%/index.html"

py -m http.server %PORT% 2>nul
if errorlevel 1 python -m http.server %PORT% 2>nul
if errorlevel 1 (
  echo   没有检测到 Python，改为直接用浏览器打开 index.html
  start "" "index.html"
  pause
)
