using System.Xml;
using PointOfSale.API.DTOs.Feeds;

namespace PointOfSale.API.Services.Feeds;

/// <summary>
/// FEED-1A: Serializes canonical <see cref="FeedProduct"/> items into the
/// Meta RSS 2.0 feed format (xmlns:g = http://base.google.com/ns/1.0).
/// </summary>
public interface IMetaFeedXmlSerializer
{
    /// <summary>
    /// Produces a complete RSS document. Optional g: nodes are omitted entirely
    /// when absent/invalid — no empty elements are ever emitted.
    /// </summary>
    string Serialize(string tenantName, string channelLink, IReadOnlyList<FeedProduct> items);
}

/// <summary>
/// FEED-1A: XmlWriter-based serializer. Escaping (&amp; &lt; &gt; quotes, Arabic,
/// any UTF-8 text) is handled by the XML stack — no manual string concatenation.
/// FEED-2: shared XML infrastructure moved to <see cref="FeedXml"/> (same output).
/// </summary>
public sealed class MetaFeedXmlSerializer : IMetaFeedXmlSerializer
{
    public string Serialize(string tenantName, string channelLink, IReadOnlyList<FeedProduct> items)
    {
        var settings = FeedXml.CreateUtf8RssWriterSettings();

        // Serialize through a byte stream: XmlWriter only honors settings.Encoding
        // in the XML declaration when the sink has a real byte encoding. A
        // StringBuilder sink is char-based and always declares "utf-16", which
        // would contradict the UTF-8 response bytes and Content-Type.
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
                FeedXml.WriteElement(writer, "description", $"Products feed for {tenantName}");

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
        if (!string.IsNullOrWhiteSpace(item.Sku))
        {
            FeedXml.WriteGoogleElement(writer, "sku", item.Sku);
        }
        if (!string.IsNullOrWhiteSpace(item.Gtin))
        {
            FeedXml.WriteGoogleElement(writer, "gtin", item.Gtin);
        }

        writer.WriteEndElement(); // item
    }
}
