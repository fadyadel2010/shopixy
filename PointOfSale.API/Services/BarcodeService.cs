namespace PointOfSale.API.Services;

public interface IBarcodeService
{
    string GenerateEAN13();
    string GenerateCode128(string prefix = "PROD");
    bool ValidateEAN13(string barcode);
    bool ValidateCode128(string barcode);
}

public class BarcodeService : IBarcodeService
{
    private readonly Random _random = new();

    /// <summary>
    /// Generate a valid EAN-13 barcode
    /// </summary>
    public string GenerateEAN13()
    {
        // EAN-13 format: 12 digits + 1 check digit
        // We'll use a simple approach: random 12 digits + calculated check digit
        
        var digits = new int[12];
        for (int i = 0; i < 12; i++)
        {
            digits[i] = _random.Next(0, 10);
        }

        // Calculate check digit
        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            sum += digits[i] * (i % 2 == 0 ? 1 : 3);
        }
        int checkDigit = (10 - (sum % 10)) % 10;

        // Combine all digits
        return string.Join("", digits) + checkDigit;
    }

    /// <summary>
    /// Generate Code 128 barcode with prefix
    /// </summary>
    public string GenerateCode128(string prefix = "PROD")
    {
        // Code 128 can encode alphanumeric
        // Format: PREFIX-NNNNNN (e.g., PROD-000001)
        var number = _random.Next(1, 999999);
        return $"{prefix}-{number:D6}";
    }

    /// <summary>
    /// Validate EAN-13 barcode
    /// </summary>
    public bool ValidateEAN13(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode) || barcode.Length != 13)
            return false;

        if (!barcode.All(char.IsDigit))
            return false;

        // Validate check digit
        var digits = barcode.Select(c => int.Parse(c.ToString())).ToArray();
        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            sum += digits[i] * (i % 2 == 0 ? 1 : 3);
        }
        int calculatedCheckDigit = (10 - (sum % 10)) % 10;

        return calculatedCheckDigit == digits[12];
    }

    /// <summary>
    /// Validate Code 128 barcode format
    /// </summary>
    public bool ValidateCode128(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return false;

        // Basic validation: contains alphanumeric and hyphens
        return barcode.All(c => char.IsLetterOrDigit(c) || c == '-');
    }
}
