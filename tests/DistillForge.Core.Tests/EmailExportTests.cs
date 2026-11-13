using DistillForge.Core.Contracts;
using DistillForge.Core.Export;
using DistillForge.Core.Extraction;

namespace DistillForge.Core.Tests;

public class EmailExportTests
{
    private static ActionItemDto Note(string title, string? owner = null, string? due = null, string? summary = null,
        int sort = 0, Guid? mergedInto = null, params string[] quotes) =>
        new(Guid.NewGuid(), title, summary, owner, due, null, false, sort,
            quotes.Select((q, i) => new EvidenceDto($"S{i + 1}", q, 0, q.Length, MatchQuality.Exact)).ToList(),
            MergedIntoId: mergedInto);

    private static readonly EmailOptions All = new();

    [Fact]
    public void Formats_a_full_note()
    {
        var n = Note("Ring Sam", "Priya", "Friday", "About the rota.", 0, null, "Priya will ring Sam by Friday.");

        Assert.Equal(
            "1. Ring Sam\n   Owner: Priya  ·  Due: Friday\n   About the rota.\n   Source: “Priya will ring Sam by Friday.”",
            EmailExport.FormatNote(1, n, All));
    }

    [Fact]
    public void Missing_details_are_left_out_rather_than_shown_empty() =>
        Assert.Equal("3. Just a title", EmailExport.FormatNote(3, Note("Just a title", owner: " ", due: ""), All));

    [Fact]
    public void Options_switch_details_off()
    {
        var n = Note("T", "O", "D", "S", 0, null, "quote");
        Assert.Equal("1. T", EmailExport.FormatNote(1, n, new EmailOptions(false, false, false, false)));
        Assert.Contains("Owner: O", EmailExport.FormatNote(1, n, new EmailOptions(Due: false, Summary: false, Source: false)));
        Assert.DoesNotContain("Due:", EmailExport.FormatNote(1, n, new EmailOptions(Due: false)));
    }

    [Fact]
    public void Line_breaks_in_text_are_collapsed()
    {
        var n = Note("Two\nlines", summary: "a\r\n\r\nb   c", quotes: "x\ny");
        var text = EmailExport.FormatNote(1, n, All);
        Assert.Contains("1. Two lines", text);
        Assert.Contains("a b c", text);
        Assert.Contains("“x y”", text);
    }

    [Fact]
    public void Long_quotes_are_clipped_and_extra_passages_counted()
    {
        var n = Note("T", quotes: [new string('a', 500), "two", "three"]);
        var text = EmailExport.FormatNote(1, n, All);
        Assert.Contains("…” (+2 more passages)", text);
        Assert.True(text.Length < 400);
        Assert.Contains("(+1 more passage)", EmailExport.FormatNote(1, Note("T", quotes: ["a", "b"]), All));
    }

    [Fact]
    public void Merged_notes_are_excluded_and_order_follows_sort_order()
    {
        var parent = Note("Parent", sort: 5);
        var child = Note("Child", sort: 1, mergedInto: parent.Id);
        var first = Note("First", sort: 2);

        var draft = EmailExport.Build("Doc", "Everything", [parent, child, first], All);

        Assert.Equal(2, draft.Items.Count);
        Assert.StartsWith("1. First", draft.Items[0]);
        Assert.StartsWith("2. Parent", draft.Items[1]);
        Assert.DoesNotContain("Child", draft.Body);
        Assert.EndsWith("2 items", draft.Header);
    }

    [Fact]
    public void Header_and_subject_name_the_scope_and_document()
    {
        var draft = EmailExport.Build("Q3 call", "Follow ups", [Note("A")], All);
        Assert.Equal("Action items: Follow ups (Q3 call)", draft.Subject);
        Assert.Equal("Action items: Follow ups\nFrom: Q3 call\n1 item", draft.Header);
        Assert.Equal(draft.Header + "\n\n1. A", draft.Body);
    }

