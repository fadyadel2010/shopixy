using System.Text;
using System.Xml.Linq;
using PointOfSale.API.DTOs.Feeds;
using PointOfSale.API.Services.Feeds;
using Xunit;

namespace PointOfSale.Tests.Feeds;

/// <summary>
/// FEED-3: TikTok Catalog RSS serializer contract — RSS 2.0 shape (no g:
/// namespace), TikTok field vocabulary (sku_id, not id), escaping, UTF-8
/// (incl. Arabic), optional-node omission, variant grouping via item_group_id,
/// deterministic order, and the negative identifiers (no SKU-as-MPN, no
/// invented google_product_category).
///
/// OFFICIAL TIKTOK REQUIREMENTS:
///   Catalog Product Parameters — TikTok Ads Manager (last updated February 2025).
///   Required: sku_id, title, description, price, link, image_link, availability,
///   condition, brand. Optional: item_group_id, additional_image_link, sale_price,
///   gtin, product_type.
/// </summary>
public sealed class TikTokFeedXmlSerializerTests
{
    private readonly TikTokFeedXmlSerializer _serializer = new();

    /// <summary>
    /// A complete TikTok item matching all official required + key optional fields.
    /// SKU is populated so sku_id is the merchant SKU (not the FeedItemId fallback).
    /// </summary>
    private static FeedProduct ValidItem(string title = "Blue Sneakers") => new()
    {
        FeedItemId = Guid.NewGuid(),
        ProductId = Guid.NewGuid(),
        Title = title,
        Description = "Comfortable sneakers",
        ProductUrl = "https://store.example.com/products/blue-sneakers",
        PrimaryImageUrl = "https://cdn.example.com/img/1.jpg",
        AdditionalImageUrls = new[] { "https://cdn.example.com/img/2.jpg" },
        Price = 120m,
        Currency = "USD",
        Availability = FeedAvailability.InStock,
        Condition = "new",
        Brand = "Acme",
        Sku = "SKU-1",
        Gtin = "1234567890123",
        CategoryName = "Shoes"
    };

    // =========================================================================
    // RSS 2.0 + TikTok vocabulary (no g: namespace)
    // =========================================================================

    [Fact]
    public void Serialize_ProducesValidRss20_WithoutGoogleNamespace()
    {
        var xml = _serializer.Serialize("My Store", "https://store.example.com", new[] { ValidItem() });

        var doc = XDocument.Parse(xml);
        Assert.Equal("rss", doc.Root!.Name.LocalName);
        Assert.Equal("2.0", (string?)doc.Root.Attribute("version"));
        // TikTok uses bare element names — no g: namespace declaration.
        Assert.Null(doc.Root.GetNamespaceOfPrefix("g")?.NamespaceName);

        var channel = doc.Root.Element("channel");
        Assert.NotNull(channel);
        Assert.Equal("My Store", (string?)channel!.Element("title"));
        Assert.Equal("https://store.example.com", (string?)channel.Element("link"));
        Assert.Equal("Products feed for My Store - TikTok", (string?)channel.Element("description"));
    }

    [Fact]
    public void Serialize_EmitsAllRequiredTikTokItemFields_WithBareNames()
    {
        var item = ValidItem();
        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { item });
        var node = XDocument.Parse(xml).Root!.Element("channel")!.Element("item")!;

