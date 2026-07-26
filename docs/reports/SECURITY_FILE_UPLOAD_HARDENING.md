# Security File Upload Hardening - Manual Wallet Controller

## Summary
Enhanced file upload security in `StorefrontManualWalletController` to protect against malicious file uploads in the anonymous endpoint.

## Changes Made

### 1. Created FileValidationService (`PointOfSale.API/Services/FileValidationService.cs`)
- Validates files using multiple security layers:
  - **Extension allowlist**: Only `.png`, `.jpg`, `.jpeg`, `.pdf`
  - **MIME type validation**: Ensures Content-Type matches the file extension
  - **Magic bytes verification**: Reads file signatures to detect actual file type
  
- Supported file signatures:
  - PNG: `89 50 4E 47 0D 0A 1A 0A`
  - JPEG: `FF D8 FF E0/E1/E2/E3/E8`
  - PDF: `25 50 44 46` (%PDF)

### 2. Updated StorefrontManualWalletController
- Added `IFileValidationService` dependency injection
- Replaced simple extension check with comprehensive validation
- Implemented two-stage upload process:
  1. **Quarantine stage**: Files first saved to `quarantine/{tenantId}/manual-payments/`
  2. **Production stage**: After validation, moved to `secure-uploads/{tenantId}/manual-payments/`

### 3. Registered Service in DI Container
- Added `IFileValidationService` registration in `Program.cs`

## Security Benefits

1. **Content-based validation**: Cannot be bypassed by renaming files
2. **MIME type verification**: Prevents Content-Type spoofing
3. **Quarantine workflow**: Ready for antivirus/malware scanning integration
4. **Defense in depth**: Multiple validation layers
5. **Detailed logging**: Failed validations are logged with context

## Integration Points for Production

The quarantine directory is ready for integration with:
- Antivirus scanning services (ClamAV, Windows Defender, etc.)
- Cloud-based malware detection (AWS GuardDuty, Azure Defender)
- Custom security scanning pipelines

Add scanning logic between quarantine save and final move in `UploadProof` method.

## Testing Recommendations

1. Test with valid image files (PNG, JPEG)
2. Test with renamed executables (e.g., `malware.exe` → `malware.jpg`)
3. Test with files having mismatched MIME types
4. Test with corrupted/truncated files
5. Verify quarantine directory cleanup on failures
