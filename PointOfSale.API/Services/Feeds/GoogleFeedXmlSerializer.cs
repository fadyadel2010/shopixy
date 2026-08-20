using System.Xml;
using PointOfSale.API.DTOs.Feeds;

namespace PointOfSale.API.Services.Feeds;

/// <summary>
/// FEED-2: Serializes canonical <see cref="FeedProduct"/> items into the
/// Google Merchant Center RSS 2.0 product data source format
/// (xmlns:g = http://base.google.com/ns/1.0).
/// </summary>
public interface IGoogleFeedXmlSerializer
{
    /// <summary>
    /// Produces a complete RSS document. Optional g: nodes are omitted entirely
    /// when absent/invalid — no empty elements are ever emitted.
    /// </summary>
    string Serialize(string tenantName, string channelLink, IReadOnlyList<FeedProduct> items);
}

/// <summary>
/// FEED-2: Google channel adapter over the SAME certified FeedProduct truth.
/// Shares the Feed-1A XML infrastructure (<see cref="FeedXml"/>): XmlWriter
/// escaping (no manual concatenation), UTF-8 MemoryStream encoding, and the
/// same omission discipline. Only the attribute vocabulary differs from Meta:
///
/// - Google's specification has no g:sku attribute, and g:mpn is emitted ONLY
///   with verified manufacturer-part-number semantics — Shopixy SKUs are
///   merchant-assigned, so g:mpn is deliberately never invented here.
/// - g:gtin is emitted only when the canonical projection validated the
///   barcode (Feed-1A TryBuildGtin semantics), never reinterpreted.
/// - Variants keep the Feed-0 identity contract: g:id = VariantId,
///   g:item_group_id = ProductId, parent product URL (no variant routes).
/// </summary>
public sealed class GoogleFeedXmlSerializer : IGoogleFeedXmlSerializer
{
    public string Serialize(string tenantName, string channelLink, IReadOnlyList<FeedProduct> items)
    {
        var settings = FeedXml.CreateUtf8RssWriterSettings();

        // Byte-stream sink (certified Feed-1A fix): the XML declaration only
        // reports utf-8 when the sink has a real byte encoding.
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
                FeedXml.WriteElement(writer, "description", $"Products feed for {tenantName} - Google");

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

        // RSS-level Google attributes: title/description/link are plain RSS
        // elements; product attributes live under the g: namespace.
        FeedXml.WriteElement(writer, "title", item.Title);
        FeedXml.WriteElement(writer, "link", item.ProductUrl);
        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            FeedXml.WriteElement(writer, "description", item.Description);
        }

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

        // Google Merchant Center uses its own attribute values
        // (in_stock/out_of_stock/backorder). Keep FeedAvailability channel-neutral.
        FeedXml.WriteGoogleElement(writer, "availability", MapGoogleAvailability(item.Availability));

        // Feed-0 established "new" as the only supported condition domain value.
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
        if (ShouldDeclareGoogleIdentifiersUnavailable(item))
        {
            FeedXml.WriteGoogleElement(writer, "identifier_exists", "no");
        }

        writer.WriteEndElement(); // item
    }

    private static string MapGoogleAvailability(string availability) => availability switch
    {
        FeedAvailability.InStock => "in_stock",
        FeedAvailability.OutOfStock => "out_of_stock",
        FeedAvailability.AvailableForOrder => "backorder",
        _ => availability
    };

    private static bool ShouldDeclareGoogleIdentifiersUnavailable(FeedProduct item) =>
        // Shopixy has no authoritative MPN field. When no GTIN is available,
        // use Google's documented identifier_exists mechanism instead of
        // inventing g:mpn from SKU or store data. If Brand is present it is
        // still emitted above as the one UPI Shopixy can truthfully provide.
        string.IsNullOrWhiteSpace(item.Gtin);
}
