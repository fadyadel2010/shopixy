# Prometheus Metrics Endpoint - Deployment Guide

## Overview

The `/metrics` endpoint exposes OpenTelemetry metrics for Prometheus scraping. This guide explains the security model and deployment options.

## Breaking Change Notice

**Previous Behavior**: The `/metrics` endpoint required SuperAdmin JWT authentication.

**Current Behavior**: The `/metrics` endpoint is **unauthenticated by default** and relies on network-level security.

**Impact**: Existing Prometheus scrape configurations that included SuperAdmin JWT tokens can be simplified.

---

## Security Model

### Default Configuration (Recommended)

The `/metrics` endpoint is **unauthenticated** and should be protected at the network level:

- **Internal Networks**: Deploy behind a firewall or VPC that restricts access to monitoring infrastructure only
- **Cloud Environments**: Use security groups, network ACLs, or private subnets
- **Kubernetes**: Use NetworkPolicies to restrict access to Prometheus pods

### Why Unauthenticated?

1. **Standard Practice**: Prometheus scraping is typically done by infrastructure systems without user-level authentication
2. **Operational Data Only**: The `/metrics` endpoint exposes operational metrics (request counts, latencies, error rates) but **no customer PII**
3. **Simplicity**: Avoids the complexity of managing credentials for monitoring infrastructure

### What Data is Exposed?

The `/metrics` endpoint includes:
- ✅ HTTP request counts and latencies
- ✅ Database connection pool metrics
- ✅ Custom business metrics (cart abandonment rates, order counts)
- ✅ System metrics (CPU, memory, GC)
- ❌ **No customer PII** (names, emails, addresses)
- ❌ **No authentication tokens**
- ❌ **No sensitive business data** (individual order details, payment info)

---

## Deployment Options

### Option 1: Network-Level Security (Default)

**Recommended for**: Internal networks, VPCs, Kubernetes clusters

**Configuration**: No code changes required. The endpoint is already unauthenticated.

**Prometheus Scrape Config**:
```yaml
scrape_configs:
  - job_name: 'pointofsale-api'
    static_configs:
      - targets: ['api.internal:5000']
    metrics_path: '/metrics'
    scrape_interval: 15s
```

**Network Security Examples**:

**AWS Security Group**:
```hcl
resource "aws_security_group_rule" "prometheus_scrape" {
  type              = "ingress"
  from_port         = 5000
  to_port           = 5000
  protocol          = "tcp"
  source_security_group_id = aws_security_group.prometheus.id
  security_group_id = aws_security_group.api.id
  description       = "Allow Prometheus to scrape /metrics"
}
```

**Kubernetes NetworkPolicy**:
```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: allow-prometheus-scrape
spec:
  podSelector:
    matchLabels:
      app: pointofsale-api
  ingress:
  - from:
    - podSelector:
        matchLabels:
          app: prometheus
    ports:
    - protocol: TCP
      port: 5000
```

---

### Option 2: API Key Authentication (Optional)

**Recommended for**: Public-facing APIs, multi-tenant environments, compliance requirements

**Configuration**:

1. **Uncomment the API key middleware** in `Program.cs` (lines 2869-2905)

2. **Set the API key** in your environment:
   ```bash
   export PROMETHEUS_API_KEY="your-secure-random-key-here"
   ```

   Or in `appsettings.Production.json`:
   ```json
   {
     "PROMETHEUS_API_KEY": "your-secure-random-key-here"
   }
   ```

3. **Generate a secure key**:
   ```bash
   # Linux/macOS
   openssl rand -base64 32
   
   # PowerShell
   [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Minimum 0 -Maximum 256 }))
   ```

**Prometheus Scrape Config**:
```yaml
scrape_configs:
  - job_name: 'pointofsale-api'
    static_configs:
      - targets: ['api.example.com:443']
    metrics_path: '/metrics'
    scrape_interval: 15s
    scheme: https
    # Add API key header
    authorization:
      type: Bearer
      credentials_file: /etc/prometheus/api-key.txt
    # OR use basic_auth with custom header (requires Prometheus 2.26+)
    params:
      headers:
        - X-Prometheus-Api-Key: your-secure-random-key-here
```