        Assert.Equal(item.Title, (string?)node.Element("title"));
        Assert.Equal(item.ProductUrl, (string?)node.Element("link"));
        Assert.Equal(item.Description, (string?)node.Element("description"));
        Assert.Equal("SKU-1", (string?)node.Element("sku_id"));
        Assert.Equal(item.PrimaryImageUrl, (string?)node.Element("image_link"));
        Assert.Equal("120.00 USD", (string?)node.Element("price"));
        Assert.Equal("in stock", (string?)node.Element("availability"));
        Assert.Equal("new", (string?)node.Element("condition"));
        Assert.Equal("Acme", (string?)node.Element("brand"));
        Assert.Single(node.Elements("additional_image_link"));
        Assert.Null(node.Element("item_group_id"));
    }

    // =========================================================================
    // sku_id mapping (STEP 7)
    // =========================================================================

    [Fact]
    public void Serialize_UsesSkuAsSkuId_WhenSkuPresent()
    {
        var item = ValidItem();
        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { item });
        var node = XDocument.Parse(xml).Root!.Element("channel")!.Element("item")!;

        Assert.Equal("SKU-1", (string?)node.Element("sku_id"));
    }

    [Fact]
    public void Serialize_FallsBackToFeedItemId_WhenSkuNull_TikTokAcceptsUniqueIds()
    {
        var item = ValidItem();
        item.Sku = null;

        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { item });
        var node = XDocument.Parse(xml).Root!.Element("channel")!.Element("item")!;

        Assert.Equal(item.FeedItemId.ToString("D"), (string?)node.Element("sku_id"));
    }

    [Fact]
    public void Serialize_NeverEmitsSkuAsMpn_NoInventedMpn()
    {
        var item = ValidItem();
        item.Sku = "MERCHANT-SKU-42";

        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { item });
        var node = XDocument.Parse(xml).Root!.Element("channel")!.Element("item")!;

        Assert.Null(node.Element("mpn"));
        Assert.DoesNotContain("mpn", xml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_NeverEmitsGoogleProductCategory_NoTaxonomyMappingInvented()
    {
        var item = ValidItem();
        item.CategoryName = "Shoes > Sneakers";

        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { item });
        var node = XDocument.Parse(xml).Root!.Element("channel")!.Element("item")!;

        Assert.Null(node.Element("google_product_category"));
        // Shopixy category maps only to product_type (free-form).
        Assert.Equal("Shoes > Sneakers", (string?)node.Element("product_type"));
    }

    // =========================================================================
    // Variants: item_group_id groups siblings, sku_id = variant identity
    // =========================================================================

    [Fact]
    public void Serialize_VariantItem_EmitsItemGroupIdAndSkuId()
    {
        var productId = Guid.NewGuid();
        var item = ValidItem();
        item.VariantId = Guid.NewGuid();
        item.FeedItemId = item.VariantId.Value;
        item.ItemGroupId = productId;

        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { item });
        var node = XDocument.Parse(xml).Root!.Element("channel")!.Element("item")!;

        Assert.Equal(productId.ToString("D"), (string?)node.Element("item_group_id"));
    }

    [Fact]
    public void Serialize_VariantGroup_SharesItemGroupId_AcrossSiblingVariants()
    {
        var productId = Guid.NewGuid();
        var variantA = ValidItem(title: "Sneakers - Red");
        variantA.VariantId = Guid.NewGuid();
        variantA.FeedItemId = variantA.VariantId.Value;
        variantA.ItemGroupId = productId;
        variantA.Sku = "SKU-RED";
        var variantB = ValidItem(title: "Sneakers - Blue");
        variantB.VariantId = Guid.NewGuid();
        variantB.FeedItemId = variantB.VariantId.Value;
        variantB.ItemGroupId = productId;
        variantB.Sku = "SKU-BLUE";

        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { variantA, variantB });
        var nodes = XDocument.Parse(xml).Root!.Element("channel")!.Elements("item").ToList();

        Assert.Equal(2, nodes.Count);
        Assert.All(nodes, node => Assert.Equal(productId.ToString("D"), (string?)node.Element("item_group_id")));
        Assert.Equal("SKU-RED", (string?)nodes[0].Element("sku_id"));
        Assert.Equal("SKU-BLUE", (string?)nodes[1].Element("sku_id"));
    }

    // =========================================================================
    // Escaping + UTF-8 (Arabic)
    // =========================================================================

    [Fact]
    public void Serialize_EscapesSpecialCharacters_RoundTrip()
    {
        var item = ValidItem(title: "A & B < C > D \"quoted\" 'single' — عربية");
        item.Description = "وصف عربي & <tag> text";

        var xml = _serializer.Serialize("متجر & Co", "https://store.example.com", new[] { item });
        var doc = XDocument.Parse(xml); // Throws if escaping is broken.

        var channel = doc.Root!.Element("channel")!;
        Assert.Equal("متجر & Co", (string?)channel.Element("title"));
        var node = channel.Element("item")!;
        Assert.Equal(item.Title, (string?)node.Element("title"));
        Assert.Equal(item.Description, (string?)node.Element("description"));
    }

    [Fact]
    public void Serialize_DeclaresUtf8_AndDeclarationMatchesActualBytes()
    {
        var item = ValidItem(title: "حذاء & <رياضي>");
        item.Description = "وصف عربي";

        var xml = _serializer.Serialize("متجر", "https://store.example.com", new[] { item });

        // 1. Declaration must say utf-8 (matching the HTTP Content-Type contract).
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml);

        // 2. Byte-level agreement: encode exactly as the HTTP layer does
        //    (UTF-8, no BOM) and parse from raw bytes — a utf-16 declaration
        //    over UTF-8 bytes fails to load, proving declaration == bytes.
        var bytes = new UTF8Encoding(false).GetBytes(xml);
        Assert.False(bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE, "No UTF-16 LE BOM expected.");
        Assert.False(bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF, "No UTF-16 BE BOM expected.");

        using var stream = new MemoryStream(bytes);
        var doc = XDocument.Load(stream);

        // 3. Arabic content survives the UTF-8 byte round-trip; structure intact.
        var channel = doc.Root!.Element("channel")!;
        Assert.Equal("متجر", (string?)channel.Element("title"));
        var node = channel.Element("item")!;
        Assert.Equal(item.Title, (string?)node.Element("title"));
        Assert.Equal(item.Description, (string?)node.Element("description"));
        Assert.Equal("rss", doc.Root.Name.LocalName);
        Assert.Equal("2.0", (string?)doc.Root.Attribute("version"));
    }

    // =========================================================================
    // Conditional fields / omission discipline
    // =========================================================================

    [Fact]
    public void Serialize_OmitsOptionalNodes_WhenAbsentOrInvalid()
    {
        var item = ValidItem();
        item.Brand = null;
        item.Sku = null;
        item.Gtin = null;
        item.CategoryName = null;
        item.SalePrice = null;
        item.Description = string.Empty;
        item.AdditionalImageUrls = Array.Empty<string>();

        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { item });
        var node = XDocument.Parse(xml).Root!.Element("channel")!.Element("item")!;

        Assert.Null(node.Element("brand"));
        Assert.Null(node.Element("gtin"));
        Assert.Null(node.Element("product_type"));
        Assert.Null(node.Element("sale_price"));
        Assert.Null(node.Element("description"));
        Assert.Empty(node.Elements("additional_image_link"));
        // Required attributes always survive omission of optionals.
        Assert.Equal(item.FeedItemId.ToString("D"), (string?)node.Element("sku_id")); // fallback to FeedItemId
        Assert.Equal("120.00 USD", (string?)node.Element("price"));
        Assert.Equal("in stock", (string?)node.Element("availability"));
        Assert.Equal("new", (string?)node.Element("condition"));
    }

    [Fact]
    public void Serialize_SalePrice_EmittedOnlyWhenStrictlyLowerThanBase()
    {
        var withSale = ValidItem();
        withSale.SalePrice = 99.50m;
        var equalSale = ValidItem();
        equalSale.SalePrice = equalSale.Price;
        var zeroSale = ValidItem();
        zeroSale.SalePrice = 0m;

        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { withSale, equalSale, zeroSale });
        var nodes = XDocument.Parse(xml).Root!.Element("channel")!.Elements("item").ToList();

        Assert.Equal("99.50 USD", (string?)nodes[0].Element("sale_price"));
        Assert.Null(nodes[1].Element("sale_price"));
        Assert.Null(nodes[2].Element("sale_price"));
    }

    [Fact]
    public void Serialize_InvalidImageUrls_AreOmitted_NotEmittedBroken()
    {
        var item = ValidItem();
        item.PrimaryImageUrl = "not-a-url";
        item.AdditionalImageUrls = new[] { "javascript:alert(1)", "ftp://cdn.example.com/x.jpg", "https://cdn.example.com/ok.jpg" };

        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { item });
        var node = XDocument.Parse(xml).Root!.Element("channel")!.Element("item")!;

        Assert.Null(node.Element("image_link"));
        var additional = node.Elements("additional_image_link").Select(e => (string?)e).ToList();
        Assert.Single(additional);
        Assert.Equal("https://cdn.example.com/ok.jpg", additional[0]);
    }

    // =========================================================================
    // Empty catalog + deterministic order
    // =========================================================================

    [Fact]
    public void Serialize_EmptyCatalog_ProducesValidRssWithZeroItems()
    {
        var xml = _serializer.Serialize("Empty Store", "https://store.example.com", Array.Empty<FeedProduct>());

        var doc = XDocument.Parse(xml);
        var channel = doc.Root!.Element("channel");
        Assert.NotNull(channel);
        Assert.Empty(channel!.Elements("item"));
    }

    [Fact]
    public void Serialize_PreservesDeterministicItemOrder()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => ValidItem()).ToList();
        // Clear SKUs so sku_id falls back to FeedItemId (a stable Guid).
        foreach (var item in ids)
        {
            item.Sku = null;
        }

        var xml = _serializer.Serialize("Store", "https://store.example.com", ids);
        var emitted = XDocument.Parse(xml).Root!.Element("channel")!.Elements("item")
            .Select(node => Guid.Parse((string?)node.Element("sku_id")!))
            .ToList();

        Assert.Equal(ids.Select(item => item.FeedItemId), emitted);
    }

    // =========================================================================
    // Gtin: serializer emits the canonical FeedProduct.Gtin as-is; validation
    // (only 8/12/13/14 digit barcodes) is enforced by the shared projection's
    // TryBuildGtin (see MetaProductFeedQueryTests), not by the serializer.
    // =========================================================================

    [Theory]
    [InlineData("1234567890123", true)]
    [InlineData("12345678", true)]
    [InlineData("12345678901234", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void Gtin_EmittedFromCanonicalFeedProduct_WhenPresent(string? gtin, bool expected)
    {
        var item = ValidItem();
        item.Gtin = gtin;

        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { item });

        var node = XDocument.Parse(xml).Root!.Element("channel")!.Element("item")!;
        var gtinElement = node.Element("gtin");
        if (expected)
        {
            Assert.Equal(gtin, (string?)gtinElement);
        }
        else
        {
            Assert.Null(gtinElement);
        }
    }
}
