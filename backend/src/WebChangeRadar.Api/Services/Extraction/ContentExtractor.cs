using System.Net;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace WebChangeRadar.Api.Services.Extraction;

public enum ContentMode { Text, Html }

public interface IContentExtractor
{
    string Extract(string? html, ContentMode mode = ContentMode.Text);
    string? ExtractSelected(string? html, string selector);
}

public sealed class ContentExtractor : IContentExtractor
{
    public static bool IsValidSelector(string selector)
    {
        try { new HtmlParser().ParseDocument("<main></main>").QuerySelectorAll(selector); return true; }
        catch (Exception ex) when (ex is ArgumentException or AngleSharp.Dom.DomException) { return false; }
    }

    public string? ExtractSelected(string? html, string selector)
    {
        var document = new HtmlParser().ParseDocument(html ?? "");
        var matches = document.QuerySelectorAll(selector);
        if (matches.Length == 0) return null;
        var output = new StringBuilder();
        foreach (var element in matches)
        {
            if (element.Ancestors().OfType<IElement>().Any(ancestor =>
                    matches.Contains(ancestor) || IsIgnored(ancestor))) continue;
            AppendText(element, output);
        }
        return NormalizeTextOutput(output.ToString());
    }
    private static readonly HashSet<string> IgnoredElements = new(StringComparer.Ordinal)
    {
        "script", "style", "noscript", "template", "svg", "canvas", "iframe"
    };

    private static readonly HashSet<string> BlockElements = new(StringComparer.Ordinal)
    {
        "address", "article", "aside", "blockquote", "body", "br", "dd", "div", "dl",
        "dt", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6", "header", "hr",
        "li", "main", "nav", "ol", "p", "pre", "section", "table", "tbody", "td", "th",
        "thead", "tfoot", "tr", "ul"
    };

    private static readonly HashSet<string> VoidElements = new(StringComparer.Ordinal)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta",
        "param", "source", "track", "wbr"
    };

    private static readonly HashSet<string> ContentAttributes = new(StringComparer.Ordinal)
    {
        "alt", "aria-label", "href", "src", "title"
    };

    public string Extract(string? html, ContentMode mode = ContentMode.Text)
    {
        if (mode is not (ContentMode.Text or ContentMode.Html))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        // A parser instance belongs to this call, so the singleton service is safe to share.
        var document = new HtmlParser().ParseDocument(html);
        var root = (INode?)document.Body ?? document.DocumentElement;
        if (root is null) return string.Empty;

        var result = new StringBuilder();
        foreach (var node in root.ChildNodes)
        {
            if (mode == ContentMode.Text) AppendText(node, result);
            else AppendHtml(node, result);
        }

        return mode == ContentMode.Text
            ? NormalizeTextOutput(result.ToString())
            : result.ToString().Trim();
    }

    private static void AppendText(INode node, StringBuilder output)
    {
        if (node is IText text)
        {
            output.Append(NormalizeWhitespace(text.Data));
            return;
        }
        if (node is not IElement element || IsIgnored(element)) return;

        var block = BlockElements.Contains(element.LocalName);
        if (block) output.Append('\n');
        foreach (var child in element.ChildNodes) AppendText(child, output);
        if (block) output.Append('\n');
    }

    private static void AppendHtml(INode node, StringBuilder output)
    {
        if (node is IText text)
        {
            var value = NormalizeWhitespace(text.Data);
            if (value == " " && node.ParentElement is { } parent &&
                BlockElements.Contains(parent.LocalName) &&
                (node.PreviousSibling is null || node.NextSibling is null ||
                 node.PreviousSibling is IElement previous && BlockElements.Contains(previous.LocalName) ||
                 node.NextSibling is IElement next && BlockElements.Contains(next.LocalName)))
                return;
            output.Append(WebUtility.HtmlEncode(value));
            return;
        }
        if (node is not IElement element || IsIgnored(element)) return;

        var name = element.LocalName;
        output.Append('<').Append(name);
        foreach (var attribute in element.Attributes
            .Where(attribute => ContentAttributes.Contains(attribute.Name))
            .OrderBy(attribute => attribute.Name, StringComparer.Ordinal))
        {
            output.Append(' ').Append(attribute.Name).Append("=\"")
                .Append(WebUtility.HtmlEncode(NormalizeWhitespace(attribute.Value).Trim()))
                .Append('"');
        }
        output.Append('>');
        if (VoidElements.Contains(name)) return;

        var children = new StringBuilder();
        foreach (var child in element.ChildNodes) AppendHtml(child, children);
        var content = children.ToString();
        output.Append(BlockElements.Contains(name) ? content.Trim() : content);
        output.Append("</").Append(name).Append('>');
    }

    private static bool IsIgnored(IElement element) =>
        IgnoredElements.Contains(element.LocalName) ||
        element.HasAttribute("hidden") ||
        string.Equals(element.GetAttribute("aria-hidden"), "true", StringComparison.OrdinalIgnoreCase) ||
        HasHiddenInlineStyle(element.GetAttribute("style"));

    private static bool HasHiddenInlineStyle(string? style) => style is not null &&
        style.Split(';').Any(declaration =>
        {
            var parts = declaration.Split(':', 2);
            if (parts.Length != 2) return false;
            var property = parts[0].Trim();
            var value = parts[1].Trim();
            return property.Equals("display", StringComparison.OrdinalIgnoreCase) &&
                   value.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                   property.Equals("visibility", StringComparison.OrdinalIgnoreCase) &&
                   value.Equals("hidden", StringComparison.OrdinalIgnoreCase);
        });

    private static string NormalizeWhitespace(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormC);
        var result = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        foreach (var character in normalized)
        {
            if (char.IsWhiteSpace(character))
            {
                pendingSpace = true;
                continue;
            }
            if (pendingSpace) result.Append(' ');
            result.Append(character);
            pendingSpace = false;
        }
        if (pendingSpace) result.Append(' ');
        return result.ToString();
    }

    private static string NormalizeTextOutput(string value) => string.Join('\n',
        value.Split('\n').Select(line => NormalizeWhitespace(line).Trim())
            .Where(line => line.Length > 0));
}
