using DistillForge.Core.Integrations;

namespace DistillForge.Core.Export;

/// <summary>One note as an export target sees it. Built on the server from the stored note, never taken from the client.</summary>
public sealed record ExportItem(
    Guid NoteId, string Title, string? Summary, string? Owner, string? Due,
    string? SourceQuote, string? SourceLabel, string? CategoryName, string WorkspaceTitle);

/// <summary>What happened to one note in a push. Ok=false carries a message the user can act on.</summary>
public sealed record PushOutcome(Guid NoteId, bool Ok, string? ExternalId, string? Url, string? Error);

/// <summary>A choice shown by name but sent by value (Trello boards and lists are chosen by name, addressed by id).</summary>
public sealed record PushOption(string Value, string Label);

/// <summary>
/// What the push dialog offers for a connection, fetched live. <see cref="ItemTypes"/> is what each note becomes (a work item type,
/// a Trello list), worded by <see cref="ItemTypeLabel"/>. Platforms with a level above the item (Azure DevOps projects, Trello boards)
/// set <see cref="ScopeLabel"/>; <see cref="Scopes"/> is only filled when no scope was asked for, and <see cref="Scope"/> is the one
/// the item types belong to.
/// </summary>
public sealed record PushOptionsDto(
    IReadOnlyList<PushOption> ItemTypes, string? DefaultItemType, string? Error,
    string? ScopeLabel = null, IReadOnlyList<PushOption>? Scopes = null, string? Scope = null,
    string ItemTypeLabel = "Create each note as", bool HasAreaPath = false);

/// <summary>What the user chose at push time: the option values from <see cref="PushOptionsDto"/>. AreaPath is optional and only for platforms that have one.</summary>
public sealed record PushChoices(string ItemType, string? Scope, string? AreaPath);

/// <summary>
/// Push the given notes of a workspace through a connection. Notes are looked up server-side; merged notes are ignored.
/// Notes already pushed through this connection (to the same scope) are skipped unless <see cref="Repush"/>.
/// </summary>
public sealed record PushRequest(Guid WorkspaceId, IReadOnlyList<Guid> NoteIds, string ItemType, bool IncludeSource = true, bool Repush = false,
    string? Scope = null, string? AreaPath = null);

public sealed record PushResponse(IReadOnlyList<PushOutcome> Outcomes);

public sealed record NotePushDto(Guid NoteId, Guid IntegrationId, string ExternalId, string? Url, string ItemType, DateTimeOffset PushedAt, string? Scope = null);

public static class PushSupport
{
    /// <summary>Most notes in one push request (they are created one by one, within the HTTP time limit).</summary>
    public const int MaxNotes = 50;

    /// <summary>Platforms that have a push implementation. The rest can be connected but not pushed to yet.</summary>
    public static bool IsSupported(IntegrationPlatform p) => p is IntegrationPlatform.AzureDevOps or IntegrationPlatform.Trello;
}
