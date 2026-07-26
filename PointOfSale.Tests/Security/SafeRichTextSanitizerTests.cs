using PointOfSale.API.Services.Security;

namespace PointOfSale.Tests.Security;

public sealed class SafeRichTextSanitizerTests
{
    [Fact]
    public void Sanitize_ShouldRemove_BasicScriptTag()
    {
        var input = "<p>Hello</p><script>alert(1)</script><p>World</p>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result);
        Assert.Contains("<p>Hello</p>", result);
        Assert.Contains("<p>World</p>", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_NestedDangerousTags()
    {
        var input = "<script><script>alert(1)</script></script>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_DeeplyNestedDangerousTags()
    {
        var input = "<div><script><script><script>alert(1)</script></script></script></div>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result);
        Assert.Contains("<div>", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_HtmlEntityEncodedScriptTag()
    {
        var input = "&#60;script&#62;alert(1)&#60;/script&#62;";
        var result = SafeRichTextSanitizer.Sanitize(input);

        // Entity-encoded tags should be escaped as text, not executed
        Assert.DoesNotContain("<script>", result);
        Assert.Contains("&lt;", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_MixedCaseEntityEncodedTags()
    {
        var input = "&#60;ScRiPt&#62;alert(1)&#60;/ScRiPt&#62;";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_HexEntityEncodedScriptTag()
    {
        var input = "&#x3C;script&#x3E;alert(1)&#x3C;/script&#x3E;";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<script>", result);
        Assert.Contains("&lt;", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_SvgWithEmbeddedScript()
    {
        var input = "<svg><script>alert(1)</script></svg>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<svg", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_SvgWithOnloadAttribute()
    {
        var input = "<svg onload=\"alert(1)\"></svg>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<svg", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onload", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_MathElementWithScript()
    {
        var input = "<math><script>alert(1)</script></math>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<math", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_MalformedNestedTags()
    {
        var input = "<div><script<script>>alert(1)</script></div>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("alert(1)", result);
        Assert.Contains("<div>", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_UnclosedScriptTag()
    {
        var input = "<p>Test</p><script>alert(1)";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<p>Test</p>", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_SelfClosingScriptTag()
    {
        var input = "<script src=\"evil.js\" />";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.js", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_EventHandlerAttributes()
    {
        var input = "<a href=\"#\" onclick=\"alert(1)\">Click</a>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("onclick", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result);
        Assert.Contains("<a", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_OnErrorAttribute()
    {
        var input = "<img src=\"x\" onerror=\"alert(1)\" />";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<img", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_JavascriptProtocolInHref()
    {
        var input = "<a href=\"javascript:alert(1)\">Click</a>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("javascript:", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_JavascriptProtocolWithWhitespace()
    {
        var input = "<a href=\"java script:alert(1)\">Click</a>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        // Link should be removed entirely since href is invalid
        Assert.DoesNotContain("href", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_DataProtocolInHref()
    {
        var input = "<a href=\"data:text/html,<script>alert(1)</script>\">Click</a>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("data:", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_ShouldRemove_VbscriptProtocolInHref()
    {
        var input = "<a href=\"vbscript:msgbox(1)\">Click</a>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("vbscript:", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("msgbox", result);
    }

    [Fact]
    public void Sanitize_ShouldAllow_SafeHttpsHref()
    {
        var input = "<a href=\"https://example.com\">Link</a>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Contains("href=\"https://example.com\"", result);
        Assert.Contains(">Link</a>", result);
    }

    [Fact]
    public void Sanitize_ShouldAllow_SafeHttpHref()
    {
        var input = "<a href=\"http://example.com\">Link</a>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Contains("href=\"http://example.com\"", result);
        Assert.Contains(">Link</a>", result);
    }

    [Fact]
    public void Sanitize_ShouldAllow_MailtoHref()
    {
        var input = "<a href=\"mailto:test@example.com\">Email</a>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Contains("href=\"mailto:test@example.com\"", result);
        Assert.Contains(">Email</a>", result);
    }

    [Fact]
    public void Sanitize_ShouldAllow_TelHref()
    {
        var input = "<a href=\"tel:+1234567890\">Call</a>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Contains("href=\"tel:+1234567890\"", result);
        Assert.Contains(">Call</a>", result);
    }

    [Fact]
    public void Sanitize_ShouldAllow_RelativeHref()
    {
        var input = "<a href=\"/page/about\">About</a>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Contains("href=\"/page/about\"", result);
        Assert.Contains(">About</a>", result);
    }

    [Fact]
    public void Sanitize_ShouldReject_ProtocolRelativeUrl()
    {
        var input = "<a href=\"//evil.com/malware\">Link</a>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("href", result);
        Assert.DoesNotContain("evil.com", result);
    }

    [Fact]
    public void Sanitize_ShouldAdd_NoopenerNoreferrerToBlankTarget()
    {
        var input = "<a href=\"https://example.com\" target=\"_blank\">Link</a>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Contains("target=\"_blank\"", result);
        Assert.Contains("rel=\"noopener noreferrer\"", result);
    }

    [Fact]
    public void Sanitize_ShouldPreserve_ExistingRelWithBlankTarget()
    {
        var input = "<a href=\"https://example.com\" target=\"_blank\" rel=\"external\">Link</a>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Contains("target=\"_blank\"", result);
        Assert.Contains("noopener", result);
        Assert.Contains("noreferrer", result);
        Assert.Contains("external", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_IframeTag()
    {
        var input = "<iframe src=\"https://evil.com\"></iframe>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<iframe", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.com", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_ObjectTag()
    {
        var input = "<object data=\"malware.swf\"></object>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<object", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("malware.swf", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_EmbedTag()
    {
        var input = "<embed src=\"malware.swf\" />";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<embed", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("malware.swf", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_FormTag()
    {
        var input = "<form action=\"/steal-data\" method=\"post\"><input name=\"password\" /></form>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<form", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<input", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("steal-data", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_InputTag()
    {
        var input = "<input type=\"text\" value=\"test\" />";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<input", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_ShouldRemove_ButtonTag()
    {
        var input = "<button onclick=\"alert(1)\">Click</button>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<button", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_ShouldRemove_StyleTag()
    {
        var input = "<style>body { background: url('javascript:alert(1)'); }</style>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<style", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_ShouldRemove_LinkTag()
    {
        var input = "<link rel=\"stylesheet\" href=\"evil.css\" />";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<link", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.css", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_MetaTag()
    {
        var input = "<meta http-equiv=\"refresh\" content=\"0;url=https://evil.com\" />";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<meta", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.com", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_BaseTag()
    {
        var input = "<base href=\"https://evil.com\" />";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<base", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("evil.com", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_HtmlComments()
    {
        var input = "<p>Test</p><!-- This is a comment --><p>More</p>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<!--", result);
        Assert.DoesNotContain("-->", result);
        Assert.DoesNotContain("This is a comment", result);
        Assert.Contains("<p>Test</p>", result);
        Assert.Contains("<p>More</p>", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_CommentWithScriptTag()
    {
        var input = "<!-- <script>alert(1)</script> -->";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<!--", result);
        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result);
    }

    [Fact]
    public void Sanitize_ShouldAllow_SafeFormattingTags()
    {
        var input = "<p>This is <strong>bold</strong> and <em>italic</em> text.</p>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Contains("<p>", result);
        Assert.Contains("<strong>", result);
        Assert.Contains("<em>", result);
        Assert.Contains("bold", result);
        Assert.Contains("italic", result);
    }

    [Fact]
    public void Sanitize_ShouldAllow_HeadingTags()
    {
        var input = "<h1>Title</h1><h2>Subtitle</h2><h3>Section</h3>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Contains("<h1>Title</h1>", result);
        Assert.Contains("<h2>Subtitle</h2>", result);
        Assert.Contains("<h3>Section</h3>", result);
    }

    [Fact]
    public void Sanitize_ShouldAllow_ListTags()
    {
        var input = "<ul><li>Item 1</li><li>Item 2</li></ul>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Contains("<ul>", result);
        Assert.Contains("<li>", result);
        Assert.Contains("Item 1", result);
        Assert.Contains("Item 2", result);
    }

    [Fact]
    public void Sanitize_ShouldAllow_CodeAndPreTags()
    {
        var input = "<pre><code>const x = 1;</code></pre>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Contains("<pre>", result);
        Assert.Contains("<code>", result);
        Assert.Contains("const x = 1;", result);
    }

    [Fact]
    public void Sanitize_ShouldEscape_UnallowedTagsAsText()
    {
        var input = "<p>Test</p><custom>Not allowed</custom>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Contains("<p>Test</p>", result);
        Assert.DoesNotContain("<custom>", result);
        Assert.Contains("&lt;custom&gt;", result);
        Assert.Contains("Not allowed", result);
    }

    [Fact]
    public void Sanitize_ShouldHandle_EmptyInput()
    {
        var result = SafeRichTextSanitizer.Sanitize("");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Sanitize_ShouldHandle_NullInput()
    {
        var result = SafeRichTextSanitizer.Sanitize(null!);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Sanitize_ShouldHandle_WhitespaceOnlyInput()
    {
        var result = SafeRichTextSanitizer.Sanitize("   \n\t   ");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Sanitize_ShouldHandle_PlainTextWithoutTags()
    {
        var input = "This is plain text without any HTML tags.";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void Sanitize_ShouldEscape_LessThanAndGreaterThanInText()
    {
        var input = "<p>5 < 10 and 10 > 5</p>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Contains("<p>", result);
        Assert.Contains("&lt;", result);
        Assert.Contains("&gt;", result);
    }

    [Fact]
    public void Sanitize_ShouldHandle_MalformedHtml()
    {
        var input = "<p>Test<p>Unclosed<div>Mixed</p></div>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        // Should still process valid tags
        Assert.Contains("<p>", result);
        Assert.Contains("<div>", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_ScriptWithWhitespaceVariations()
    {
        var input = "< script >alert(1)</script>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("alert(1)", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_ScriptWithNewlines()
    {
        var input = "<script\n>alert(1)</script>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("alert(1)", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_MultipleScriptTags()
    {
        var input = "<script>alert(1)</script><p>Safe</p><script>alert(2)</script>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result);
        Assert.Contains("<p>Safe</p>", result);
    }

    [Fact]
    public void Sanitize_ShouldHandle_ComplexNestedStructure()
    {
        var input = @"
            <div>
                <h1>Title</h1>
                <p>Paragraph with <strong>bold</strong> and <em>italic</em>.</p>
                <ul>
                    <li>Item 1</li>
                    <li>Item 2 with <a href=""https://example.com"">link</a></li>
                </ul>
                <blockquote>Quote text</blockquote>
            </div>";

        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Contains("<div>", result);
        Assert.Contains("<h1>", result);
        Assert.Contains("<p>", result);
        Assert.Contains("<strong>", result);
        Assert.Contains("<em>", result);
        Assert.Contains("<ul>", result);
        Assert.Contains("<li>", result);
        Assert.Contains("<a href=\"https://example.com\">", result);
        Assert.Contains("<blockquote>", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_MixedDangerousContent()
    {
        var input = @"
            <div>
                <p>Safe content</p>
                <script>alert('xss')</script>
                <p onclick=""alert('xss')"">Click me</p>
                <a href=""javascript:void(0)"">Bad link</a>
                <iframe src=""evil.com""></iframe>
                <p>More safe content</p>
            </div>";

        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Contains("<div>", result);
        Assert.Contains("Safe content", result);
        Assert.Contains("More safe content", result);
        Assert.DoesNotContain("<script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<iframe", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result);
    }

    [Fact]
    public void Sanitize_ShouldHandle_VoidTagsCorrectly()
    {
        var input = "<p>Line 1<br>Line 2<br />Line 3</p><hr><p>After</p>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Contains("<br />", result);
        Assert.Contains("<hr />", result);
        Assert.Contains("<p>Line 1", result);
        Assert.Contains("<p>After</p>", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_NoscriptTag()
    {
        var input = "<noscript>Your browser doesn't support JavaScript</noscript>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<noscript", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_ShouldRemove_TextareaTag()
    {
        var input = "<textarea>User input</textarea>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<textarea", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_ShouldRemove_SelectTag()
    {
        var input = "<select><option>Option 1</option></select>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("<select", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<option", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sanitize_ShouldHandle_CaseInsensitiveTagNames()
    {
        var input = "<P>Test</P><STRONG>Bold</STRONG><EM>Italic</EM>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.Contains("<p>Test</p>", result);
        Assert.Contains("<strong>Bold</strong>", result);
        Assert.Contains("<em>Italic</em>", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_CaseInsensitiveDangerousTags()
    {
        var input = "<SCRIPT>alert(1)</SCRIPT><ScRiPt>alert(2)</ScRiPt>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("script", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result);
    }

    [Fact]
    public void Sanitize_ShouldEncode_SpecialCharactersInAttributes()
    {
        var input = "<a href=\"https://example.com?a=1&b=2\">Link</a>";
        var result = SafeRichTextSanitizer.Sanitize(input);

        // Ampersands in URLs should be properly encoded
        Assert.Contains("href=", result);
        Assert.Contains("example.com", result);
    }

    [Fact]
    public void Sanitize_ShouldRemove_OnPrefixedAttributes()
    {
        var input = @"
            <div onload=""alert(1)"" onclick=""alert(2)"" onmouseover=""alert(3)"" 
                 onerror=""alert(4)"" onabort=""alert(5)"">Test</div>";

        var result = SafeRichTextSanitizer.Sanitize(input);

        Assert.DoesNotContain("onload", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onmouseover", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onabort", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", result);
        Assert.Contains("<div>Test</div>", result);
    }
}
