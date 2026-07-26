namespace PointOfSale.API.DTOs.Customers;

public class CustomerRegisterRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
}

public class CustomerLoginRequestDto
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class CustomerProfileDto
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public bool IsEmailVerified { get; set; }
    public DateTime CreatedAt { get; set; }
    public IReadOnlyList<string> NavigationCapabilities { get; set; } = Array.Empty<string>();
}

public class CustomerAuthResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public CustomerProfileDto Customer { get; set; } = new();
}

public class CustomerForgotPasswordRequestDto
{
    public string Email { get; set; } = string.Empty;
}

public class CustomerResetPasswordRequestDto
{
    public string Token { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class CustomerVerifyEmailRequestDto
{
    public string Token { get; set; } = string.Empty;
}

public class CustomerResendVerificationRequestDto
{
    public string Email { get; set; } = string.Empty;
}

public class CustomerUpdateProfileRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
}

public class CustomerChangePasswordRequestDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class CustomerChangeEmailRequestDto
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewEmail { get; set; } = string.Empty;
}
