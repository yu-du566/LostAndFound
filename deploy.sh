#!/bin/bash
# Lost & Found Platform - One-click Deploy Script
# Run this on your cloud server after uploading the project
# Usage: chmod +x deploy.sh && ./deploy.sh

set -e

echo "============================================"
echo "  Lost & Found Platform Deploy"
echo "============================================"

# Check dotnet SDK
if ! command -v dotnet &> /dev/null; then
    echo "Installing .NET SDK..."
    # For Ubuntu/Debian
    if command -v apt-get &> /dev/null; then
        wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
        chmod +x dotnet-install.sh
        ./dotnet-install.sh --channel 10.0
        export PATH=$HOME/.dotnet:$PATH
        echo 'export PATH=$HOME/.dotnet:$PATH' >> ~/.bashrc
    else
        echo "Please install .NET 10 SDK manually: https://dotnet.microsoft.com/download"
        exit 1
    fi
fi

echo ""
echo "[1/3] Publishing application..."
dotnet publish -c Release -o app

echo ""
echo "[2/3] Setting up as system service..."
# Create systemd service for auto-start and auto-restart
SERVICE_FILE="/etc/systemd/system/lostandfound.service"
sudo tee $SERVICE_FILE > /dev/null << EOF
[Unit]
Description=Lost & Found Platform
After=network.target

[Service]
Type=simple
WorkingDirectory=$(pwd)/app
ExecStart=$(which dotnet) LostAndFound.dll --urls http://0.0.0.0:5000
Restart=always
RestartSec=5
User=$USER

[Install]
WantedBy=multi-user.target
EOF

echo ""
echo "[3/3] Starting service..."
sudo systemctl daemon-reload
sudo systemctl enable lostandfound
sudo systemctl start lostandfound

echo ""
echo "============================================"
echo "  Deployment complete!"
echo "============================================"
echo "  Access: http://$(curl -s ifconfig.me 2>/dev/null || hostname -I | awk '{print $1}'):5000"
echo "  Admin:  admin / admin123"
echo "============================================"
echo ""
echo "  Useful commands:"
echo "    sudo systemctl status lostandfound   # Check status"
echo "    sudo systemctl restart lostandfound  # Restart"
echo "    sudo journalctl -u lostandfound -f   # View logs"
echo ""
