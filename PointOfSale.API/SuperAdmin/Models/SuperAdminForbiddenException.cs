namespace PointOfSale.API.SuperAdmin.Models;

public sealed class SuperAdminForbiddenException : Exception
{
    public SuperAdminForbiddenException(string message)
        : base(message)
    {
    }
}
