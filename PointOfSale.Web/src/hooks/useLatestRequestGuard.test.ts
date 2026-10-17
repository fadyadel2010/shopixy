import { describe, expect, it } from 'vitest';
import { createLatestRequestGuard } from './useLatestRequestGuard';

describe('createLatestRequestGuard', () => {
  it('lets the latest request win', () => {
    const guard = createLatestRequestGuard();

    const firstRequestId = guard.beginRequest();
    const secondRequestId = guard.beginRequest();

    expect(guard.isLatestRequest(firstRequestId)).toBe(false);
    expect(guard.isLatestRequest(secondRequestId)).toBe(true);
  });

  it('invalidates in-flight requests when a new cycle starts', () => {
    const guard = createLatestRequestGuard();

    const requestId = guard.beginRequest();
    guard.invalidate();

    expect(guard.isLatestRequest(requestId)).toBe(false);
  });
});

describe('useLatestRequestGuard', () => {
  it('factory function invalidates requests on cleanup', () => {
    // Test the factory function behavior that the hook relies on
    const guard = createLatestRequestGuard();
    
    // Begin a request
    const requestId = guard.beginRequest();
    expect(guard.isLatestRequest(requestId)).toBe(true);
    
    // Simulate cleanup: invalidate() is called during cleanup
    guard.invalidate();
    
    // After invalidation, the old request should no longer be latest
    expect(guard.isLatestRequest(requestId)).toBe(false);
    
    // New requests after invalidation should become the latest
    const postInvalidationRequestId = guard.beginRequest();
    expect(guard.isLatestRequest(requestId)).toBe(false);
    expect(guard.isLatestRequest(postInvalidationRequestId)).toBe(true);
  });
});
