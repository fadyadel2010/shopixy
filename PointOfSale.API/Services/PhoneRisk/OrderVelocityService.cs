using PointOfSale.Infrastructure.Redis.Services;
using StackExchange.Redis;

namespace PointOfSale.API.Services.PhoneRisk;

public class OrderVelocityService : IOrderVelocityService
{
    private const int VelocityWindowSeconds = 300;
    private const int IpVelocityThreshold = 5;
    private const int PhoneVelocityThreshold = 3;

    private readonly RedisConnectionFactory _redisConnectionFactory;
    private readonly ILogger<OrderVelocityService> _logger;

    public OrderVelocityService(
        RedisConnectionFactory redisConnectionFactory,
        ILogger<OrderVelocityService> logger)
    {
        _redisConnectionFactory = redisConnectionFactory;
        _logger = logger;
    }

    public async Task<OrderVelocityAssessment> TrackOrderAsync(
        string? ipAddress,
        string? normalizedPhoneNumber,
        CancellationToken cancellationToken = default)
    {
        var database = _redisConnectionFactory.GetDatabase();
        if (database == null)
        {
            return new OrderVelocityAssessment();
        }

        try
        {
            var assessment = new OrderVelocityAssessment();

            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                var ipKey = BuildIpKey(ipAddress);
                assessment.IpOrderCount = await IncrementWindowAsync(database, ipKey);
                assessment.IpVelocityTriggered = assessment.IpOrderCount >= IpVelocityThreshold;
            }

            if (!string.IsNullOrWhiteSpace(normalizedPhoneNumber))
            {
                var phoneKey = BuildPhoneKey(normalizedPhoneNumber);
                assessment.PhoneOrderCount = await IncrementWindowAsync(database, phoneKey);
                assessment.PhoneVelocityTriggered = assessment.PhoneOrderCount >= PhoneVelocityThreshold;
            }

            return assessment;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to track order velocity | IpAddress: {IpAddress} | Phone: {Phone}",
                ipAddress ?? string.Empty,
                normalizedPhoneNumber ?? string.Empty);

            return new OrderVelocityAssessment();
        }
    }

    public async Task<OrderVelocityAssessment> GetCurrentAssessmentAsync(
        string? ipAddress,
        string? normalizedPhoneNumber,
        CancellationToken cancellationToken = default)
    {
        var database = _redisConnectionFactory.GetDatabase();
        if (database == null)
        {
            return new OrderVelocityAssessment();
        }

        try
        {
            var assessment = new OrderVelocityAssessment();

            if (!string.IsNullOrWhiteSpace(ipAddress))
            {
                var ipValue = await database.StringGetAsync(BuildIpKey(ipAddress));
                assessment.IpOrderCount = TryParseCount(ipValue);
                assessment.IpVelocityTriggered = assessment.IpOrderCount >= IpVelocityThreshold;
            }

            if (!string.IsNullOrWhiteSpace(normalizedPhoneNumber))
            {
                var phoneValue = await database.StringGetAsync(BuildPhoneKey(normalizedPhoneNumber));
                assessment.PhoneOrderCount = TryParseCount(phoneValue);
                assessment.PhoneVelocityTriggered = assessment.PhoneOrderCount >= PhoneVelocityThreshold;
            }

            return assessment;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to read order velocity | IpAddress: {IpAddress} | Phone: {Phone}",
                ipAddress ?? string.Empty,
                normalizedPhoneNumber ?? string.Empty);

            return new OrderVelocityAssessment();
        }
    }

    private static string BuildIpKey(string ipAddress) => $"orders:ip:{ipAddress.Trim()}";

    private static string BuildPhoneKey(string phoneNumber) => $"orders:phone:{phoneNumber.Trim()}";

    private static int TryParseCount(RedisValue value)
    {
        return value.HasValue && int.TryParse(value.ToString(), out var count)
            ? count
            : 0;
    }

    private static async Task<int> IncrementWindowAsync(IDatabase database, string key)
    {
        var count = await database.StringIncrementAsync(key);
        await database.KeyExpireAsync(key, TimeSpan.FromSeconds(VelocityWindowSeconds));
        return (int)count;
    }
}
