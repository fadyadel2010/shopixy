// M1.1 thin wrapper over k6/http.
// Injects X-Request-Id / X-Correlation-Id per request, merges tenant headers,
// attaches consistent tags, records timing metadata. No retries, no
// suppression of 4xx/5xx, no secret logging.
import http from 'k6/http';
import { newRequestId } from './identity.js';

function bodyHash(body) {
  if (body === null || body === undefined) return 'empty';
  var s = typeof body === 'string' ? body : JSON.stringify(body);
  // FNV-1a 32-bit — non-crypto fingerprint for violation bundles only.
  var h = 0x811c9dc5;
  for (var i = 0; i < s.length; i++) {
    h ^= s.charCodeAt(i);
    h = (h * 0x01000193) >>> 0;
  }
  return ('0000000' + h.toString(16)).slice(-8) + ':' + String(s.length);
}

// opts: { method, url, tenantId, sessionId, hostOverride, tags, headers, body, params }
// hostOverride: value for the Host header (M1.2). The API resolves anonymous
// tenants from Host via Storefront:UrlTemplate (http://{slug}.localhost:5174,
// port-sensitive), so the harness sends Host:{slug}.localhost:5174 while the
// TCP connection still targets BASE_URL. No secrets are ever attached.
// Returns { res, meta } where meta is the sanitized record for checks.js.
export function requestWithMetadata(opts) {
  var requestId = newRequestId();
  var headers = Object.assign({}, opts.headers || {});
  headers['X-Request-Id'] = requestId;
  headers['X-Correlation-Id'] = requestId;
  if (opts.hostOverride && !headers['Host']) {
    headers['Host'] = opts.hostOverride;
  }
  if (opts.tenantId && !headers['X-Tenant-Id']) {
    headers['X-Tenant-Id'] = opts.tenantId;
  }
  if (opts.sessionId && !headers['X-Session-Id']) {
    headers['X-Session-Id'] = opts.sessionId;
  }

  var tags = Object.assign(
    { tenant: 'unknown', store: 'unknown', profile: 'read-smoke', vu_kind: 'smoke', endpoint: 'unknown' },
    opts.tags || {}
  );

  var params = Object.assign({}, opts.params || {}, { headers: headers, tags: tags });
  var method = (opts.method || 'GET').toUpperCase();
  var res;
  var start = Date.now();
  if (method === 'GET') {
    res = http.get(opts.url, params);
  } else {
    throw new Error('M1.1 harness is read-only: method ' + method + ' not allowed');
  }
  var latencyMs = Date.now() - start;

  var meta = {
    method: method,
    url: opts.url,
    requestId: requestId,
    correlationId: requestId,
    statusCode: res.status,
    latencyMs: latencyMs,
    responseBodyHash: bodyHash(res.body),
    tags: tags,
    // NOTE: headers recorded WITHOUT Authorization (never attached in M1.1/M1.2).
    sentHeaders: { 'X-Tenant-Id': headers['X-Tenant-Id'] || null, 'X-Session-Id': headers['X-Session-Id'] || null, 'Host': headers['Host'] || null },
  };
  return { res: res, meta: meta };
}

export function getWithMetadata(url, ctx) {
  return requestWithMetadata({
    method: 'GET',
    url: url,
    tenantId: ctx.tenantId,
    sessionId: ctx.sessionId,
    hostOverride: ctx.hostOverride,
    tags: ctx.tags,
    headers: ctx.headers,
    params: ctx.params,
  });
}
