-- ════════════════════════════════════════════════════════════════════════════
-- PHASE TOJ: TREASURY WRITE BLOCKING TRIGGERS
-- 
-- These triggers prevent direct writes to legacy treasury tables.
-- All treasury operations MUST go through the ledger.
--
-- Apply this migration when you are ready to fully enforce ledger-only writes.
-- IMPORTANT: Only apply AFTER all controllers are migrated to use 
-- ITreasuryOperationService for all treasury mutations.
-- ════════════════════════════════════════════════════════════════════════════

-- ═══════════════════════════════════════════════════════════════════════════
-- TRIGGER 1: Block INSERT on CashboxTransactions
-- ═══════════════════════════════════════════════════════════════════════════
CREATE OR REPLACE FUNCTION tr_block_cashbox_transactions_insert()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'PHASE_TOJ_BLOCKED: Direct INSERT to CashboxTransactions is blocked. Use ITreasuryOperationService to post journal entries.';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_block_cashbox_insert ON "CashboxTransactions";
CREATE TRIGGER trg_block_cashbox_insert
    BEFORE INSERT ON "CashboxTransactions"
    FOR EACH ROW
    EXECUTE FUNCTION tr_block_cashbox_transactions_insert();

-- ═══════════════════════════════════════════════════════════════════════════
-- TRIGGER 2: Block UPDATE on CashboxTransactions
-- ═══════════════════════════════════════════════════════════════════════════
CREATE OR REPLACE FUNCTION tr_block_cashbox_transactions_update()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'PHASE_TOJ_BLOCKED: Direct UPDATE to CashboxTransactions is blocked. Use ITreasuryOperationService for corrections.';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_block_cashbox_update ON "CashboxTransactions";
CREATE TRIGGER trg_block_cashbox_update
    BEFORE UPDATE ON "CashboxTransactions"
    FOR EACH ROW
    EXECUTE FUNCTION tr_block_cashbox_transactions_update();

-- ═══════════════════════════════════════════════════════════════════════════
-- TRIGGER 3: Block INSERT on VisaTransactions
-- ═══════════════════════════════════════════════════════════════════════════
CREATE OR REPLACE FUNCTION tr_block_visa_transactions_insert()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'PHASE_TOJ_BLOCKED: Direct INSERT to VisaTransactions is blocked. Use ITreasuryOperationService to post journal entries.';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_block_visa_insert ON "VisaTransactions";
CREATE TRIGGER trg_block_visa_insert
    BEFORE INSERT ON "VisaTransactions"
    FOR EACH ROW
    EXECUTE FUNCTION tr_block_visa_transactions_insert();

-- ═══════════════════════════════════════════════════════════════════════════
-- TRIGGER 4: Block UPDATE on VisaTransactions
-- ═══════════════════════════════════════════════════════════════════════════
CREATE OR REPLACE FUNCTION tr_block_visa_transactions_update()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'PHASE_TOJ_BLOCKED: Direct UPDATE to VisaTransactions is blocked. Use ITreasuryOperationService for corrections.';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_block_visa_update ON "VisaTransactions";
CREATE TRIGGER trg_block_visa_update
    BEFORE UPDATE ON "VisaTransactions"
    FOR EACH ROW
    EXECUTE FUNCTION tr_block_visa_transactions_update();

-- ═══════════════════════════════════════════════════════════════════════════
-- TRIGGER 5: Block INSERT on TreasuryTransactions
-- ═══════════════════════════════════════════════════════════════════════════
CREATE OR REPLACE FUNCTION tr_block_treasury_transactions_insert()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'PHASE_TOJ_BLOCKED: Direct INSERT to TreasuryTransactions is blocked. Use ITreasuryOperationService to post journal entries.';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_block_treasury_insert ON "TreasuryTransactions";
CREATE TRIGGER trg_block_treasury_insert
    BEFORE INSERT ON "TreasuryTransactions"
    FOR EACH ROW
    EXECUTE FUNCTION tr_block_treasury_transactions_insert();

-- ═══════════════════════════════════════════════════════════════════════════
-- TRIGGER 6: Block UPDATE on TreasuryTransactions
-- ═══════════════════════════════════════════════════════════════════════════
CREATE OR REPLACE FUNCTION tr_block_treasury_transactions_update()
RETURNS TRIGGER AS $$
BEGIN
    RAISE EXCEPTION 'PHASE_TOJ_BLOCKED: Direct UPDATE to TreasuryTransactions is blocked. Use ITreasuryOperationService for corrections.';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_block_treasury_update ON "TreasuryTransactions";
CREATE TRIGGER trg_block_treasury_update
    BEFORE UPDATE ON "TreasuryTransactions"
    FOR EACH ROW
    EXECUTE FUNCTION tr_block_treasury_transactions_update();

-- ═══════════════════════════════════════════════════════════════════════════
-- ROLLBACK SCRIPT (for emergency use)
-- ═══════════════════════════════════════════════════════════════════════════
-- DROP TRIGGER IF EXISTS trg_block_cashbox_insert ON "CashboxTransactions";
-- DROP TRIGGER IF EXISTS trg_block_cashbox_update ON "CashboxTransactions";
-- DROP TRIGGER IF EXISTS trg_block_visa_insert ON "VisaTransactions";
-- DROP TRIGGER IF EXISTS trg_block_visa_update ON "VisaTransactions";
-- DROP TRIGGER IF EXISTS trg_block_treasury_insert ON "TreasuryTransactions";
-- DROP TRIGGER IF EXISTS trg_block_treasury_update ON "TreasuryTransactions";
-- DROP FUNCTION IF EXISTS tr_block_cashbox_transactions_insert();
-- DROP FUNCTION IF EXISTS tr_block_cashbox_transactions_update();
-- DROP FUNCTION IF EXISTS tr_block_visa_transactions_insert();
-- DROP FUNCTION IF EXISTS tr_block_visa_transactions_update();
-- DROP FUNCTION IF EXISTS tr_block_treasury_transactions_insert();
-- DROP FUNCTION IF EXISTS tr_block_treasury_transactions_update();
