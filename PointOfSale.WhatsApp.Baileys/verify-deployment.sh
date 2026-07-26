#!/bin/bash

# ============================================================================
# Baileys WhatsApp Service - Deployment Verification Script
# ============================================================================
# This script verifies that the Baileys service is deployed correctly
# and all endpoints are functioning as expected.
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
TEST_TENANT_ID="verify-test-$(date +%s)"

echo -e "${BLUE}=========================================${NC}"
echo -e "${BLUE}Baileys Service Deployment Verification${NC}"
echo -e "${BLUE}=========================================${NC}"
echo ""
echo "Service URL: $SERVICE_URL"
echo "Test Tenant ID: $TEST_TENANT_ID"
echo ""

# Test counter
TESTS_PASSED=0
TESTS_FAILED=0

# Helper function to run tests
run_test() {
    local test_name="$1"
    local test_command="$2"
    
    echo -e "${YELLOW}Testing: $test_name${NC}"
    
    if eval "$test_command"; then
        echo -e "${GREEN}✓ PASSED${NC}"
        TESTS_PASSED=$((TESTS_PASSED + 1))
        echo ""
        return 0
    else
        echo -e "${RED}✗ FAILED${NC}"
        TESTS_FAILED=$((TESTS_FAILED + 1))
        echo ""
        return 1
    fi
}

# Test 1: Health Check
run_test "Health Check Endpoint" \
    "curl -s -f $SERVICE_URL/health > /dev/null"

# Test 2: Health Check Response Format
run_test "Health Check Response Format" \
    "curl -s $SERVICE_URL/health | jq -e '.status == \"ok\"' > /dev/null"

# Test 3: Connect Session (Dedicated Mode)
run_test "Connect Session (Dedicated Mode)" \
    "curl -s -X POST $SERVICE_URL/sessions/$TEST_TENANT_ID/connect \
        -H 'Content-Type: application/json' \
        -d '{\"mode\": \"DEDICATED\"}' | jq -e '.success == true' > /dev/null"

# Wait for session to initialize
echo -e "${YELLOW}Waiting for session to initialize...${NC}"
sleep 2
echo ""

# Test 4: Get Session Status
run_test "Get Session Status" \
    "curl -s $SERVICE_URL/sessions/$TEST_TENANT_ID/status | jq -e '.success == true' > /dev/null"

# Test 5: Get QR Code
run_test "Get QR Code" \
    "curl -s $SERVICE_URL/sessions/$TEST_TENANT_ID/qr | jq -e '.success == true and .qrCode != null' > /dev/null"

# Test 6: Disconnect Session
run_test "Disconnect Session" \
    "curl -s -X POST $SERVICE_URL/sessions/$TEST_TENANT_ID/disconnect | jq -e '.success == true' > /dev/null"

# Test 7: Verify Session Cleanup
echo -e "${YELLOW}Waiting for session cleanup...${NC}"
sleep 2
echo ""

run_test "Verify Session Cleanup" \
    "curl -s $SERVICE_URL/sessions/$TEST_TENANT_ID/status | jq -e '.status == \"disconnected\"' > /dev/null"

# Test 8: Error Handling (Invalid Tenant)
run_test "Error Handling (Invalid Request)" \
    "curl -s -X POST $SERVICE_URL/messages/send \
        -H 'Content-Type: application/json' \
        -d '{\"tenantId\": \"invalid\"}' | jq -e '.success == false' > /dev/null"

# Test 9: Health Check Shows Active Sessions
run_test "Health Check Active Sessions Count" \
    "curl -s $SERVICE_URL/health | jq -e '.activeSessions >= 0' > /dev/null"

# Summary
echo -e "${BLUE}=========================================${NC}"
echo -e "${BLUE}Verification Summary${NC}"
echo -e "${BLUE}=========================================${NC}"
echo ""
echo -e "Tests Passed: ${GREEN}$TESTS_PASSED${NC}"
echo -e "Tests Failed: ${RED}$TESTS_FAILED${NC}"
echo ""

if [ $TESTS_FAILED -eq 0 ]; then
    echo -e "${GREEN}✓ All tests passed! Deployment verified.${NC}"
    echo ""
    echo "Next steps:"
    echo "1. Monitor logs: pm2 logs whatsapp-baileys"
    echo "2. Check memory usage: pm2 monit"
    echo "3. Test with real WhatsApp account (scan QR code)"
    echo "4. Proceed to backend integration testing"
    exit 0
else
    echo -e "${RED}✗ Some tests failed. Please review the errors above.${NC}"
    echo ""
    echo "Troubleshooting:"
    echo "1. Check service logs: pm2 logs whatsapp-baileys"
    echo "2. Verify Redis connectivity: redis-cli ping"
    echo "3. Check service status: pm2 status"
    echo "4. Review configuration: cat .env"
    exit 1
fi
