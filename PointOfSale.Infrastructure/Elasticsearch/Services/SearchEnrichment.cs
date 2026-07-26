using System.Text;

namespace PointOfSale.Infrastructure.Elasticsearch.Services;

/// <summary>
/// Search enrichment service for deterministic multilingual search.
/// 
/// ARCHITECTURE PRINCIPLE:
/// Search engines do NOT invent language knowledge - they only match what you index.
/// Therefore, we enrich searchable text at INDEX TIME, not query time.
/// 
/// This approach:
/// ✅ Is deterministic (no AI, no runtime translation)
/// ✅ Is scalable (dictionary lookup is O(1))
/// ✅ Requires no external services
/// ✅ Works offline
/// ✅ Is testable and predictable
/// 
/// HOW IT WORKS:
/// When indexing "iPhone 16 Pro Max", SearchText becomes:
/// "iPhone 16 Pro Max iphone ايفون apple ابل"
/// 
/// Now user can search:
/// - "iphone" ✅
/// - "ايفون" ✅
/// - "iphnoe" ✅ (fuzziness handles typos)
/// </summary>
public static class SearchEnrichment
{
    /// <summary>
    /// Brand and product keywords with their cross-language equivalents.
    /// Key: English term (lowercase for matching)
    /// Value: All searchable variants (English + Arabic)
    /// </summary>
    private static readonly Dictionary<string, string[]> BrandKeywords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Apple ecosystem
            ["iphone"] = new[] { "iphone", "ايفون", "آيفون" },
            ["apple"] = new[] { "apple", "ابل", "آبل" },
            ["ipad"] = new[] { "ipad", "ايباد", "آيباد" },
            ["macbook"] = new[] { "macbook", "ماك بوك", "ماكبوك" },
            ["airpods"] = new[] { "airpods", "ايربودز", "اير بودز" },
            ["watch"] = new[] { "watch", "ساعة", "واتش" },
            
            // Samsung
            ["samsung"] = new[] { "samsung", "سامسونج", "سامسونغ" },
            ["galaxy"] = new[] { "galaxy", "جالاكسي", "جالكسي" },
            
            // Chinese brands
            ["xiaomi"] = new[] { "xiaomi", "شاومي" },
            ["huawei"] = new[] { "huawei", "هواوي" },
            ["oppo"] = new[] { "oppo", "اوبو" },
            ["vivo"] = new[] { "vivo", "فيفو" },
            ["realme"] = new[] { "realme", "ريلمي" },
            ["oneplus"] = new[] { "oneplus", "ون بلس", "ونبلس" },
            ["honor"] = new[] { "honor", "هونر" },
            
            // Other brands
            ["google"] = new[] { "google", "جوجل", "غوغل" },
            ["pixel"] = new[] { "pixel", "بيكسل" },
            ["sony"] = new[] { "sony", "سوني" },
            ["lg"] = new[] { "lg", "ال جي" },
            ["nokia"] = new[] { "nokia", "نوكيا" },
            ["motorola"] = new[] { "motorola", "موتورولا" },
            
            // Product categories
            ["phone"] = new[] { "phone", "موبايل", "جوال", "هاتف", "تليفون" },
            ["mobile"] = new[] { "mobile", "موبايل", "جوال" },
            ["laptop"] = new[] { "laptop", "لابتوب", "حاسوب محمول" },
            ["tablet"] = new[] { "tablet", "تابلت", "لوحي" },
            ["headphones"] = new[] { "headphones", "سماعات", "هيدفون" },
            ["earbuds"] = new[] { "earbuds", "سماعات", "اير بودز" },
            ["charger"] = new[] { "charger", "شاحن" },
            ["case"] = new[] { "case", "كفر", "جراب" },
            ["screen"] = new[] { "screen", "شاشة" },
            ["protector"] = new[] { "protector", "حماية" },
            
            // Common specs
            ["pro"] = new[] { "pro", "برو" },
            ["max"] = new[] { "max", "ماكس" },
            ["plus"] = new[] { "plus", "بلس" },
            ["ultra"] = new[] { "ultra", "الترا" },
            ["lite"] = new[] { "lite", "لايت" },
            ["mini"] = new[] { "mini", "ميني" }
        };

    /// <summary>
    /// Expands input text with cross-language keywords found in the brand dictionary.
    /// </summary>
    /// <param name="text">Input text (usually product name)</param>
    /// <returns>Set of all searchable terms including original text</returns>
    public static IEnumerable<string> Expand(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Enumerable.Empty<string>();

        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            text // Always include original
        };

        // Split into words for matching
        var words = text.Split(new[] { ' ', '-', '_', '/', '\\', '(', ')', '[', ']' }, 
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            // Check if this word matches any brand keyword
            if (BrandKeywords.TryGetValue(word, out var expansions))
            {
                foreach (var expansion in expansions)
                {
                    results.Add(expansion);
                }
            }
        }

        return results;
    }

    /// <summary>
    /// Builds a comprehensive SearchText field for Elasticsearch indexing.
    /// This is THE power field for all search operations.
    /// 
    /// Result example for "iPhone 16 Pro Max":
    /// "iPhone 16 Pro Max iphone ايفون آيفون pro برو max ماكس"
    /// </summary>
    public static string BuildSearchText(
        string name,
        string? nameAr = null,
        string? description = null,
        string? categoryName = null,
        string? sku = null,
        string? barcode = null,
        string? brand = null)
    {
        var tokens = new List<string>();

        // Expand the primary name with brand keywords
        tokens.AddRange(Expand(name));

        // Add Arabic name if provided (already in Arabic, no expansion needed)
        if (!string.IsNullOrWhiteSpace(nameAr))
        {
            tokens.Add(nameAr);
        }

        // Expand brand name with cross-language keywords
        if (!string.IsNullOrWhiteSpace(brand))
        {
            tokens.AddRange(Expand(brand));
        }

        // Add description (don't expand - too long, just add as-is)
        if (!string.IsNullOrWhiteSpace(description))
        {
            // Only take first 500 chars of description to avoid bloating
            var descSnippet = description.Length > 500 
                ? description.Substring(0, 500) 
                : description;
            tokens.Add(descSnippet);
        }

        // Add category (expand for brand matching)
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            tokens.AddRange(Expand(categoryName));
        }

        // Add SKU and barcode for exact match
        if (!string.IsNullOrWhiteSpace(sku))
        {
            tokens.Add(sku);
        }

        if (!string.IsNullOrWhiteSpace(barcode))
        {
            tokens.Add(barcode);
        }

        // Join all tokens, remove duplicates
        return string.Join(" ", tokens.Distinct());
    }
}
