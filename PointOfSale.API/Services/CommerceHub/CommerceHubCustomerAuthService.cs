using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using PointOfSale.API.DTOs.Customers;
using PointOfSale.API.Models;
using PointOfSale.API.Services.Auth;
using PointOfSale.API.Services.Governance;
using PointOfSale.API.Security;
using PointOfSale.Core.Entities;
using PointOfSale.Core.Enums;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.API.Services.CommerceHub;

public class CommerceHubCustomerAuthResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public CustomerAuthResponseDto? Response { get; set; }
}

public interface ICommerceHubCustomerAuthService
{
    Task<CommerceHubCustomerAuthResult> RegisterAsync(CustomerRegisterRequestDto request, CancellationToken cancellationToken = default);
    Task<CommerceHubCustomerAuthResult> LoginAsync(CustomerLoginRequestDto request, CancellationToken cancellationToken = default);
    Task<CustomerProfileDto?> GetProfileAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task<CommerceHubCustomerAuthResult> ForgotPasswordAsync(string email, string? ipAddress, CancellationToken cancellationToken = default);
    Task<CommerceHubCustomerAuthResult> ResetPasswordAsync(string token, string newPassword, string? ipAddress, CancellationToken cancellationToken = default);
    Task<CommerceHubCustomerAuthResult> VerifyEmailAsync(string token, CancellationToken cancellationToken = default);
    Task<CommerceHubCustomerAuthResult> ResendVerificationEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<CommerceHubCustomerAuthResult> UpdateProfileAsync(Guid customerId, CustomerUpdateProfileRequestDto request, CancellationToken cancellationToken = default);
    Task<CommerceHubCustomerAuthResult> ChangePasswordAsync(Guid customerId, CustomerChangePasswordRequestDto request, CancellationToken cancellationToken = default);
    Task<CommerceHubCustomerAuthResult> ChangeEmailAsync(Guid customerId, CustomerChangeEmailRequestDto request, CancellationToken cancellationToken = default);
    Task RecordIdentityEventAsync(Guid customerId, string eventType, string? description = null, string? metadata = null, string? ipAddress = null, CancellationToken cancellationToken = default);
}

public sealed class CommerceHubCustomerIdentityOptions
{
    public const string SectionName = "CommerceHub:CustomerIdentity";

    public string PublicBaseUrl { get; set; } = "http://localhost:5173";
}

