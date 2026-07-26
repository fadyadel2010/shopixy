namespace PointOfSale.API.Security;

public sealed class TokenRevocationCheckUnavailableException : Exception
{
    public TokenRevocationCheckUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

public sealed class TokenRevocationPersistenceException : Exception
{
    public TokenRevocationPersistenceException(string message, Exception? innerException = null, Guid? subjectId = null)
        : base(message, innerException)
    {
        SubjectId = subjectId;
    }

    public Guid? SubjectId { get; }
}

public sealed class CustomerTokenSessionRevokedException : Exception
{
    public CustomerTokenSessionRevokedException(string message)
        : base(message)
    {
    }
}