**Testing**:
```bash
# Without API key (should fail if enabled)
curl https://api.example.com/metrics

# With API key
curl -H "X-Prometheus-Api-Key: your-secure-random-key-here" https://api.example.com/metrics
```

---

### Option 3: mTLS (Advanced)

**Recommended for**: High-security environments, zero-trust architectures

**Configuration**: Requires custom middleware and certificate management. Contact the platform team for implementation guidance.

---

## Migration from SuperAdmin JWT

If you previously configured Prometheus with SuperAdmin JWT authentication:

### Before (Old Configuration)
```yaml
scrape_configs:
  - job_name: 'pointofsale-api'
    static_configs:
      - targets: ['api.example.com:443']
    metrics_path: '/metrics'
    bearer_token: 'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...'  # SuperAdmin JWT
```

### After (New Configuration)
```yaml
scrape_configs:
  - job_name: 'pointofsale-api'
    static_configs:
      - targets: ['api.internal:5000']  # Internal network
    metrics_path: '/metrics'
    # No authentication required - protected by network security
```

---

## Monitoring Best Practices

1. **Network Isolation**: Always deploy the API in a private network with restricted access
2. **TLS Encryption**: Use HTTPS even for internal metrics endpoints
3. **Rate Limiting**: Consider adding rate limiting to prevent metrics endpoint abuse
4. **Alerting**: Set up alerts for unusual metrics access patterns
5. **Audit Logging**: Enable access logs for the `/metrics` endpoint

---

## Troubleshooting

### Prometheus Can't Scrape Metrics

**Symptom**: Prometheus shows targets as "down" or "unreachable"

**Solutions**:
1. Verify network connectivity: `curl http://api.internal:5000/metrics`
2. Check firewall rules and security groups
3. Verify the API is listening on the correct port
4. Check Prometheus logs: `kubectl logs -n monitoring prometheus-0`

### 401 Unauthorized Error

**Symptom**: Prometheus receives 401 errors when scraping

**Solutions**:
1. If API key authentication is enabled, verify the key is correct
2. Check the `X-Prometheus-Api-Key` header is being sent
3. Verify the `PROMETHEUS_API_KEY` environment variable is set on the API

### Metrics Not Updating

**Symptom**: Metrics are stale or not changing

**Solutions**:
1. Verify the API is receiving traffic
2. Check OpenTelemetry configuration in `Program.cs`
3. Restart the API to reinitialize metrics
4. Check for errors in API logs

---

## Security Considerations

### What to Monitor

- **Unusual Access Patterns**: High-frequency scraping from unknown IPs
- **Data Exfiltration**: Large volumes of metrics data being exported
- **Endpoint Abuse**: Repeated 401/403 errors indicating authentication attempts

### What NOT to Expose

Never include in metrics:
- ❌ Customer names, emails, phone numbers
- ❌ Authentication tokens or API keys
- ❌ Payment card numbers or financial account details
- ❌ Personally identifiable information (PII)

### Compliance

- **GDPR**: Metrics should not contain personal data
- **PCI-DSS**: Metrics should not contain cardholder data
- **HIPAA**: Metrics should not contain protected health information

---

## Support

For questions or issues:
1. Check the [OpenTelemetry documentation](https://opentelemetry.io/docs/)
2. Review the [Prometheus scraping guide](https://prometheus.io/docs/prometheus/latest/configuration/configuration/#scrape_config)
3. Contact the platform team for infrastructure support

---

## Changelog

### 2026-04-19
- **BREAKING**: Removed SuperAdmin JWT requirement from `/metrics` endpoint
- **ADDED**: Network-level security as default approach
- **ADDED**: Optional API key authentication middleware
- **ADDED**: Deployment guide and migration instructions
