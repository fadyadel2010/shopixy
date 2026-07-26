#!/bin/bash

# ============================================================================
# Baileys WhatsApp Service - Production Deployment Script
# ============================================================================
# This script deploys the Baileys service to production environment
# IMPORTANT: Review all configuration before running in production
# ============================================================================

set -e  # Exit on error

echo "========================================="
echo "Baileys WhatsApp Service - Production Deploy"
echo "========================================="
echo ""

# Configuration
SERVICE_NAME="whatsapp-baileys"
SERVICE_DIR="/opt/whatsapp-baileys"
ENV_FILE=".env.production"
BACKUP_DIR="/opt/whatsapp-baileys-backups"

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# Safety check
echo -e "${RED}WARNING: This will deploy to PRODUCTION${NC}"
echo -e "${YELLOW}Press Ctrl+C to cancel, or Enter to continue...${NC}"
read

# Step 1: Check prerequisites
echo -e "${YELLOW}[1/10] Checking prerequisites...${NC}"
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
    echo -e "${RED}Error: PM2 is not installed. Install with: npm install -g pm2${NC}"
    exit 1
fi

echo -e "${GREEN}✓ Prerequisites OK${NC}"
echo ""

# Step 2: Create backup
echo -e "${YELLOW}[2/10] Creating backup...${NC}"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
sudo mkdir -p "$BACKUP_DIR"

if [ -d "$SERVICE_DIR" ]; then
    sudo tar -czf "$BACKUP_DIR/backup_$TIMESTAMP.tar.gz" -C "$SERVICE_DIR" .
    echo -e "${GREEN}✓ Backup created: $BACKUP_DIR/backup_$TIMESTAMP.tar.gz${NC}"
else
    echo -e "${YELLOW}No existing installation to backup${NC}"
fi
echo ""

# Step 3: Create service directory
echo -e "${YELLOW}[3/10] Creating service directory...${NC}"
sudo mkdir -p "$SERVICE_DIR"
sudo chown $USER:$USER "$SERVICE_DIR"
echo -e "${GREEN}✓ Service directory ready${NC}"
echo ""

# Step 4: Copy files
echo -e "${YELLOW}[4/10] Copying service files...${NC}"
cp -r src "$SERVICE_DIR/"
cp -r package*.json "$SERVICE_DIR/"
cp tsconfig.json "$SERVICE_DIR/"

# Check if .env exists, if not copy from template
if [ ! -f "$SERVICE_DIR/.env" ]; then
    echo -e "${YELLOW}No .env file found. Copying from $ENV_FILE${NC}"
    cp "$ENV_FILE" "$SERVICE_DIR/.env"
    echo -e "${RED}IMPORTANT: Review and update $SERVICE_DIR/.env with production values${NC}"
    echo -e "${YELLOW}Press Enter after updating .env file...${NC}"
    read
fi

echo -e "${GREEN}✓ Files copied${NC}"
echo ""

# Step 5: Verify .env configuration
echo -e "${YELLOW}[5/10] Verifying configuration...${NC}"
cd "$SERVICE_DIR"

# Check for default secrets
if grep -q "CHANGE_THIS" .env; then
    echo -e "${RED}Error: .env file contains default secrets${NC}"
    echo -e "${YELLOW}Please update AUTH_STATE_ENCRYPTION_SECRET in .env${NC}"
    exit 1
fi

# Verify required variables
REQUIRED_VARS=("PORT" "REDIS_HOST" "REDIS_PORT" "AUTH_STATE_ENCRYPTION_SECRET")
for VAR in "${REQUIRED_VARS[@]}"; do
    if ! grep -q "^$VAR=" .env; then
        echo -e "${RED}Error: Missing required variable: $VAR${NC}"
        exit 1
    fi
done

echo -e "${GREEN}✓ Configuration verified${NC}"
echo ""

# Step 6: Install dependencies
echo -e "${YELLOW}[6/10] Installing dependencies...${NC}"
npm ci --production
echo -e "${GREEN}✓ Dependencies installed${NC}"
echo ""

