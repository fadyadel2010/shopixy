namespace PointOfSale.Core.Interfaces;

/// <summary>
/// Provides access to the current correlation id for traceable backend flows.
/// </summary>
public interface ICorrelationIdAccessor
{
    string GetCorrelationId();
}