    [Fact]
    public void Empty_selection_gives_a_header_only_body()
    {
        var draft = EmailExport.Build("Doc", "X", [], All);
        Assert.Empty(draft.Items);
        Assert.Equal(draft.Header, draft.Body);
        Assert.EndsWith("0 items", draft.Header);
    }

    [Fact]
    public void Very_long_subjects_are_clipped() =>
        Assert.True(EmailExport.Build(new string('t', 400), "S", [], All).Subject.Length <= EmailExport.MaxSubjectChars);
}

public class MailtoLinkTests
{
    private static EmailDraft Draft(int items, int itemLength = 40) =>
        new("Subject here", "Header", Enumerable.Range(1, items).Select(i => $"{i}. " + new string('x', itemLength)).ToList());

    [Fact]
    public void Builds_an_encoded_link_with_crlf_line_breaks()
    {
        var link = MailtoLink.Build("sam@example.com", Draft(1));

        Assert.StartsWith("mailto:sam@example.com?subject=Subject%20here&body=", link.Uri);
        Assert.Contains("Header%0D%0A%0D%0A1.%20xxxx", link.Uri);
        Assert.False(link.Truncated);
        Assert.Equal((1, 1), (link.Included, link.Total));
    }

    [Fact]
    public void Recipients_may_be_separated_by_commas_semicolons_or_spaces_and_may_be_empty()
    {
        Assert.StartsWith("mailto:a@x.com,b@y.org,c@z.net?", MailtoLink.Build("a@x.com; b@y.org  c@z.net,", Draft(1)).Uri);
        Assert.StartsWith("mailto:?subject=", MailtoLink.Build(null, Draft(1)).Uri);
        Assert.StartsWith("mailto:?subject=", MailtoLink.Build("  ", Draft(1)).Uri);
    }

    [Fact]
    public void Special_characters_are_percent_encoded_and_cannot_inject_extra_fields()
    {
        var draft = new EmailDraft("A&B=C?", "H&cc=evil@x.com", []);
        var uri = MailtoLink.Build(null, draft).Uri;

        Assert.Contains("subject=A%26B%3DC%3F", uri);
        Assert.Contains("body=H%26cc%3Devil%40x.com", uri);
        Assert.Equal(2, uri.Count(c => c == '&') + 1); // exactly one separator between subject and body
    }

    [Fact]
    public void Long_drafts_are_cut_at_a_whole_note_and_say_so()
    {
        var draft = Draft(items: 60);

        var link = MailtoLink.Build(null, draft, maxLength: 1000);

        Assert.True(link.Truncated);
        Assert.InRange(link.Uri.Length, 1, 1000);
        Assert.InRange(link.Included, 1, 59);
        Assert.Equal(60, link.Total);
        var body = Uri.UnescapeDataString(link.Uri[(link.Uri.IndexOf("&body=", StringComparison.Ordinal) + 6)..]);
        Assert.Contains($"[{60 - link.Included} more items not included", body);
        Assert.Contains($"{link.Included}. xxxx", body);
        Assert.DoesNotContain($"{link.Included + 1}. xxxx", body);
    }

    [Fact]
    public void One_item_over_the_limit_reads_as_singular()
    {
        var draft = Draft(items: 2, itemLength: 300);
        var link = MailtoLink.Build(null, draft, maxLength: 600);
        Assert.Equal(1, link.Included);
        Assert.Contains("%5B1%20more%20item%20not%20included", link.Uri);
    }

    [Fact]
    public void A_single_note_that_cannot_fit_still_yields_a_usable_link()
    {
        var link = MailtoLink.Build(null, Draft(items: 1, itemLength: 5000), maxLength: 300);
        Assert.InRange(link.Uri.Length, 1, 300);
        Assert.Equal(0, link.Included);
    }

    [Fact]
    public void Default_limit_keeps_links_under_what_outlook_accepts()
    {
        var link = MailtoLink.Build("someone@example.com", Draft(items: 200, itemLength: 120));
        Assert.True(link.Uri.Length <= MailtoLink.DefaultMaxLength);
        Assert.True(MailtoLink.DefaultMaxLength < 2083);
    }
}