# Step 7: Build TypeScript
echo -e "${YELLOW}[7/10] Building TypeScript...${NC}"
npm run build
echo -e "${GREEN}✓ Build complete${NC}"
echo ""

# Step 8: Verify Redis connectivity
echo -e "${YELLOW}[8/10] Verifying Redis connectivity...${NC}"
REDIS_HOST=$(grep REDIS_HOST .env | cut -d'=' -f2)
REDIS_PORT=$(grep REDIS_PORT .env | cut -d'=' -f2)

if command -v redis-cli &> /dev/null; then
    if redis-cli -h "$REDIS_HOST" -p "$REDIS_PORT" ping > /dev/null 2>&1; then
        echo -e "${GREEN}✓ Redis connection OK${NC}"
    else
        echo -e "${RED}Error: Cannot connect to Redis at $REDIS_HOST:$REDIS_PORT${NC}"
        exit 1
    fi
else
    echo -e "${YELLOW}redis-cli not found. Skipping Redis check.${NC}"
fi
echo ""

# Step 9: Stop existing service (if running)
echo -e "${YELLOW}[9/10] Stopping existing service...${NC}"
if pm2 describe "$SERVICE_NAME" > /dev/null 2>&1; then
    pm2 stop "$SERVICE_NAME"
    pm2 delete "$SERVICE_NAME"
    echo -e "${GREEN}✓ Existing service stopped${NC}"
else
    echo -e "${YELLOW}No existing service found${NC}"
fi
echo ""

# Step 10: Start service with PM2
echo -e "${YELLOW}[10/10] Starting service...${NC}"
pm2 start dist/index.js --name "$SERVICE_NAME" \
    --log-date-format "YYYY-MM-DD HH:mm:ss Z" \
    --merge-logs \
    --max-memory-restart 4G \
    --restart-delay 3000

# Configure PM2 startup
pm2 startup
pm2 save

echo -e "${GREEN}✓ Service started${NC}"
echo ""

# Wait for service to start
echo -e "${YELLOW}Waiting for service to start...${NC}"
sleep 5

# Health check
echo -e "${YELLOW}Running health check...${NC}"
PORT=$(grep PORT .env | cut -d'=' -f2)
HEALTH_URL="http://localhost:$PORT/health"

MAX_RETRIES=5
RETRY_COUNT=0

while [ $RETRY_COUNT -lt $MAX_RETRIES ]; do
    if curl -s "$HEALTH_URL" > /dev/null; then
        echo -e "${GREEN}✓ Health check passed${NC}"
        break
    else
        RETRY_COUNT=$((RETRY_COUNT + 1))
        if [ $RETRY_COUNT -lt $MAX_RETRIES ]; then
            echo -e "${YELLOW}Health check failed. Retrying ($RETRY_COUNT/$MAX_RETRIES)...${NC}"
            sleep 3
        else
            echo -e "${RED}✗ Health check failed after $MAX_RETRIES attempts${NC}"
            echo -e "${YELLOW}Check logs: pm2 logs $SERVICE_NAME${NC}"
            exit 1
        fi
    fi
done

echo ""
echo -e "${GREEN}=========================================${NC}"
echo -e "${GREEN}Production deployment successful!${NC}"
echo -e "${GREEN}=========================================${NC}"
echo ""
echo "Service status:"
pm2 info "$SERVICE_NAME"
echo ""
echo "Useful commands:"
echo "  View logs:     pm2 logs $SERVICE_NAME"
echo "  Monitor:       pm2 monit"
echo "  Restart:       pm2 restart $SERVICE_NAME"
echo "  Stop:          pm2 stop $SERVICE_NAME"
echo "  Health check:  curl $HEALTH_URL"
echo ""
echo "Backup location: $BACKUP_DIR/backup_$TIMESTAMP.tar.gz"
echo ""
echo -e "${YELLOW}Next steps:${NC}"
echo "1. Monitor logs for any errors"
echo "2. Test message sending with a pilot tenant"
echo "3. Enable UseBaileys feature gate for pilot tenants"
echo "4. Monitor metrics for 24-48 hours before wider rollout"
