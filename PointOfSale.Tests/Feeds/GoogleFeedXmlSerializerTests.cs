using System.Text;
using System.Xml.Linq;
using PointOfSale.API.DTOs.Feeds;
using PointOfSale.API.Services.Feeds;
using Xunit;

namespace PointOfSale.Tests.Feeds;

/// <summary>
/// FEED-2: Google Merchant Center RSS serializer contract — RSS 2.0 shape,
/// g: namespace, Google attribute vocabulary (no g:sku / g:mpn / no invented
/// google_product_category), escaping, UTF-8 (incl. Arabic), optional-node
/// omission, variant identity, deterministic order.
/// </summary>
public sealed class GoogleFeedXmlSerializerTests
{
    private static readonly XNamespace G = "http://base.google.com/ns/1.0";

    private readonly GoogleFeedXmlSerializer _serializer = new();

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
    // RSS 2.0 + g: namespace shape
    // =========================================================================

    [Fact]
    public void Serialize_ProducesValidRss20_WithGoogleNamespace()
    {
        var xml = _serializer.Serialize("My Store", "https://store.example.com", new[] { ValidItem() });

        var doc = XDocument.Parse(xml);
        Assert.Equal("rss", doc.Root!.Name.LocalName);
        Assert.Equal("2.0", (string?)doc.Root.Attribute("version"));
        Assert.Equal(G.NamespaceName, doc.Root.GetNamespaceOfPrefix("g")?.NamespaceName);

        var channel = doc.Root.Element("channel");
        Assert.NotNull(channel);
        Assert.Equal("My Store", (string?)channel!.Element("title"));
        Assert.Equal("https://store.example.com", (string?)channel.Element("link"));
        // Channel-distinct description — never identical to the Meta feed's.
        Assert.Equal("Products feed for My Store - Google", (string?)channel.Element("description"));
    }

    [Fact]
    public void Serialize_EmitsAllGoogleItemAttributes_WithGPrefix()
    {
        var item = ValidItem();
        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { item });
        var node = XDocument.Parse(xml).Root!.Element("channel")!.Element("item")!;

