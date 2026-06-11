@echo off
chcp 936 >nul
title LostAndFound
set PROJECT_DIR=%~dp0
set PUBLISH_EXE=%PROJECT_DIR%publish\LostAndFound.exe

echo   Lost & Found - Starting...

REM Start server (prefer published .exe for speed)
if exist "%PUBLISH_EXE%" (
    start "Server" /MIN "%PUBLISH_EXE%" --urls http://0.0.0.0:5000
) else (
    start "Server" /MIN dotnet run --project "%PROJECT_DIR%LostAndFound.csproj" --urls http://0.0.0.0:5000
)

REM Wait for server
ping 127.0.0.1 -n 8 >nul

REM Start public tunnel
start "Tunnel" /MIN ssh -o StrictHostKeyChecking=no -o ServerAliveInterval=30 -R 80:localhost:5000 nokey@localhost.run

REM Open browser
start http://localhost:5000

echo   Done! Browser opened.
echo   Phone: scan QR code on homepage, or check Tunnel window for URL.
pause
