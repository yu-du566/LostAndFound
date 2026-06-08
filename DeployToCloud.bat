@echo off
chcp 936 >nul
title Deploy to Cloud Server

echo.
echo   ============================================
echo     Deploy LostAndFound to Cloud
echo   ============================================
echo.
echo   This script deploys the app to a remote Linux server.
echo   Prerequisites:
echo     - A cloud server (Alibaba Cloud / Tencent Cloud / etc.)
echo     - SSH access to the server
echo.
echo   ============================================
echo.

set /p SERVER_IP="Server IP: "
set /p SERVER_USER="SSH Username (default root): " || set SERVER_USER=root
if "%SERVER_IP%"=="" (
    echo ERROR: Server IP is required.
    pause
    exit /b 1
)

echo.
echo   1. Uploading project to server...
scp -r . %SERVER_USER%@%SERVER_IP%:/opt/lostandfound/

echo.
echo   2. Installing Docker on server (if needed)...
ssh %SERVER_USER%@%SERVER_IP% "command -v docker || curl -fsSL https://get.docker.com | sh"

echo.
echo   3. Building and starting the app...
ssh %SERVER_USER%@%SERVER_IP% "cd /opt/lostandfound && docker compose up -d --build"

echo.
echo   4. Opening firewall port 5000...
ssh %SERVER_USER%@%SERVER_IP% "ufw allow 5000 2>/dev/null || firewall-cmd --add-port=5000/tcp --permanent 2>/dev/null || iptables -I INPUT -p tcp --dport 5000 -j ACCEPT 2>/dev/null"

echo.
echo   ============================================
echo   Deployment complete!
echo   ============================================
echo   Access from phone: http://%SERVER_IP%:5000
echo   Default admin: admin / admin123
echo   ============================================
echo.
pause
