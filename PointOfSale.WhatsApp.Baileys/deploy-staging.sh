#!/bin/bash

# ============================================================================
# Baileys WhatsApp Service - Staging Deployment Script
# ============================================================================
# This script deploys the Baileys service to staging environment
# ============================================================================

set -e  # Exit on error

echo "========================================="
echo "Baileys WhatsApp Service - Staging Deploy"
echo "========================================="
echo ""

# Configuration
SERVICE_NAME="whatsapp-baileys-staging"
SERVICE_DIR="/opt/whatsapp-baileys"
ENV_FILE=".env.staging"

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Step 1: Check prerequisites
echo -e "${YELLOW}[1/8] Checking prerequisites...${NC}"
if ! command -v node &> /dev/null; then
    echo -e "${RED}Error: Node.js is not installed${NC}"
    exit 1
fi

NODE_VERSION=$(node --version | cut -d'v' -f2 | cut -d'.' -f1)
if [ "$NODE_VERSION" -lt 20 ]; then
    echo -e "${RED}Error: Node.js 20.x or higher is required (found: $(node --version))${NC}"
    exit 1
fi

if ! command -v pm2 &> /dev/null; then
    echo -e "${YELLOW}PM2 not found. Installing...${NC}"
    npm install -g pm2
fi

echo -e "${GREEN}✓ Prerequisites OK${NC}"
echo ""

# Step 2: Create service directory
echo -e "${YELLOW}[2/8] Creating service directory...${NC}"
sudo mkdir -p "$SERVICE_DIR"
sudo chown $USER:$USER "$SERVICE_DIR"
echo -e "${GREEN}✓ Service directory ready${NC}"
echo ""

# Step 3: Copy files
echo -e "${YELLOW}[3/8] Copying service files...${NC}"
cp -r src "$SERVICE_DIR/"
cp -r package*.json "$SERVICE_DIR/"
cp tsconfig.json "$SERVICE_DIR/"
cp "$ENV_FILE" "$SERVICE_DIR/.env"
echo -e "${GREEN}✓ Files copied${NC}"
echo ""

# Step 4: Install dependencies
echo -e "${YELLOW}[4/8] Installing dependencies...${NC}"
cd "$SERVICE_DIR"
npm install --production
echo -e "${GREEN}✓ Dependencies installed${NC}"
echo ""

# Step 5: Build TypeScript
echo -e "${YELLOW}[5/8] Building TypeScript...${NC}"
npm run build
echo -e "${GREEN}✓ Build complete${NC}"
echo ""

# Step 6: Verify Redis connectivity
echo -e "${YELLOW}[6/8] Verifying Redis connectivity...${NC}"
REDIS_HOST=$(grep REDIS_HOST .env | cut -d'=' -f2)
REDIS_PORT=$(grep REDIS_PORT .env | cut -d'=' -f2)

if command -v redis-cli &> /dev/null; then
    if redis-cli -h "$REDIS_HOST" -p "$REDIS_PORT" ping > /dev/null 2>&1; then
        echo -e "${GREEN}✓ Redis connection OK${NC}"
    else
        echo -e "${RED}Warning: Cannot connect to Redis at $REDIS_HOST:$REDIS_PORT${NC}"
        echo -e "${YELLOW}Service may not start correctly. Please verify Redis configuration.${NC}"
    fi
else
    echo -e "${YELLOW}redis-cli not found. Skipping Redis check.${NC}"
fi
echo ""

# Step 7: Stop existing service (if running)
echo -e "${YELLOW}[7/8] Stopping existing service...${NC}"
if pm2 describe "$SERVICE_NAME" > /dev/null 2>&1; then
    pm2 stop "$SERVICE_NAME"
    pm2 delete "$SERVICE_NAME"
    echo -e "${GREEN}✓ Existing service stopped${NC}"
else
    echo -e "${YELLOW}No existing service found${NC}"
fi
echo ""

# Step 8: Start service with PM2
echo -e "${YELLOW}[8/8] Starting service...${NC}"
pm2 start dist/index.js --name "$SERVICE_NAME" \
    --log-date-format "YYYY-MM-DD HH:mm:ss Z" \
    --merge-logs \
    --max-memory-restart 2G

pm2 save
echo -e "${GREEN}✓ Service started${NC}"
echo ""

# Wait for service to start
echo -e "${YELLOW}Waiting for service to start...${NC}"
sleep 5

# Health check
echo -e "${YELLOW}Running health check...${NC}"
PORT=$(grep PORT .env | cut -d'=' -f2)
if curl -s "http://localhost:$PORT/health" > /dev/null; then
    echo -e "${GREEN}✓ Health check passed${NC}"
    echo ""
    echo -e "${GREEN}=========================================${NC}"
    echo -e "${GREEN}Deployment successful!${NC}"
    echo -e "${GREEN}=========================================${NC}"
    echo ""
    echo "Service status:"
    pm2 info "$SERVICE_NAME"
    echo ""
    echo "View logs:"
    echo "  pm2 logs $SERVICE_NAME"
    echo ""
    echo "Monitor service:"
    echo "  pm2 monit"
    echo ""
    echo "Health check:"
    echo "  curl http://localhost:$PORT/health"
else
    echo -e "${RED}✗ Health check failed${NC}"
    echo -e "${YELLOW}Check logs: pm2 logs $SERVICE_NAME${NC}"
    exit 1
fi
