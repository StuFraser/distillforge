using System.Text;
using DistillForge.Core.Contracts;

namespace DistillForge.Core.Export;

/// <summary>Which optional details of a note go into the email.</summary>
public sealed record EmailOptions(bool Owner = true, bool Due = true, bool Summary = true, bool Source = true);

/// <summary>
/// A plain-text email: a subject, a header and one block per note. Kept as separate blocks (not one string) so a length-limited
/// <c>mailto:</c> link can drop whole notes instead of cutting one in half.
/// </summary>
public sealed record EmailDraft(string Subject, string Header, IReadOnlyList<string> Items)
{
    public string Body => Items.Count == 0 ? Header : Header + "\n\n" + string.Join("\n\n", Items);
}

/// <summary>Formats action notes as a readable plain-text email. Pure and browser-safe; nothing here knows about mail clients.</summary>
public static class EmailExport
{
    public const int MaxQuoteChars = 240;
    public const int MaxSubjectChars = 120;

    /// <summary>
    /// Merged notes are left out: they are duplicates folded into another note, which is the one that gets shared.
    /// Order follows the board's order (SortOrder).
    /// </summary>
    public static IReadOnlyList<ActionItemDto> Shareable(IEnumerable<ActionItemDto> notes) =>
        notes.Where(n => n.MergedIntoId is null).OrderBy(n => n.SortOrder).ToList();

    public static EmailDraft Build(string workspaceTitle, string scopeName, IEnumerable<ActionItemDto> notes, EmailOptions options)
    {
        var list = Shareable(notes);
        var subject = Clip($"Action items: {scopeName} ({workspaceTitle})", MaxSubjectChars);

        var header = new StringBuilder()
            .Append("Action items: ").Append(scopeName).Append('\n')
            .Append("From: ").Append(workspaceTitle).Append('\n')
            .Append(list.Count == 1 ? "1 item" : $"{list.Count} items");

        var items = list.Select((n, i) => FormatNote(i + 1, n, options)).ToList();
        return new EmailDraft(subject, header.ToString(), items);
    }

    public static string FormatNote(int number, ActionItemDto note, EmailOptions options)
    {
        const string indent = "   ";
        var sb = new StringBuilder();
        sb.Append(number).Append(". ").Append(Collapse(note.Title));

        var meta = new List<string>();
        if (options.Owner && !string.IsNullOrWhiteSpace(note.Owner)) meta.Add("Owner: " + Collapse(note.Owner));
        if (options.Due && !string.IsNullOrWhiteSpace(note.Due)) meta.Add("Due: " + Collapse(note.Due));
        if (meta.Count > 0) sb.Append('\n').Append(indent).Append(string.Join("  ·  ", meta));

        if (options.Summary && !string.IsNullOrWhiteSpace(note.Summary))
            sb.Append('\n').Append(indent).Append(Collapse(note.Summary));

        if (options.Source && note.Evidence.Count > 0)
        {
            var quote = Clip(Collapse(note.Evidence[0].Quote), MaxQuoteChars);
            if (quote.Length > 0)
            {
                sb.Append('\n').Append(indent).Append("Source: “").Append(quote).Append('”');
                if (note.Evidence.Count > 1)
                    sb.Append(" (+").Append(note.Evidence.Count - 1).Append(note.Evidence.Count == 2 ? " more passage)" : " more passages)");
            }
        }
        return sb.ToString();
    }

    /// <summary>Single-line, single-spaced text: source quotes and summaries can contain line breaks that would wreck the layout.</summary>
    private static string Collapse(string s) => string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
}

public sealed record MailtoResult(string Uri, int Included, int Total)
{
    public bool Truncated => Included < Total;
}

/// <summary>
/// Builds a <c>mailto:</c> link. Mail clients and browsers cap link length (Outlook is the strictest at about 2,083 characters),
/// so long drafts are cut at a whole-note boundary with a pointer to the Copy button; the full text is always available there.
/// </summary>
public static class MailtoLink
{
    public const int DefaultMaxLength = 1900;

    public static MailtoResult Build(string? to, EmailDraft draft, int maxLength = DefaultMaxLength)
    {
        var recipients = NormaliseRecipients(to);
        int total = draft.Items.Count;

        // Longest prefix of whole notes that fits. Items shrink monotonically, so scan down from "all".
        for (int included = total; included >= 0; included--)
        {
            bool truncated = included < total;
            var body = Compose(draft, included, truncated, total);
            var uri = Uri(recipients, draft.Subject, body);
            if (uri.Length <= maxLength || included == 0)
            {
                // With nothing included the header alone may still be too long; cut it rather than emit an unusable link.
                if (uri.Length > maxLength) uri = Uri(recipients, draft.Subject, "");
                return new MailtoResult(uri, included, total);
            }
        }
        return new MailtoResult(Uri(recipients, draft.Subject, ""), 0, total); // unreachable
    }

    private static string Compose(EmailDraft draft, int included, bool truncated, int total)
    {
        var sb = new StringBuilder(draft.Header);
        if (included > 0) sb.Append("\n\n").Append(string.Join("\n\n", draft.Items.Take(included)));
        if (truncated)
            sb.Append("\n\n[").Append(total - included).Append(total - included == 1 ? " more item" : " more items")
              .Append(" not included: the email link was too long. Use Copy in DistillForge to get the full list.]");
        return sb.ToString();
    }

    private static string Uri(string recipients, string subject, string body) =>
        "mailto:" + EncodeRecipients(recipients) + "?subject=" + Encode(subject) + "&body=" + Encode(body);

    /// <summary>Accepts commas, semicolons or whitespace between addresses; returns them comma separated.</summary>
    public static string NormaliseRecipients(string? to) =>
        string.Join(',', (to ?? "").Split([',', ';', ' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    // Keep @ and , readable in the recipient list; everything else is percent-encoded.
    private static string EncodeRecipients(string recipients) =>
        recipients.Length == 0 ? "" : System.Uri.EscapeDataString(recipients).Replace("%40", "@").Replace("%2C", ",");

    // Mail clients expect CRLF line breaks in a mailto body.
    private static string Encode(string s) => System.Uri.EscapeDataString(s.Replace("\r\n", "\n").Replace("\n", "\r\n"));
}
