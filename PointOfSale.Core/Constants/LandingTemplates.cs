namespace PointOfSale.Core.Constants;

public static class LandingTemplates
{
    public const string ClassicV1 = "Classic:v1";
    public const string StorytellingV1 = "Storytelling:v1";
    public const string MinimalFastCheckoutV1 = "MinimalFastCheckout:v1";

    public static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
    {
        ClassicV1,
        StorytellingV1,
        MinimalFastCheckoutV1
    };
}
