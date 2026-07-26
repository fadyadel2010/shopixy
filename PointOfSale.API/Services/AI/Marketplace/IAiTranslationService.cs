namespace PointOfSale.API.Services.AI.Marketplace;

public interface IAiTranslationService
{
    Task<string> TranslateAsync(
        string input,
        string from,
        string to,
        CancellationToken cancellationToken = default);
}
