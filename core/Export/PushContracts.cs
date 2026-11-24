using DistillForge.Core.Integrations;

namespace DistillForge.Core.Export;

/// <summary>One note as an export target sees it. Built on the server from the stored note, never taken from the client.</summary>
public sealed record ExportItem(
    Guid NoteId, string Title, string? Summary, string? Owner, string? Due,
    string? SourceQuote, string? SourceLabel, string? CategoryName, string WorkspaceTitle);

/// <summary>What happened to one note in a push. Ok=false carries a message the user can act on.</summary>
public sealed record PushOutcome(Guid NoteId, bool Ok, string? ExternalId, string? Url, string? Error);

/// <summary>What the push dialog offers for a connection, fetched live (e.g. the work item types of the Azure DevOps project).</summary>
public sealed record PushOptionsDto(IReadOnlyList<string> ItemTypes, string? DefaultItemType, string? Error);

/// <summary>
/// Push the given notes of a workspace through a connection. Notes are looked up server-side; merged notes are ignored.
/// Notes already pushed through this connection are skipped unless <see cref="Repush"/>.
/// </summary>
public sealed record PushRequest(Guid WorkspaceId, IReadOnlyList<Guid> NoteIds, string ItemType, bool IncludeSource = true, bool Repush = false);

public sealed record PushResponse(IReadOnlyList<PushOutcome> Outcomes);

public sealed record NotePushDto(Guid NoteId, Guid IntegrationId, string ExternalId, string? Url, string ItemType, DateTimeOffset PushedAt);

public static class PushSupport
{
    /// <summary>Most notes in one push request (they are created one by one, within the HTTP time limit).</summary>
    public const int MaxNotes = 50;

    /// <summary>Platforms that have a push implementation. The rest can be connected but not pushed to yet.</summary>
    public static bool IsSupported(IntegrationPlatform p) => p == IntegrationPlatform.AzureDevOps;
}
