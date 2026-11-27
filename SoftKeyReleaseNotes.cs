using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

// Parses GitHub release note bodies into a calm summary plus categorized SoftKey changelog rows.
static class SoftKeyReleaseNotes
{
    internal enum Category { Nyhet, Förbättring, Fix }
    internal sealed record Entry(Category Category, string Title, string Detail);
    internal sealed record Parsed(string Summary, IReadOnlyList<Entry> Entries);

    static readonly Regex ExplicitTag = new(
        @"^(?:\[(?<tag>nyhet|förbättring|forbattring|fix|buggfix|feat|feature|improve|improvement)\]|(?<tag>nyhet|förbättring|forbattring|fix|buggfix|feat|feature|improve|improvement)\s*:)\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly Regex BulletPrefix = new(@"^(?:[•\-\*])\s+", RegexOptions.Compiled);
    static readonly Regex HeadingPrefix = new(@"^#{1,6}\s+", RegexOptions.Compiled);
    static readonly Regex BoldMarkers = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    static readonly string[] DetailSeparators = { " – ", " — ", " | " };

    public static Parsed Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new Parsed(null, Array.Empty<Entry>());

        var summaryLines = new List<string>();
        var entries = new List<Entry>();
        Entry current = null;
        Category? sectionCategory = null;

        foreach (string rawLine in raw.Replace("\r\n", "\n").Split('\n'))
        {
            string trimmed = rawLine.Trim();
            if (trimmed.Length == 0) continue;

            if (HeadingPrefix.IsMatch(trimmed))
            {
                string heading = HeadingPrefix.Replace(trimmed, "").Trim();
                sectionCategory = TryCategoryFromHeading(heading) ?? sectionCategory;
                continue;
            }

            if (BulletPrefix.IsMatch(trimmed))
            {
                string body = BulletPrefix.Replace(trimmed, "").Trim();
                if (body.Length == 0) continue;
                current = BuildEntry(body, sectionCategory);
                entries.Add(current);
                continue;
            }

            if (current != null)
            {
                string detailText = CleanMarkdown(trimmed);
                if (detailText.Length > 0)
                {
                    string merged = string.IsNullOrEmpty(current.Detail) ? detailText : current.Detail + " " + detailText;
                    current = current with { Detail = merged };
                    entries[^1] = current;
                }
            }
            else
            {
                string lead = CleanMarkdown(trimmed);
                if (lead.Length > 0) summaryLines.Add(lead);
            }
        }

        string summary = summaryLines.Count > 0 ? string.Join(" ", summaryLines) : null;
        if (entries.Count == 0 && !string.IsNullOrEmpty(summary))
        {
            entries.Add(BuildEntry(summary, sectionCategory));
            summary = null;
        }
        return new Parsed(summary, entries);
    }

    public static string Label(Category category) => category switch
    {
        Category.Nyhet => "NYHET",
        Category.Fix => "FIX",
        _ => "FÖRBÄTTRING"
    };

    static Entry BuildEntry(string rawBody, Category? sectionCategory)
    {
        Category? explicitCategory = null;
        string remainder = rawBody;
        var tagMatch = ExplicitTag.Match(remainder);
        if (tagMatch.Success)
        {
            explicitCategory = MapTag(tagMatch.Groups["tag"].Value);
            remainder = remainder[tagMatch.Length..].Trim();
        }

        string title = remainder;
        string detail = null;
        foreach (string separator in DetailSeparators)
        {
            int index = remainder.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0 && index + separator.Length < remainder.Length)
            {
                title = remainder[..index].Trim();
                detail = remainder[(index + separator.Length)..].Trim();
                break;
            }
        }

        title = CleanMarkdown(title);
        detail = string.IsNullOrWhiteSpace(detail) ? null : CleanMarkdown(detail);
        var category = explicitCategory ?? sectionCategory ?? Classify(title, detail);
        return new Entry(category, title, detail);
    }

    static string CleanMarkdown(string value) => BoldMarkers.Replace(value.Trim(), "$1").Trim();

    static Category? TryCategoryFromHeading(string heading)
    {
        string lower = heading.ToLowerInvariant();
        if (lower.Contains("fix") || lower.Contains("bugg") || lower.Contains("åtgärd")) return Category.Fix;
        if (lower.Contains("nyhet") || lower.Contains("nytt") || lower.Contains("new")) return Category.Nyhet;
        if (lower.Contains("förbättr") || lower.Contains("improv")) return Category.Förbättring;
        return null;
    }

    static Category MapTag(string tag) => tag.ToLowerInvariant() switch
    {
        "nyhet" or "feat" or "feature" => Category.Nyhet,
        "fix" or "buggfix" => Category.Fix,
        _ => Category.Förbättring
    };

    static Category Classify(string title, string detail)
    {
        string combined = (title + " " + (detail ?? "")).ToLowerInvariant();
        string[] fixMarkers = { "fix", "bugg", "åtgärd", "rätta", "krasch", "löser", "förhindra", "korriger", "trasig" };
        if (fixMarkers.Any(combined.Contains)) return Category.Fix;
        string[] newMarkers = { "nyhet", "ny ", "nytt ", "nya ", "lägg till", "inför", "stöd för", "inbyggd", "automatiser", "add ", "new ", "replace " };
        if (newMarkers.Any(combined.Contains)) return Category.Nyhet;
        return Category.Förbättring;
    }
}
