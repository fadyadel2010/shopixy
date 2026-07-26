#!/bin/bash

# ============================================================================
# Baileys WhatsApp Service - Integration Test Script
# ============================================================================
# This script performs end-to-end integration testing with a real WhatsApp
# account. Requires manual QR code scanning.
# ============================================================================

set -e

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
SERVICE_URL="${1:-http://localhost:3000}"
TEST_TENANT_ID="integration-test-$(date +%s)"
TEST_PHONE="${2}"

if [ -z "$TEST_PHONE" ]; then
    echo -e "${RED}Error: Test phone number required${NC}"
    echo "Usage: $0 [SERVICE_URL] TEST_PHONE"
    echo "Example: $0 http://localhost:3000 +1234567890"
    exit 1
fi

echo -e "${BLUE}=========================================${NC}"
echo -e "${BLUE}Baileys Integration Test${NC}"
echo -e "${BLUE}=========================================${NC}"
echo ""
echo "Service URL: $SERVICE_URL"
echo "Test Tenant ID: $TEST_TENANT_ID"
echo "Test Phone: $TEST_PHONE"
echo ""

# Step 1: Connect Session
echo -e "${YELLOW}[1/6] Connecting session...${NC}"
CONNECT_RESPONSE=$(curl -s -X POST $SERVICE_URL/sessions/$TEST_TENANT_ID/connect \
    -H 'Content-Type: application/json' \
    -d '{"mode": "DEDICATED"}')

if echo "$CONNECT_RESPONSE" | jq -e '.success == true' > /dev/null; then
    echo -e "${GREEN}✓ Session connected${NC}"
else
    echo -e "${RED}✗ Failed to connect session${NC}"
    echo "$CONNECT_RESPONSE" | jq
    exit 1
fi
echo ""

# Step 2: Get QR Code
echo -e "${YELLOW}[2/6] Getting QR code...${NC}"
QR_RESPONSE=$(curl -s $SERVICE_URL/sessions/$TEST_TENANT_ID/qr)

if echo "$QR_RESPONSE" | jq -e '.success == true' > /dev/null; then
    echo -e "${GREEN}✓ QR code generated${NC}"
    
    # Save QR code to file
    QR_CODE=$(echo "$QR_RESPONSE" | jq -r '.qrCode')
    echo "$QR_CODE" > qr_code_$TEST_TENANT_ID.txt
    
    echo ""
    echo -e "${BLUE}=========================================${NC}"
    echo -e "${BLUE}SCAN QR CODE WITH WHATSAPP${NC}"
    echo -e "${BLUE}=========================================${NC}"
    echo ""
    echo "QR code saved to: qr_code_$TEST_TENANT_ID.txt"
    echo ""
    echo "To view QR code:"
    echo "  cat qr_code_$TEST_TENANT_ID.txt"
    echo ""
    echo "Or open in browser:"
    echo "  echo '<img src=\"$(cat qr_code_$TEST_TENANT_ID.txt)\" />' > qr.html && open qr.html"
    echo ""
    echo -e "${YELLOW}Press Enter after scanning QR code...${NC}"
    read
else
    echo -e "${RED}✗ Failed to get QR code${NC}"
    echo "$QR_RESPONSE" | jq
    exit 1
fi
echo ""

# Step 3: Wait for Connection
echo -e "${YELLOW}[3/6] Waiting for WhatsApp connection...${NC}"
MAX_RETRIES=10
RETRY_COUNT=0

while [ $RETRY_COUNT -lt $MAX_RETRIES ]; do
    STATUS_RESPONSE=$(curl -s $SERVICE_URL/sessions/$TEST_TENANT_ID/status)
    STATUS=$(echo "$STATUS_RESPONSE" | jq -r '.status')
    
    if [ "$STATUS" == "connected" ]; then
        echo -e "${GREEN}✓ WhatsApp connected${NC}"
        break
    elif [ "$STATUS" == "qr_pending" ] || [ "$STATUS" == "connecting" ]; then
        echo -e "${YELLOW}Status: $STATUS (attempt $((RETRY_COUNT + 1))/$MAX_RETRIES)${NC}"
        sleep 3
        RETRY_COUNT=$((RETRY_COUNT + 1))
    else
        echo -e "${RED}✗ Unexpected status: $STATUS${NC}"
        echo "$STATUS_RESPONSE" | jq
        exit 1
    fi
done

if [ $RETRY_COUNT -eq $MAX_RETRIES ]; then
    echo -e "${RED}✗ Connection timeout${NC}"
    exit 1
fi
echo ""

# Step 4: Send Test Message
echo -e "${YELLOW}[4/6] Sending test message...${NC}"
MESSAGE_TEXT="Test message from Baileys integration test - $(date)"

SEND_RESPONSE=$(curl -s -X POST $SERVICE_URL/messages/send \
    -H 'Content-Type: application/json' \
    -d "{
        \"tenantId\": \"$TEST_TENANT_ID\",
        \"phone\": \"$TEST_PHONE\",
        \"text\": \"$MESSAGE_TEXT\",
        \"type\": \"text\"
    }")

if echo "$SEND_RESPONSE" | jq -e '.success == true' > /dev/null; then
    MESSAGE_ID=$(echo "$SEND_RESPONSE" | jq -r '.messageId')
    echo -e "${GREEN}✓ Message sent successfully${NC}"
    echo "Message ID: $MESSAGE_ID"
else
    echo -e "${RED}✗ Failed to send message${NC}"
    echo "$SEND_RESPONSE" | jq
    exit 1
fi
echo ""

# Step 5: Verify Session Still Connected
echo -e "${YELLOW}[5/6] Verifying session status...${NC}"
STATUS_RESPONSE=$(curl -s $SERVICE_URL/sessions/$TEST_TENANT_ID/status)

if echo "$STATUS_RESPONSE" | jq -e '.status == "connected"' > /dev/null; then
    echo -e "${GREEN}✓ Session still connected${NC}"
else
    echo -e "${RED}✗ Session disconnected unexpectedly${NC}"
    echo "$STATUS_RESPONSE" | jq
    exit 1
fi
echo ""

# Step 6: Disconnect Session
echo -e "${YELLOW}[6/6] Disconnecting session...${NC}"
DISCONNECT_RESPONSE=$(curl -s -X POST $SERVICE_URL/sessions/$TEST_TENANT_ID/disconnect)

if echo "$DISCONNECT_RESPONSE" | jq -e '.success == true' > /dev/null; then
    echo -e "${GREEN}✓ Session disconnected${NC}"
else
    echo -e "${RED}✗ Failed to disconnect session${NC}"
    echo "$DISCONNECT_RESPONSE" | jq
    exit 1
fi
echo ""

# Cleanup
rm -f qr_code_$TEST_TENANT_ID.txt

# Summary
echo -e "${BLUE}=========================================${NC}"
echo -e "${BLUE}Integration Test Summary${NC}"
echo -e "${BLUE}=========================================${NC}"
echo ""
echo -e "${GREEN}✓ All integration tests passed!${NC}"
echo ""
echo "Verified:"
echo "  ✓ Session connection"
echo "  ✓ QR code generation"
echo "  ✓ WhatsApp authentication"
echo "  ✓ Message sending"
echo "  ✓ Session persistence"
echo "  ✓ Session disconnection"
echo ""
echo "Next steps:"
echo "1. Verify message received on $TEST_PHONE"
echo "2. Check service logs: pm2 logs whatsapp-baileys"
echo "3. Monitor memory usage: pm2 monit"
echo "4. Proceed to backend integration (Task 33)"
