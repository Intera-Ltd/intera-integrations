using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Intera.Jira.Api.Infrastructure;

/// <summary>
/// Flattens an Atlassian Document Format value — what REST API v3 answers for an issue's description and
/// environment, a comment's body, a worklog's comment and a rich-text custom field — into plain text.
/// </summary>
/// <remarks>
/// <para>
/// A dashboard has no use for the document tree, only for its words. Blocks (paragraphs, headings, list items,
/// table rows, code blocks) become lines; a bullet list item starts with <c>- </c>, an ordered one with its number,
/// table cells are joined with <c> | </c>; inline nodes keep what a reader sees — a mention its <c>@name</c>, an
/// emoji its character or short name, a link card its URL, a date its <c>yyyy-MM-dd</c>, a status lozenge its
/// label. Media (attachments, images) leave nothing: there is no text to keep.
/// </para>
/// <para>
/// A value that is already a string (a v2-style field, or a plain-text custom field) is returned as it is; any
/// other shape that is not a document gives null rather than its JSON, which would read as content.
/// </para>
/// </remarks>
public static class AdfText
{
    private static readonly HashSet<string> BlockTypes = new(StringComparer.Ordinal)
    {
        "paragraph", "heading", "codeBlock", "blockquote", "panel", "rule", "bulletList", "orderedList", "listItem",
        "table", "tableRow", "tableCell", "tableHeader", "expand", "nestedExpand", "mediaSingle", "mediaGroup",
        "taskList", "taskItem", "decisionList", "decisionItem", "layoutSection", "layoutColumn", "blockCard",
        "embedCard", "bodiedExtension", "extension", "doc"
    };

    /// <summary>The text of <paramref name="value"/>, or null when there is none.</summary>
    public static string? ToText(JsonElement? value)
    {
        if (value is not { } element)
        {
            return null;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString() is { Length: > 0 } text ? text : null;

            case JsonValueKind.Object when string.Equals(NodeType(element), "doc", StringComparison.Ordinal):
                var rendered = Normalise(RenderBlock(element));
                return rendered.Length == 0 ? null : rendered;

            default:
                return null;
        }
    }

    private static string RenderBlock(JsonElement node)
    {
        var type = NodeType(node);

        switch (type)
        {
            case "paragraph" or "heading" or "codeBlock" or "decisionItem":
                return RenderInline(node);

            case "taskItem":
                var done = string.Equals(JiraJson.String(JiraJson.Object(node, "attrs"), "state"), "DONE", StringComparison.OrdinalIgnoreCase);
                return (done ? "[x] " : "[ ] ") + RenderInline(node);

            case "rule":
                return "---";

            case "bulletList":
                return string.Join("\n", Children(node).Select(item => Prefix("- ", RenderBlock(item))));

            case "orderedList":
                var start = JiraJson.Long(JiraJson.Object(node, "attrs"), "order") ?? 1;
                return string.Join("\n", Children(node).Select((item, index) =>
                    Prefix($"{(start + index).ToString(CultureInfo.InvariantCulture)}. ", RenderBlock(item))));

            case "table":
                return string.Join("\n", Children(node).Select(RenderBlock));

            case "tableRow":
                return string.Join(" | ", Children(node).Select(cell => RenderBlock(cell).Replace('\n', ' ').Trim()));

            case "blockCard" or "embedCard":
                return JiraJson.String(JiraJson.Object(node, "attrs"), "url") ?? string.Empty;

            case "media" or "mediaSingle" or "mediaGroup" or "mediaInline":
                return string.Empty;

            default:
                var children = Children(node).ToList();
                if (children.Count == 0)
                {
                    return RenderInlineNode(node);
                }

                // A container of blocks (doc, list item, panel, expand, cell, …) puts each on its own line; an unknown
                // node holding inline content is read like a paragraph.
                return children.Any(static child => BlockTypes.Contains(NodeType(child) ?? string.Empty))
                    ? string.Join("\n", children.Select(RenderBlock).Where(static text => text.Length > 0))
                    : RenderInline(node);
        }
    }

    private static string RenderInline(JsonElement node)
    {
        var text = new StringBuilder();
        foreach (var child in Children(node))
        {
            text.Append(BlockTypes.Contains(NodeType(child) ?? string.Empty) ? RenderBlock(child) : RenderInlineNode(child));
        }

        return text.ToString();
    }

    private static string RenderInlineNode(JsonElement node)
    {
        var attrs = JiraJson.Object(node, "attrs");

        return NodeType(node) switch
        {
            "text" => JiraJson.String(node, "text") ?? string.Empty,
            "hardBreak" => "\n",
            "mention" => JiraJson.String(attrs, "text") ?? string.Empty,
            "emoji" => JiraJson.String(attrs, "text") ?? JiraJson.String(attrs, "shortName") ?? string.Empty,
            "inlineCard" => JiraJson.String(attrs, "url") ?? string.Empty,
            "status" => JiraJson.String(attrs, "text") ?? string.Empty,
            "date" => JiraJson.Long(attrs, "timestamp") is { } ms
                ? DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : JiraJson.String(attrs, "timestamp") is { } raw && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? DateTimeOffset.FromUnixTimeMilliseconds(parsed).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    : string.Empty,
            _ => Children(node).Any() ? RenderInline(node) : JiraJson.String(node, "text") ?? string.Empty
        };
    }

    private static IEnumerable<JsonElement> Children(JsonElement node) =>
        JiraJson.Array(node, "content").Where(static child => child.ValueKind == JsonValueKind.Object);

    private static string? NodeType(JsonElement node) => JiraJson.String(node, "type");

    /// <summary>Puts <paramref name="prefix"/> before the first line and indents the rest under it.</summary>
    private static string Prefix(string prefix, string text)
    {
        var indent = new string(' ', prefix.Length);
        var lines = text.Split('\n');
        return prefix + string.Join("\n", lines.Select((line, index) => index == 0 ? line : indent + line));
    }

    /// <summary>Trims every line's end and the whole, and allows at most one empty line in a row.</summary>
    private static string Normalise(string text)
    {
        var lines = text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n').Select(static line => line.TrimEnd());
        var result = new StringBuilder();
        var blank = 0;

        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                blank++;
                if (blank > 1)
                {
                    continue;
                }
            }
            else
            {
                blank = 0;
            }

            if (result.Length > 0)
            {
                result.Append('\n');
            }

            result.Append(line);
        }

        return result.ToString().Trim('\n', ' ');
    }
}