public class CommerceHubCustomerAuthService : ICommerceHubCustomerAuthService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly CustomerJwtSettings _jwtSettings;
    private readonly IPasswordHasher<CommerceHubCustomer> _passwordHasher;
    private readonly IUserGovernanceService _userGovernanceService;
    private readonly IEmailSender _emailSender;
    private readonly CommerceHubCustomerIdentityOptions _identityOptions;
    private readonly ILogger<CommerceHubCustomerAuthService> _logger;

    public CommerceHubCustomerAuthService(
        ApplicationDbContext dbContext,
        IOptions<CustomerJwtSettings> jwtSettings,
        IOptions<CommerceHubCustomerIdentityOptions> identityOptions,
        IPasswordHasher<CommerceHubCustomer> passwordHasher,
        IUserGovernanceService userGovernanceService,
        IEmailSender emailSender,
        ILogger<CommerceHubCustomerAuthService> logger)
    {
        _dbContext = dbContext;
        _jwtSettings = jwtSettings.Value;
        _identityOptions = identityOptions.Value;
        _passwordHasher = passwordHasher;
        _userGovernanceService = userGovernanceService;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task<CommerceHubCustomerAuthResult> RegisterAsync(
        CustomerRegisterRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.Name))
        {
            return Fail("INVALID_REQUEST", "Email, password, and name are required.");
        }

        if (request.Password.Trim().Length < 8)
        {
            return Fail("WEAK_PASSWORD", "Password must be at least 8 characters.");
        }

        try
        {
            await _userGovernanceService.EnsureRegistrationAllowedAsync(
                request.Email,
                request.PhoneNumber,
                ipAddress: null,
                deviceFingerprint: null,
                cancellationToken);
        }
        catch (UserGovernanceException exception)
        {
            _logger.LogWarning(
                exception,
                "CommerceHub customer registration blocked by governance policy | Email: {Email}",
                request.Email);
            return Fail(exception.Code, "An unexpected error occurred");
        }

        var email = CommerceHubCustomer.NormalizeEmail(request.Email);

        // SECURITY: IgnoreQueryFilters is intentional here because CommerceHub
        // customer identity is global. We still only match the exact normalized email.
        var existingCustomer = await _dbContext.CommerceHubCustomers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(customer => customer.Email == email, cancellationToken);

        if (existingCustomer != null)
        {
            return Fail("EMAIL_EXISTS", "A customer account with this email already exists.");
        }

        var customer = CommerceHubCustomer.Create(email, request.Name, request.PhoneNumber, null);
        customer.SetPasswordHash(_passwordHasher.HashPassword(customer, request.Password));
        var verificationToken = CreateEmailVerificationToken(customer.Id);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            _dbContext.CommerceHubCustomers.Add(customer);
            _dbContext.CommerceHubCustomerEmailVerificationTokens.Add(verificationToken.Entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _userGovernanceService.EnsureProfileExistsAsync(
                UserGovernanceRole.Customer,
                customer.Id,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsEmailUniquenessViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            return Fail("EMAIL_EXISTS", "A customer account with this email already exists.");
        }

        await RecordIdentityEventAsync(
            customer.Id,
            CommerceHubCustomerIdentityEvent.EventTypes.Registered,
            "Customer account registered",
            cancellationToken: cancellationToken);
        await RecordIdentityEventAsync(
            customer.Id,
            CommerceHubCustomerIdentityEvent.EventTypes.EmailVerificationRequested,
            "Verification email issued during registration",
            cancellationToken: cancellationToken);
        await TrySendVerificationEmailAsync(customer, verificationToken.RawToken);

        _logger.LogInformation(
            "CommerceHub customer registered | CustomerId: {CustomerId} | Email: {Email}",
            customer.Id,
            customer.Email);

        return new CommerceHubCustomerAuthResult
        {
            Success = true,
            Message = "Account created. Check your email to verify the address before signing in."
        };
    }

    public async Task<CommerceHubCustomerAuthResult> LoginAsync(
        CustomerLoginRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Fail("INVALID_REQUEST", "Email and password are required.");
        }

        var email = CommerceHubCustomer.NormalizeEmail(request.Email);

        // SECURITY: IgnoreQueryFilters is intentional because customer login runs
        // on the global CommerceHub identity space, not a tenant-scoped user table.
        var customer = await _dbContext.CommerceHubCustomers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(current => !current.IsDeleted && current.Email == email, cancellationToken);

        if (customer == null)
        {
            return Fail("INVALID_CREDENTIALS", "Invalid email or password.");
        }

        if (string.IsNullOrWhiteSpace(customer.PasswordHash))
        {
            return Fail("PASSWORD_NOT_SET", "This order-history account cannot sign in yet. Please contact support.");
        }

        if (!customer.IsEmailVerified)
        {
            return Fail("EMAIL_NOT_VERIFIED", "Verify your email address before signing in.");
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(customer, customer.PasswordHash, request.Password);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return Fail("INVALID_CREDENTIALS", "Invalid email or password.");
        }

        try
        {
            await _userGovernanceService.EnsureCustomerLoginAllowedAsync(customer.Id, cancellationToken);
        }
        catch (UserGovernanceException exception)
        {
            _logger.LogWarning(
                exception,
                "CommerceHub customer login blocked by governance policy | CustomerId: {CustomerId}",
                customer.Id);
            return Fail(exception.Code, "An unexpected error occurred");
        }

        if (verificationResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            customer.SetPasswordHash(_passwordHasher.HashPassword(customer, request.Password));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _userGovernanceService.RecordAccessAsync(
            UserGovernanceRole.Customer,
            customer.Id,
            cancellationToken);
        await RecordIdentityEventAsync(
            customer.Id,
            CommerceHubCustomerIdentityEvent.EventTypes.LoggedIn,
            "Customer logged in",
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "CommerceHub customer logged in | CustomerId: {CustomerId} | Email: {Email}",
            customer.Id,
            customer.Email);

        return new CommerceHubCustomerAuthResult
        {
            Success = true,
            Response = BuildAuthResponse(customer)
        };
    }

    public async Task<CustomerProfileDto?> GetProfileAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        if (customerId == Guid.Empty)
        {
            return null;
        }

        var customer = await _dbContext.CommerceHubCustomers
            .AsNoTracking()
            .FirstOrDefaultAsync(current => !current.IsDeleted && current.Id == customerId, cancellationToken);

        return customer == null ? null : MapProfile(customer);
    }

    private CustomerAuthResponseDto BuildAuthResponse(CommerceHubCustomer customer)
    {
        var issuedAtUtc = DateTime.UtcNow;
        var expiresAt = issuedAtUtc.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);
        return new CustomerAuthResponseDto
        {
            AccessToken = GenerateToken(customer, issuedAtUtc, expiresAt),
            ExpiresAt = expiresAt,
            Customer = MapProfile(customer)
        };
    }

    private string GenerateToken(CommerceHubCustomer customer, DateTime issuedAtUtc, DateTime expiresAt)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new("CustomerId", customer.Id.ToString()),
            new(ClaimTypes.NameIdentifier, customer.Id.ToString()),
            new(ClaimTypes.Email, customer.Email),
            new(ClaimTypes.Role, CustomerSecurityConstants.Role),
            new(JwtRegisteredClaimNames.Sub, customer.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, customer.Email),
            new(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(issuedAtUtc).ToString(), ClaimValueTypes.Integer64),
            new(CustomerSecurityConstants.SessionIssuedAtTicksClaim, issuedAtUtc.Ticks.ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (!string.IsNullOrWhiteSpace(customer.Name))
        {
            claims.Add(new Claim(ClaimTypes.Name, customer.Name));
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt,
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(descriptor);
        return tokenHandler.WriteToken(token);
    }

    private static CustomerProfileDto MapProfile(CommerceHubCustomer customer)
    {
        return new CustomerProfileDto
        {
            Id = customer.Id,
            Email = customer.Email,
            Name = customer.Name,
            PhoneNumber = customer.PhoneNumber,
            IsEmailVerified = customer.IsEmailVerified,
            CreatedAt = customer.CreatedAt,
            NavigationCapabilities =
            [
                "customer.account.view",
                "customer.account.settings",
                "customer.orders.view",
                "customer.refunds.view"
            ]
        };
    }

    // ========================================================================================
    // PASSWORD RECOVERY — SQL-authoritative, hashed single-use expiring tokens
    // ========================================================================================

    public async Task<CommerceHubCustomerAuthResult> ForgotPasswordAsync(
        string email,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        // PRIVACY: Always return success to prevent user enumeration.
        if (string.IsNullOrWhiteSpace(email))
        {
            return new CommerceHubCustomerAuthResult { Success = true, Message = "If that email is registered, a reset link has been sent." };
        }

        var normalizedEmail = CommerceHubCustomer.NormalizeEmail(email);

        var customer = await _dbContext.CommerceHubCustomers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => !c.IsDeleted && c.Email == normalizedEmail, cancellationToken);

        if (customer == null)
        {
            _logger.LogInformation("Password reset requested for a non-existent CommerceHub customer identity.");
            return new CommerceHubCustomerAuthResult { Success = true, Message = "If that email is registered, a reset link has been sent." };
        }

        var now = DateTime.UtcNow;
        var rawToken = CommerceHubCustomerPasswordResetToken.GenerateRawToken();
        var tokenHash = CommerceHubCustomerPasswordResetToken.HashToken(rawToken);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _dbContext.CommerceHubCustomerPasswordResetTokens
            .Where(t => t.CustomerId == customer.Id && !t.IsUsed && t.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.IsUsed, true)
                    .SetProperty(t => t.UsedAtUtc, now),
                cancellationToken);
        _dbContext.CommerceHubCustomerPasswordResetTokens.Add(new CommerceHubCustomerPasswordResetToken
        {
            Id = Guid.NewGuid(),
            CustomerId = customer.Id,
            TokenHash = tokenHash,
            ExpiresAtUtc = now.AddMinutes(CommerceHubCustomerPasswordResetToken.TokenExpirationMinutes),
            IsUsed = false,
            CreatedAtUtc = now,
            CreatedByIp = ipAddress
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await RecordIdentityEventAsync(
            customer.Id,
            CommerceHubCustomerIdentityEvent.EventTypes.PasswordResetRequested,
            "Password reset token generated",
            ipAddress: ipAddress,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Password reset token generated | CustomerId: {CustomerId} | Email: {Email}",
            customer.Id,
            customer.Email);

        await TrySendPasswordResetEmailAsync(customer, rawToken);

        return new CommerceHubCustomerAuthResult
        {
            Success = true,
            Message = "If that email is registered, a reset link has been sent."
        };
    }

    public async Task<CommerceHubCustomerAuthResult> ResetPasswordAsync(
        string token,
        string newPassword,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(newPassword))
        {
            return Fail("INVALID_REQUEST", "Token and new password are required.");
        }

        if (newPassword.Trim().Length < 8)
        {
            return Fail("WEAK_PASSWORD", "Password must be at least 8 characters.");
        }

        var tokenHash = CommerceHubCustomerPasswordResetToken.HashToken(token);

        var now = DateTime.UtcNow;
        var resetToken = await _dbContext.CommerceHubCustomerPasswordResetTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (resetToken == null)
        {
            return Fail("INVALID_TOKEN", "This reset link is invalid or has already been used.");
        }

        if (resetToken.IsUsed)
        {
            return Fail("TOKEN_ALREADY_USED", "This reset link has already been used.");
        }

        if (resetToken.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return Fail("TOKEN_EXPIRED", "This reset link has expired. Please request a new one.");
        }

        var customer = await _dbContext.CommerceHubCustomers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == resetToken.CustomerId && !c.IsDeleted, cancellationToken);

        if (customer == null)
        {
            return Fail("CUSTOMER_NOT_FOUND", "Customer account not found.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var consumed = await _dbContext.CommerceHubCustomerPasswordResetTokens
            .Where(t => t.Id == resetToken.Id && !t.IsUsed && t.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.IsUsed, true)
                    .SetProperty(t => t.UsedAtUtc, now),
                cancellationToken);
        if (consumed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Fail("TOKEN_ALREADY_USED", "This reset link has already been used.");
        }

        // Update password
        customer.SetPasswordHash(_passwordHasher.HashPassword(customer, newPassword));

        // SECURITY: Revoke all sessions after password reset
        customer.RevokeAllSessions(now);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await RecordIdentityEventAsync(
            customer.Id,
            CommerceHubCustomerIdentityEvent.EventTypes.PasswordResetCompleted,
            "Password reset completed, all sessions revoked",
            ipAddress: ipAddress,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Password reset completed | CustomerId: {CustomerId} | Email: {Email}",
            customer.Id,
            customer.Email);

        return new CommerceHubCustomerAuthResult
        {
            Success = true,
            Message = "Your password has been reset. Please sign in with your new password."
        };
    }

    // ========================================================================================
    // EMAIL VERIFICATION — SQL-authoritative, hashed single-use expiring tokens
    // ========================================================================================

    public async Task<CommerceHubCustomerAuthResult> VerifyEmailAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Fail("INVALID_REQUEST", "Verification token is required.");
        }

        var tokenHash = CommerceHubCustomerEmailVerificationToken.HashToken(token);

        var now = DateTime.UtcNow;
        var verificationToken = await _dbContext.CommerceHubCustomerEmailVerificationTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (verificationToken == null)
        {
            return Fail("INVALID_TOKEN", "This verification link is invalid or has already been used.");
        }

        if (verificationToken.IsUsed)
        {
            return Fail("TOKEN_ALREADY_USED", "This email has already been verified.");
        }

        if (verificationToken.ExpiresAtUtc <= DateTime.UtcNow)
        {
            return Fail("TOKEN_EXPIRED", "This verification link has expired. Please request a new one.");
        }

        var customer = await _dbContext.CommerceHubCustomers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == verificationToken.CustomerId && !c.IsDeleted, cancellationToken);

        if (customer == null)
        {
            return Fail("CUSTOMER_NOT_FOUND", "Customer account not found.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var consumed = await _dbContext.CommerceHubCustomerEmailVerificationTokens
            .Where(t => t.Id == verificationToken.Id && !t.IsUsed && t.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.IsUsed, true)
                    .SetProperty(t => t.UsedAtUtc, now),
                cancellationToken);
        if (consumed != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Fail("TOKEN_ALREADY_USED", "This email has already been verified.");
        }

        // Mark email as verified
        customer.MarkEmailVerified();

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await RecordIdentityEventAsync(
            customer.Id,
            CommerceHubCustomerIdentityEvent.EventTypes.EmailVerified,
            "Email verified",
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Email verified | CustomerId: {CustomerId} | Email: {Email}",
            customer.Id,
            customer.Email);

        return new CommerceHubCustomerAuthResult
        {
            Success = true,
            Message = "Your email has been verified."
        };
    }

    public async Task<CommerceHubCustomerAuthResult> ResendVerificationEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        const string genericMessage = "If that account needs verification, a verification email has been sent.";
        if (string.IsNullOrWhiteSpace(email))
        {
            return new CommerceHubCustomerAuthResult { Success = true, Message = genericMessage };
        }

        var normalizedEmail = CommerceHubCustomer.NormalizeEmail(email);
        var customer = await _dbContext.CommerceHubCustomers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Email == normalizedEmail && !c.IsDeleted, cancellationToken);

        if (customer == null)
        {
            return new CommerceHubCustomerAuthResult { Success = true, Message = genericMessage };
        }

        if (customer.IsEmailVerified)
        {
            return new CommerceHubCustomerAuthResult { Success = true, Message = genericMessage };
        }

        var now = DateTime.UtcNow;
        var verificationToken = CreateEmailVerificationToken(customer.Id, now);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await InvalidateEmailVerificationTokensAsync(customer.Id, now, cancellationToken);
        _dbContext.CommerceHubCustomerEmailVerificationTokens.Add(verificationToken.Entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await RecordIdentityEventAsync(
            customer.Id,
            CommerceHubCustomerIdentityEvent.EventTypes.EmailVerificationRequested,
            "Verification email resent",
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Verification email resent | CustomerId: {CustomerId} | Email: {Email}",
            customer.Id,
            customer.Email);

        await TrySendVerificationEmailAsync(customer, verificationToken.RawToken);

        return new CommerceHubCustomerAuthResult
        {
            Success = true,
            Message = genericMessage
        };
    }

    // ========================================================================================
    // PROFILE MUTATION — SQL-authoritative, authenticated customer only
    // ========================================================================================

    public async Task<CommerceHubCustomerAuthResult> UpdateProfileAsync(
        Guid customerId,
        CustomerUpdateProfileRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Fail("INVALID_REQUEST", "Name is required.");
        }

        var customer = await _dbContext.CommerceHubCustomers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == customerId && !c.IsDeleted, cancellationToken);

        if (customer == null)
        {
            return Fail("CUSTOMER_NOT_FOUND", "Customer account not found.");
        }

        customer.UpdateProfile(request.Name, request.PhoneNumber);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await RecordIdentityEventAsync(
            customer.Id,
            CommerceHubCustomerIdentityEvent.EventTypes.ProfileUpdated,
            "Profile updated",
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Customer profile updated | CustomerId: {CustomerId}",
            customer.Id);

        return new CommerceHubCustomerAuthResult
        {
            Success = true,
            Message = "Profile updated.",
            Response = new CustomerAuthResponseDto
            {
                Customer = MapProfile(customer),
                ExpiresAt = DateTime.UtcNow
            }
        };
    }

    public async Task<CommerceHubCustomerAuthResult> ChangePasswordAsync(
        Guid customerId,
        CustomerChangePasswordRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return Fail("INVALID_REQUEST", "Current password and new password are required.");
        }

        if (request.NewPassword.Trim().Length < 8)
        {
            return Fail("WEAK_PASSWORD", "New password must be at least 8 characters.");
        }

        var customer = await _dbContext.CommerceHubCustomers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == customerId && !c.IsDeleted, cancellationToken);

        if (customer == null)
        {
            return Fail("CUSTOMER_NOT_FOUND", "Customer account not found.");
        }

        if (string.IsNullOrWhiteSpace(customer.PasswordHash))
        {
            return Fail("PASSWORD_NOT_SET", "Account does not have a password set.");
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(customer, customer.PasswordHash, request.CurrentPassword);
        if (verificationResult == PasswordVerificationResult.Failed)
        {
            return Fail("INVALID_CURRENT_PASSWORD", "The current password is incorrect.");
        }

        customer.SetPasswordHash(_passwordHasher.HashPassword(customer, request.NewPassword));

        // SECURITY: Revoke all other sessions after password change
        customer.RevokeAllSessions(DateTime.UtcNow);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await RecordIdentityEventAsync(
            customer.Id,
            CommerceHubCustomerIdentityEvent.EventTypes.PasswordChanged,
            "Password changed by customer, all sessions revoked",
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Customer password changed | CustomerId: {CustomerId}",
            customer.Id);

        return new CommerceHubCustomerAuthResult
        {
            Success = true,
            Message = "Your password has been changed. You will need to sign in again on other devices."
        };
    }

    // ========================================================================================
    // IDENTITY EVENT LEDGER — append-only SQL evidence
    // ========================================================================================

    public async Task<CommerceHubCustomerAuthResult> ChangeEmailAsync(
        Guid customerId,
        CustomerChangeEmailRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewEmail))
        {
            return Fail("INVALID_REQUEST", "Current password and new email are required.");
        }

        var customer = await _dbContext.CommerceHubCustomers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == customerId && !c.IsDeleted, cancellationToken);

        if (customer == null)
        {
            return Fail("CUSTOMER_NOT_FOUND", "Customer account not found.");
        }

        if (string.IsNullOrWhiteSpace(customer.PasswordHash) ||
            _passwordHasher.VerifyHashedPassword(customer, customer.PasswordHash, request.CurrentPassword) ==
            PasswordVerificationResult.Failed)
        {
            return Fail("INVALID_CURRENT_PASSWORD", "The current password is incorrect.");
        }

        var normalizedEmail = CommerceHubCustomer.NormalizeEmail(request.NewEmail);
        if (string.Equals(normalizedEmail, customer.Email, StringComparison.Ordinal))
        {
            return Fail("EMAIL_UNCHANGED", "Enter a different email address.");
        }

        var existingCustomer = await _dbContext.CommerceHubCustomers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(c => c.Email == normalizedEmail && c.Id != customerId, cancellationToken);
        if (existingCustomer)
        {
            return Fail("EMAIL_EXISTS", "A customer account with this email already exists.");
        }

        var now = DateTime.UtcNow;
        var verificationToken = CreateEmailVerificationToken(customer.Id, now);
        var previousEmail = customer.Email;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            customer.SetEmail(normalizedEmail);
            customer.MarkEmailUnverified();
            customer.RevokeAllSessions(now);
            await InvalidateEmailVerificationTokensAsync(customer.Id, now, cancellationToken);
            _dbContext.CommerceHubCustomerEmailVerificationTokens.Add(verificationToken.Entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsEmailUniquenessViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            _dbContext.ChangeTracker.Clear();
            return Fail("EMAIL_EXISTS", "A customer account with this email already exists.");
        }

        await RecordIdentityEventAsync(
            customer.Id,
            CommerceHubCustomerIdentityEvent.EventTypes.EmailChanged,
            "Customer email changed, verification required, all sessions revoked",
            metadata: $"previousEmail={previousEmail}",
            cancellationToken: cancellationToken);
        await RecordIdentityEventAsync(
            customer.Id,
            CommerceHubCustomerIdentityEvent.EventTypes.EmailVerificationRequested,
            "Verification email issued after email change",
            cancellationToken: cancellationToken);
        await TrySendVerificationEmailAsync(customer, verificationToken.RawToken);

        return new CommerceHubCustomerAuthResult
        {
            Success = true,
            Message = "Your email has been changed. Verify the new address and sign in again."
        };
    }

    private async Task InvalidateEmailVerificationTokensAsync(
        Guid customerId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await _dbContext.CommerceHubCustomerEmailVerificationTokens
            .Where(t => t.CustomerId == customerId && !t.IsUsed && t.ExpiresAtUtc > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.IsUsed, true)
                    .SetProperty(t => t.UsedAtUtc, now),
                cancellationToken);
    }

    private static IssuedEmailVerificationToken CreateEmailVerificationToken(Guid customerId, DateTime? now = null)
    {
        var issuedAtUtc = now ?? DateTime.UtcNow;
        var rawToken = CommerceHubCustomerEmailVerificationToken.GenerateRawToken();
        return new IssuedEmailVerificationToken(
            rawToken,
            new CommerceHubCustomerEmailVerificationToken
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                TokenHash = CommerceHubCustomerEmailVerificationToken.HashToken(rawToken),
                ExpiresAtUtc = issuedAtUtc.AddHours(CommerceHubCustomerEmailVerificationToken.TokenExpirationHours),
                IsUsed = false,
                CreatedAtUtc = issuedAtUtc
            });
    }

    private async Task TrySendPasswordResetEmailAsync(CommerceHubCustomer customer, string rawToken)
    {
        try
        {
            var resetUrl = BuildCustomerUrl("/reset-password", rawToken);
            await _emailSender.SendEmailAsync(
                customer.Email,
                "Reset your Shopixy password",
                $"<p>Hi {System.Net.WebUtility.HtmlEncode(customer.Name)},</p><p>Use this link to reset your password:</p><p><a href=\"{System.Net.WebUtility.HtmlEncode(resetUrl)}\">Reset password</a></p><p>This link expires in {CommerceHubCustomerPasswordResetToken.TokenExpirationMinutes} minutes.</p>",
                $"Hi {customer.Name},\n\nReset your password:\n{resetUrl}\n\nThis link expires in {CommerceHubCustomerPasswordResetToken.TokenExpirationMinutes} minutes.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "CommerceHub customer password-reset email delivery failed");
        }
    }

    private async Task TrySendVerificationEmailAsync(CommerceHubCustomer customer, string rawToken)
    {
        try
        {
            var verificationUrl = BuildCustomerUrl("/verify-email", rawToken);
            await _emailSender.SendEmailAsync(
                customer.Email,
                "Verify your Shopixy email",
                $"<p>Hi {System.Net.WebUtility.HtmlEncode(customer.Name)},</p><p>Use this link to verify your email:</p><p><a href=\"{System.Net.WebUtility.HtmlEncode(verificationUrl)}\">Verify email</a></p><p>This link expires in {CommerceHubCustomerEmailVerificationToken.TokenExpirationHours} hours.</p>",
                $"Hi {customer.Name},\n\nVerify your email:\n{verificationUrl}\n\nThis link expires in {CommerceHubCustomerEmailVerificationToken.TokenExpirationHours} hours.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "CommerceHub customer verification email delivery failed");
        }
    }

    private string BuildCustomerUrl(string path, string rawToken)
    {
        var baseUrl = _identityOptions.PublicBaseUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("CommerceHub:CustomerIdentity:PublicBaseUrl must be an absolute URL.");
        }

        return $"{baseUrl}{path}?token={Uri.EscapeDataString(rawToken)}";
    }

    public async Task RecordIdentityEventAsync(
        Guid customerId,
        string eventType,
        string? description = null,
        string? metadata = null,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        _dbContext.CommerceHubCustomerIdentityEvents.Add(new CommerceHubCustomerIdentityEvent
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            EventType = eventType,
            Description = description,
            Metadata = metadata,
            IpAddress = ipAddress,
            OccurredAtUtc = DateTime.UtcNow
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // SECURITY: Identity events must never block the primary flow.
            // Log the failure but do not throw.
            _logger.LogError(
                ex,
                "Failed to record identity lifecycle event | CustomerId: {CustomerId} | EventType: {EventType}",
                customerId,
                eventType);
        }
    }

    private static CommerceHubCustomerAuthResult Fail(string error, string message) =>
        new()
        {
            Success = false,
            Error = error,
            Message = message
        };

    private static bool IsEmailUniquenessViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgresException &&
        postgresException.SqlState == PostgresErrorCodes.UniqueViolation &&
        string.Equals(
            postgresException.ConstraintName,
            "IX_CommerceHubCustomers_Email",
            StringComparison.Ordinal);

    private sealed record IssuedEmailVerificationToken(
        string RawToken,
        CommerceHubCustomerEmailVerificationToken Entity);
}
