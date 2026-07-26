# SessionManager Tests

This directory contains comprehensive tests for the SessionManager class, including both property-based tests and unit tests.

## Test Files

### SessionManager.property.test.ts
Property-based tests using fast-check that validate universal correctness properties with 100+ random inputs.

**Properties Tested:**
- **Property 3: Session Connection Idempotency** - Validates that calling getOrCreateSession multiple times with the same tenant ID returns the same session
- **Property 4: Session Status Validity** - Validates that session status is always one of the valid enum values
- **Property 5: Session Disconnect Cleanup** - Validates that closing a session removes it from memory
- **Additional Properties**: Session count consistency, session mode preservation

### SessionManager.unit.test.ts
Unit tests that validate specific scenarios and edge cases.

**Test Suites:**
- Session Creation
- Session Retrieval
- Session Closure
- Session Count
- Active Sessions
- Initialization and Shutdown
- Edge Cases (empty IDs, long IDs, special characters)

## Running Tests

### Prerequisites
```bash
# Install dependencies
npm install
```

### Run All Tests
```bash
npm test
```

### Run Property Tests Only
```bash
npm run test:property
```

### Run Unit Tests Only
```bash
npm run test:unit
```

### Run Tests in Watch Mode
```bash
npm run test:watch
```

### Generate Coverage Report
```bash
npm run test:coverage
```

## Test Configuration

Tests are configured in `jest.config.js` with:
- TypeScript support via ts-jest
- 30-second timeout for property tests
- Coverage collection from all source files

## Mocking Strategy

The tests mock external dependencies:
- **Redis**: Mocked to avoid requiring a running Redis instance
- **Baileys**: Mocked to avoid requiring WhatsApp connections
- **QRCode**: Mocked to return predictable QR codes
- **Logger**: Mocked to avoid console output during tests

This allows tests to run quickly and reliably without external dependencies.

## Property-Based Testing

Property-based tests use fast-check to generate random inputs and validate that properties hold across the entire input space. Each property is tested with 100 iterations by default.

**Benefits:**
- Discovers edge cases that manual tests might miss
- Validates behavior across a wide range of inputs
- Provides high confidence in correctness

## Continuous Integration

These tests should be run:
- On every commit (fast feedback)
- On every pull request (comprehensive validation)
- Before deployment (final validation)

## Troubleshooting

### Tests Timeout
If property tests timeout, you can:
- Increase the timeout in jest.config.js
- Reduce numRuns in individual property tests
- Check for infinite loops or blocking operations

### Mock Issues
If mocks aren't working:
- Ensure jest.mock() calls are at the top of the file
- Clear jest cache: `npm test -- --clearCache`
- Check that mock implementations match the real API

## Future Enhancements

Potential additions:
- Integration tests with real Redis
- Performance benchmarks
- Stress tests with many concurrent sessions
- Tests for connection state transitions
- Tests for QR code generation flow
