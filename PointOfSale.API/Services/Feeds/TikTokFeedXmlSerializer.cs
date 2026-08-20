using System.Xml;
using PointOfSale.API.DTOs.Feeds;

namespace PointOfSale.API.Services.Feeds;

/// <summary>
/// FEED-3: Serializes canonical <see cref="FeedProduct"/> items into the TikTok
/// Catalog RSS 2.0 product feed format.
///
/// OFFICIAL TIKTOK REQUIREMENTS (TikTok Ads Manager — Catalog Product Parameters,
/// last updated February 2025, https://ads.tiktok.com/help/article/catalog-product-parameters):
///
///   9 Required fields:
///     sku_id        - unique item identifier (a SKU or any stable unique ID)
///     title         - product name (no promotional text)
///     description   - short plain-text description
///     availability  - enum: in stock | available for order | preorder | out of stock | discontinued
///     condition     - enum: new | refurbished | used
///     price         - "9.99 USD" (amount + ISO 4217 currency)
///     link          - product landing-page URL
///     image_link    - absolute http(s) URL ending in .jpg/.png (≥500x500 recommended)
///     brand         - real brand/manufacturer name (not a placeholder)
///
///   Optional fields emitted when available:
///     item_group_id       - groups variants (TikTok variant contract)
///     additional_image_link - extra image URLs
///     sale_price          - discounted price when strictly below base price
///     gtin                - validated global trade item number
///     product_type        - free-form category from FeedProduct.CategoryName
///
///   NOT emitted (no authoritative Shopixy source — never invented):
///     google_product_category - Shopixy has no Google taxonomy mapping
///     mpn                     - merchant SKU is not a manufacturer part number
///
/// Feed format: RSS 2.0 XML with bare element names (no g: namespace prefix),
/// which TikTok accepts (XML / RSS / Atom). This reuses the certified Feed-1A/1B
/// FeedXml writer settings (UTF-8 MemoryStream, XmlWriter escaping, deterministic
/// ordering) — only the element vocabulary differs from Meta/Google.
///
/// sku_id mapping (STEP 7):
///   - Primary source: FeedProduct.Sku (product.Sku for simple, variant.Sku for variants).
///   - Fallback: FeedItemId.ToString("D") when Sku is null — TikTok describes sku_id
///     as "a unique ID for the item, such as a SKU"; the FeedItemId is the canonical
///     Feed-0 product/variant GUID already used as g:id by Meta and Google.
/// </summary>
public interface ITikTokFeedXmlSerializer
{
    /// <summary>
    /// Produces a complete RSS document. Optional elements are omitted entirely
    /// when absent/invalid — no empty elements are ever emitted.
    /// </summary>
    string Serialize(string tenantName, string channelLink, IReadOnlyList<FeedProduct> items);
}

/// <summary>
/// FEED-3: TikTok channel adapter over the SAME certified FeedProduct truth.
/// Shares the Feed-1A XML infrastructure (<see cref="FeedXml"/>): XmlWriter
/// escaping (no manual concatenation), UTF-8 MemoryStream encoding, and the
/// same omission discipline. Only the attribute vocabulary differs from
/// Meta/Google: TikTok uses bare element names (no g: namespace) and
/// <c>sku_id</c> instead of <c>g:id</c>.
/// </summary>
public sealed class TikTokFeedXmlSerializer : ITikTokFeedXmlSerializer
{
    public string Serialize(string tenantName, string channelLink, IReadOnlyList<FeedProduct> items)
    {
        var settings = FeedXml.CreateUtf8RssWriterSettings();

        using (var stream = new MemoryStream())
        {
            using (var writer = XmlWriter.Create(stream, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("rss");
                writer.WriteAttributeString("version", "2.0");

                writer.WriteStartElement("channel");
                FeedXml.WriteElement(writer, "title", tenantName);
                FeedXml.WriteElement(writer, "link", channelLink);
                FeedXml.WriteElement(writer, "description", $"Products feed for {tenantName} - TikTok");

                foreach (var item in items)
                {
                    WriteItem(writer, item);
                }

                writer.WriteEndElement(); // channel
                writer.WriteEndElement(); // rss
            }

            return settings.Encoding.GetString(stream.ToArray());
        }
    }

    private static void WriteItem(XmlWriter writer, FeedProduct item)
    {
        writer.WriteStartElement("item");

        FeedXml.WriteElement(writer, "title", item.Title);
        FeedXml.WriteElement(writer, "link", item.ProductUrl);

        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            FeedXml.WriteElement(writer, "description", item.Description);
        }

        // TikTok requires sku_id — use the merchant SKU when available;
        // fall back to the canonical FeedItemId GUID which TikTok describes
        // as an acceptable "unique ID for the item, such as a SKU."
        var skuId = !string.IsNullOrWhiteSpace(item.Sku) ? item.Sku : item.FeedItemId.ToString("D");
        FeedXml.WriteElement(writer, "sku_id", skuId);

        // TikTok uses item_group_id to group variants (same Feed-0 contract
        // as Meta/Google: ItemGroupId = ProductId for variant items).
        if (item.ItemGroupId.HasValue && item.ItemGroupId.Value != Guid.Empty)
        {
            FeedXml.WriteElement(writer, "item_group_id", item.ItemGroupId.Value.ToString("D"));
        }

        if (FeedXml.IsValidAbsoluteHttpUrl(item.PrimaryImageUrl))
        {
            FeedXml.WriteElement(writer, "image_link", item.PrimaryImageUrl);
        }

        foreach (var additionalUrl in item.AdditionalImageUrls)
        {
            if (FeedXml.IsValidAbsoluteHttpUrl(additionalUrl))
            {
                FeedXml.WriteElement(writer, "additional_image_link", additionalUrl);
            }
        }

        FeedXml.WriteElement(writer, "price", FeedXml.FormatPrice(item.Price, item.Currency));
        if (item.SalePrice.HasValue && item.SalePrice.Value > 0m && item.SalePrice.Value < item.Price)
        {
            FeedXml.WriteElement(writer, "sale_price", FeedXml.FormatPrice(item.SalePrice.Value, item.Currency));
        }

        FeedXml.WriteElement(writer, "availability", item.Availability);
        FeedXml.WriteElement(writer, "condition", item.Condition);

        if (!string.IsNullOrWhiteSpace(item.Brand))
        {
            FeedXml.WriteElement(writer, "brand", item.Brand);
        }

        if (!string.IsNullOrWhiteSpace(item.CategoryName))
        {
            FeedXml.WriteElement(writer, "product_type", item.CategoryName);
        }

        if (!string.IsNullOrWhiteSpace(item.Gtin))
        {
            FeedXml.WriteElement(writer, "gtin", item.Gtin);
        }

        // NOTE: gt:mpn is intentionally never emitted. TikTok supports it as an
        // optional field, but Shopixy SKUs are merchant-assigned and must not be
        // interpreted as manufacturer part numbers (FEED-2/FEED-3 parity with
        // the Google adapter's explicit discipline).
        // NOTE: google_product_category is intentionally never emitted. Shopixy
        // has no Google product taxonomy mapping; emitting invented values would
        // violate TikTok's accuracy requirement.

        writer.WriteEndElement(); // item
    }
}
