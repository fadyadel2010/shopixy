using System.Globalization;
using System.Text;
using System.Xml;

namespace PointOfSale.API.Services.Feeds;

/// <summary>
/// FEED-2: Small shared XML infrastructure used by every channel feed
/// serializer (Meta, Google). Extracted from the certified Feed-1A
/// MetaFeedXmlSerializer without changing its output: UTF-8 wire encoding
/// (MemoryStream sink so the XML declaration matches the actual bytes),
/// XmlWriter-based escaping (no manual string concatenation ever), and the
/// shared g: namespace vocabulary.
/// </summary>
internal static class FeedXml
{
    /// <summary>Google Merchant attribute namespace (also used by the Meta feed format).</summary>
    public const string GoogleNamespace = "http://base.google.com/ns/1.0";

    /// <summary>
    /// Certified Feed-1A writer settings: UTF-8 without BOM, declaration
    /// included. MUST be paired with a byte-stream sink (MemoryStream) — a
    /// char-based sink would force a utf-16 declaration that contradicts the
    /// UTF-8 response bytes.
    /// </summary>
    public static XmlWriterSettings CreateUtf8RssWriterSettings() => new()
    {
        Encoding = new UTF8Encoding(false),
        Indent = true,
        OmitXmlDeclaration = false
    };

    /// <summary>Channel price format: "12.99 USD" — invariant culture, two decimals, ISO 4217 code.</summary>
    public static string FormatPrice(decimal price, string currency) =>
        string.Format(CultureInfo.InvariantCulture, "{0:0.00} {1}", price, currency);

    public static void WriteElement(XmlWriter writer, string localName, string value)
    {
        writer.WriteStartElement(localName);
        writer.WriteString(value);
        writer.WriteEndElement();
    }

    public static void WriteGoogleElement(XmlWriter writer, string localName, string value)
    {
        writer.WriteStartElement("g", localName, GoogleNamespace);
        writer.WriteString(value);
        writer.WriteEndElement();
    }

    /// <summary>Only absolute http(s) URLs may ever reach a feed document.</summary>
    public static bool IsValidAbsoluteHttpUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
