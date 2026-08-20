using System.Xml;
using PointOfSale.API.DTOs.Feeds;

namespace PointOfSale.API.Services.Feeds;

/// <summary>
/// FEED-4: Serializes canonical <see cref="FeedProduct"/> items into the Snapchat
/// Catalog RSS 2.0 product feed format (xmlns:g = http://base.google.com/ns/1.0).
///
/// OFFICIAL SNAPCHAT REQUIREMENTS (Snapchat Business Help — Catalog Format,
/// https://businesshelp.snapchat.com/s/article/catalog-format):
///
///   Snapchat accepts product catalogs in CSV, TSV, or XML (RSS) format. The
///   XML catalog format uses the Google Merchant attribute namespace
///   (xmlns:g = http://base.google.com/ns/1.0), the same conventions as the
///   certified Meta and Google feed adapters.
///
///   Required fields:
///     id            - unique identifier (g:id = FeedItemId per Feed-0 identity contract)
///     title         - product name (max 150 chars, no promotional text)
///     description   - accurate description (max 5,000 chars)
///     link          - direct product landing-page URL
///     image_link    - absolute http(s) URL, min 500x500px recommended
///     price         - "12.00 USD" (amount + ISO 4217 currency)
///     availability  - enum: in stock | out of stock | preorder | discontinued |
///                              available for order
///     condition     - enum: new | refurbished | used
///     brand         - real brand/manufacturer name (or GTIN/MPN alternative)
///
///   Optional fields emitted when available:
///     item_group_id       - groups variants (Snapchat variant contract)
///     additional_image_link - extra image URLs
///     sale_price          - discounted price when strictly below base price
///     gtin                - validated global trade item number
///     product_type        - free-form category from FeedProduct.CategoryName
///
///   NOT emitted (no authoritative Shopixy source — never invented):
///     google_product_category - Shopixy has no Google taxonomy mapping
///     mpn                     - merchant SKU is not a manufacturer part number
///
///   AVAILABILITY VOCABULARY:
///   Snapchat uses space-separated values ("in stock", "out of stock") matching
///   the canonical FeedAvailability vocabulary exactly — no remapping needed.
///   The Feed-0 projection also emits "available for order" and the FeedProduct
///   model is already Snapchat-compatible.
///
///   This reuses the certified Feed-1A/1B FeedXml writer settings (UTF-8
///   MemoryStream, XmlWriter escaping, deterministic ordering) — only the
///   channel description string differs from the Google adapter.
///
/// g:id mapping (STEP 7/8):
///   - FeedItemId is the canonical Feed-0 product/variant GUID already used as
///     g:id by Meta and Google. Snapchat's id accepts any unique identifier;
///     the FeedItemId GUID is always authoritative and present.
///   - SKU is NOT reinterpreted as g:id or g:mpn: merchant SKUs are not
///     manufacturer part numbers, and the Feed-0 identity contract already
///     establishes FeedItemId as the catalog item identity.
/// </summary>
public interface ISnapchatFeedXmlSerializer
{
    /// <summary>
    /// Produces a complete RSS document. Optional g: nodes are omitted entirely
    /// when absent/invalid — no empty elements are ever emitted.
    /// </summary>
    string Serialize(string tenantName, string channelLink, IReadOnlyList<FeedProduct> items);
}

/// <summary>
/// FEED-4: Snapchat channel adapter over the SAME certified FeedProduct truth.
/// Shares the Feed-1A XML infrastructure (<see cref="FeedXml"/>): XmlWriter
/// escaping (no manual concatenation), UTF-8 MemoryStream encoding, and the
/// same omission discipline. Uses the Google Merchant attribute namespace
/// (g:) which Snapchat accepts for its XML catalog format.
/// </summary>
public sealed class SnapchatFeedXmlSerializer : ISnapchatFeedXmlSerializer
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
                writer.WriteAttributeString("xmlns", "g", null, FeedXml.GoogleNamespace);

                writer.WriteStartElement("channel");
                FeedXml.WriteElement(writer, "title", tenantName);
                FeedXml.WriteElement(writer, "link", channelLink);
                FeedXml.WriteElement(writer, "description", $"Products feed for {tenantName} - Snapchat");

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

        // Snapchat uses g:id = FeedItemId (canonical Feed-0 identity contract,
        // same as Meta and Google). The FeedItemId GUID is always authoritative
        // and present. SKU is NOT reinterpreted as g:id or g:mpn.
        FeedXml.WriteGoogleElement(writer, "id", item.FeedItemId.ToString("D"));
        if (item.ItemGroupId.HasValue && item.ItemGroupId.Value != Guid.Empty)
        {
            FeedXml.WriteGoogleElement(writer, "item_group_id", item.ItemGroupId.Value.ToString("D"));
        }

        if (FeedXml.IsValidAbsoluteHttpUrl(item.PrimaryImageUrl))
        {
            FeedXml.WriteGoogleElement(writer, "image_link", item.PrimaryImageUrl);
        }

        foreach (var additionalUrl in item.AdditionalImageUrls)
        {
            if (FeedXml.IsValidAbsoluteHttpUrl(additionalUrl))
            {
                FeedXml.WriteGoogleElement(writer, "additional_image_link", additionalUrl);
            }
        }

        FeedXml.WriteGoogleElement(writer, "price", FeedXml.FormatPrice(item.Price, item.Currency));
        if (item.SalePrice.HasValue && item.SalePrice.Value > 0m && item.SalePrice.Value < item.Price)
        {
            FeedXml.WriteGoogleElement(writer, "sale_price", FeedXml.FormatPrice(item.SalePrice.Value, item.Currency));
        }

        // Canonical FeedAvailability vocabulary ("in stock" / "available for order"
        // / "out of stock") matches Snapchat's space-separated values exactly.
        FeedXml.WriteGoogleElement(writer, "availability", item.Availability);
        FeedXml.WriteGoogleElement(writer, "condition", item.Condition);

        if (!string.IsNullOrWhiteSpace(item.Brand))
        {
            FeedXml.WriteGoogleElement(writer, "brand", item.Brand);
        }
        if (!string.IsNullOrWhiteSpace(item.CategoryName))
        {
            FeedXml.WriteGoogleElement(writer, "product_type", item.CategoryName);
        }
        if (!string.IsNullOrWhiteSpace(item.Gtin))
        {
            FeedXml.WriteGoogleElement(writer, "gtin", item.Gtin);
        }

        // NOTE: g:mpn is intentionally never emitted. Snapchat supports it as an
        // optional field, but Shopixy SKUs are merchant-assigned and must not be
        // interpreted as manufacturer part numbers (parity with the Google and
        // TikTok adapters' explicit discipline).
        // NOTE: google_product_category is intentionally never emitted. Shopixy
        // has no Google product taxonomy mapping; emitting invented values would
        // violate Snapchat's accuracy requirement.

        writer.WriteEndElement(); // item
    }
}
