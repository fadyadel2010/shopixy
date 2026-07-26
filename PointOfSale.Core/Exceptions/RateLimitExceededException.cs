namespace PointOfSale.Core.Exceptions;

public class RateLimitExceededException : Exception
{
    public int MaxMessages { get; }
    public TimeSpan Window { get; }

    public RateLimitExceededException(int maxMessages, TimeSpan window)
        : base($"Rate limit exceeded: Maximum {maxMessages} messages per {window.TotalMinutes} minutes")
    {
        MaxMessages = maxMessages;
        Window = window;
    }
}
