@echo off
chcp 936 >/dev/null
title LostAndFound Platform
set PROJECT_DIR=%~dp0
set PUBLISH_EXE=%PROJECT_DIR%publish\LostAndFound.exe

echo.
echo   ============================================
echo     Lost & Found Platform
echo   ============================================
echo.
echo   [1] Local PC Mode
echo   [2] Local Phone Mode (PC creates WiFi hotspot)
echo   [3] Deploy to Cloud Server (for 24/7 access)
echo   ============================================
echo.
choice /c 123 /n /m "Select mode (1/2/3): "
if errorlevel 3 goto CLOUD
if errorlevel 2 goto PHONE
if errorlevel 1 goto PC

:CLOUD
echo.
echo   Starting cloud deployment...
echo   This will deploy to a remote server so phones
echo   can access the app from anywhere, anytime.
echo.
call DeployToCloud.bat
pause
exit

:PHONE
echo.
echo   Creating WiFi hotspot for phone...
echo   SSID: LostAndFound  |  Password: 12345678
echo.
netsh wlan set hostednetwork mode=allow ssid=LostAndFound key=12345678 >/dev/null 2>&1
netsh wlan start hostednetwork >/dev/null 2>&1
echo   After connecting, open in phone browser:
echo   http://192.168.137.1:5000
echo.
goto START

:PC
echo.
echo   Starting in PC mode...
goto START

:START
echo.
if not exist "%PUBLISH_EXE%" goto RUN_DEV
cd /d "%PROJECT_DIR%"
start "Server" /MIN "%PUBLISH_EXE%" --urls http://0.0.0.0:5000
goto OPEN

:RUN_DEV
cd /d "%PROJECT_DIR%"
start "Server" /MIN dotnet run --project LostAndFound.csproj --urls http://0.0.0.0:5000

:OPEN
ping 127.0.0.1 -n 6 >/dev/null
start http://localhost:5000
echo.
echo   Server running! http://localhost:5000
echo   Close Server window to stop.
pause
