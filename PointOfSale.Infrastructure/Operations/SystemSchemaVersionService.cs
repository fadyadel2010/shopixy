using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PointOfSale.Core.Entities;
using PointOfSale.Infrastructure.Data;

namespace PointOfSale.Infrastructure.Operations;

public interface ISystemSchemaVersionService
{
    Task RecordAsync(
        string component,
        string version,
        object? details = null,
        CancellationToken cancellationToken = default);
}

public class SystemSchemaVersionService : ISystemSchemaVersionService
{
    private readonly ApplicationDbContext _dbContext;

    public SystemSchemaVersionService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task RecordAsync(
        string component,
        string version,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(component) || string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        var normalizedComponent = component.Trim();
        var normalizedVersion = version.Trim();
        var detailsJson = details == null ? null : JsonSerializer.Serialize(details);

        var existing = await _dbContext.Set<SystemSchemaVersion>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Component == normalizedComponent, cancellationToken);

        if (existing == null)
        {
            await _dbContext.Set<SystemSchemaVersion>().AddAsync(
                new SystemSchemaVersion
                {
                    Id = Guid.NewGuid(),
                    Component = normalizedComponent,
                    Version = normalizedVersion,
                    AppliedAt = DateTime.UtcNow,
                    DetailsJson = detailsJson,
                    CreatedAt = DateTime.UtcNow
                },
                cancellationToken);
        }
        else
        {
            existing.Version = normalizedVersion;
            existing.AppliedAt = DateTime.UtcNow;
            existing.DetailsJson = detailsJson;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.IsDeleted = false;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
