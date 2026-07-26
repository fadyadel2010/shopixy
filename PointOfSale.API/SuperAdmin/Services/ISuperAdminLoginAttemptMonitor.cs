using PointOfSale.API.SuperAdmin.Models;

namespace PointOfSale.API.SuperAdmin.Services;

public interface ISuperAdminLoginAttemptMonitor
{
    SuperAdminLoginAttemptResult RecordFailure(string ipAddress, string email);
    void ClearFailures(string ipAddress);
}