        Assert.Equal(item.Title, (string?)node.Element("title"));
        Assert.Equal(item.ProductUrl, (string?)node.Element("link"));
        Assert.Equal(item.Description, (string?)node.Element("description"));
        Assert.Equal(item.FeedItemId.ToString("D"), (string?)node.Element(G + "id"));
        Assert.Equal(item.PrimaryImageUrl, (string?)node.Element(G + "image_link"));
        Assert.Equal("120.00 USD", (string?)node.Element(G + "price"));
        Assert.Equal("in_stock", (string?)node.Element(G + "availability"));
        Assert.Equal("new", (string?)node.Element(G + "condition"));
        Assert.Equal("Acme", (string?)node.Element(G + "brand"));
        Assert.Equal("Shoes", (string?)node.Element(G + "product_type"));
        Assert.Equal("1234567890123", (string?)node.Element(G + "gtin"));
        Assert.Null(node.Element(G + "identifier_exists"));
        Assert.Single(node.Elements(G + "additional_image_link"));
        Assert.Null(node.Element(G + "item_group_id"));
    }

    // =========================================================================
    // Google-specific vocabulary discipline
    // =========================================================================

    [Fact]
    public void Serialize_NeverEmitsSkuOrMpn_SkuIsMerchantAssignedNotManufacturerPartNumber()
    {
        var item = ValidItem();
        item.Sku = "MERCHANT-SKU-42";

        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { item });
        var node = XDocument.Parse(xml).Root!.Element("channel")!.Element("item")!;

        // Google's spec has no g:sku; g:mpn must never be invented from a
        // merchant-assigned SKU.
        Assert.Null(node.Element(G + "sku"));
        Assert.Null(node.Element(G + "mpn"));
        Assert.DoesNotContain("MERCHANT-SKU-42", xml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FeedAvailability.InStock, "in_stock")]
    [InlineData(FeedAvailability.OutOfStock, "out_of_stock")]
    [InlineData(FeedAvailability.AvailableForOrder, "backorder")]
    public void Serialize_MapsCanonicalAvailabilityToGoogleValues(string canonical, string expected)
    {
        var item = ValidItem();
        item.Availability = canonical;

        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { item });
        var node = XDocument.Parse(xml).Root!.Element("channel")!.Element("item")!;

        Assert.Equal(expected, (string?)node.Element(G + "availability"));
        Assert.DoesNotContain("available for order", xml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Serialize_GoogleIdentifiersUnavailable_UsesIdentifierExistsNo_InsteadOfInventedMpn()
    {
        var item = ValidItem();
        item.Gtin = null;
        item.Sku = "MERCHANT-SKU-42";

        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { item });
        var node = XDocument.Parse(xml).Root!.Element("channel")!.Element("item")!;

        Assert.Equal("no", (string?)node.Element(G + "identifier_exists"));
        Assert.Null(node.Element(G + "mpn"));
        Assert.DoesNotContain("MERCHANT-SKU-42", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_NeverEmitsGoogleProductCategory_NoTaxonomyMappingInvented()
    {
        var item = ValidItem();
        item.CategoryName = "Shoes > Sneakers";

        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { item });
        var node = XDocument.Parse(xml).Root!.Element("channel")!.Element("item")!;

        Assert.Null(node.Element(G + "google_product_category"));
        // Shopixy category maps only to g:product_type (free-form).
        Assert.Equal("Shoes > Sneakers", (string?)node.Element(G + "product_type"));
    }

    // =========================================================================
    // Variants: g:id = VariantId, g:item_group_id = ProductId
    // =========================================================================

    [Fact]
    public void Serialize_VariantItem_EmitsItemGroupIdAndVariantId()
    {
        var productId = Guid.NewGuid();
        var item = ValidItem();
        item.VariantId = Guid.NewGuid();
        item.FeedItemId = item.VariantId.Value;
        item.ItemGroupId = productId;

        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { item });
        var node = XDocument.Parse(xml).Root!.Element("channel")!.Element("item")!;

        Assert.Equal(item.VariantId.ToString(), (string?)node.Element(G + "id"));
        Assert.Equal(productId.ToString("D"), (string?)node.Element(G + "item_group_id"));
    }

    [Fact]
    public void Serialize_VariantGroup_SharesItemGroupId_AcrossSiblingVariants()
    {
        var productId = Guid.NewGuid();
        var variantA = ValidItem(title: "Sneakers - Red");
        variantA.VariantId = Guid.NewGuid();
        variantA.FeedItemId = variantA.VariantId.Value;
        variantA.ItemGroupId = productId;
        var variantB = ValidItem(title: "Sneakers - Blue");
        variantB.VariantId = Guid.NewGuid();
        variantB.FeedItemId = variantB.VariantId.Value;
        variantB.ItemGroupId = productId;

        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { variantA, variantB });
        var nodes = XDocument.Parse(xml).Root!.Element("channel")!.Elements("item").ToList();

        Assert.Equal(2, nodes.Count);
        Assert.All(nodes, node => Assert.Equal(productId.ToString("D"), (string?)node.Element(G + "item_group_id")));
        Assert.Equal(variantA.VariantId.ToString(), (string?)nodes[0].Element(G + "id"));
        Assert.Equal(variantB.VariantId.ToString(), (string?)nodes[1].Element(G + "id"));
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

        Assert.Null(node.Element(G + "brand"));
        Assert.Null(node.Element(G + "gtin"));
        Assert.Null(node.Element(G + "product_type"));
        Assert.Null(node.Element(G + "sale_price"));
        Assert.Null(node.Element("description"));
        Assert.Empty(node.Elements(G + "additional_image_link"));
        // Required attributes always survive omission of optionals.
        Assert.Equal("120.00 USD", (string?)node.Element(G + "price"));
        Assert.Equal("in_stock", (string?)node.Element(G + "availability"));
        Assert.Equal("new", (string?)node.Element(G + "condition"));
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

        Assert.Equal("99.50 USD", (string?)nodes[0].Element(G + "sale_price"));
        Assert.Null(nodes[1].Element(G + "sale_price"));
        Assert.Null(nodes[2].Element(G + "sale_price"));
    }

    [Fact]
    public void Serialize_InvalidImageUrls_AreOmitted_NotEmittedBroken()
    {
        var item = ValidItem();
        item.PrimaryImageUrl = "not-a-url";
        item.AdditionalImageUrls = new[] { "javascript:alert(1)", "ftp://cdn.example.com/x.jpg", "https://cdn.example.com/ok.jpg" };

        var xml = _serializer.Serialize("Store", "https://store.example.com", new[] { item });
        var node = XDocument.Parse(xml).Root!.Element("channel")!.Element("item")!;

        Assert.Null(node.Element(G + "image_link"));
        var additional = node.Elements(G + "additional_image_link").Select(e => (string?)e).ToList();
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
        Assert.Equal("Empty Store", (string?)channel!.Element("title"));
        Assert.Equal("https://store.example.com", (string?)channel.Element("link"));
        Assert.Equal("Products feed for Empty Store - Google", (string?)channel.Element("description"));
        Assert.Empty(channel!.Elements("item"));
    }

    [Fact]
    public void Serialize_PreservesDeterministicItemOrder()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => ValidItem()).ToList();

        var xml = _serializer.Serialize("Store", "https://store.example.com", ids);
        var emitted = XDocument.Parse(xml).Root!.Element("channel")!.Elements("item")
            .Select(node => Guid.Parse((string?)node.Element(G + "id")!))
            .ToList();

        Assert.Equal(ids.Select(item => item.FeedItemId), emitted);
    }
}
