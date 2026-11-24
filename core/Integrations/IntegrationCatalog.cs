namespace DistillForge.Core.Integrations;

/// <summary>Platforms notes can be pushed to. Stored by name, so don't rename members.</summary>
public enum IntegrationPlatform
{
    GitHub,
    Trello,
    AzureDevOps,
    Jira,
}

public enum FieldKind
{
    Text,
    Url,
    /// <summary>Write-only: encrypted on the server and never sent back to the client.</summary>
    Secret,
    Select,
}

/// <param name="Key">Stable identifier used in storage and requests.</param>
/// <param name="Options">For <see cref="FieldKind.Select"/>: the allowed values.</param>
/// <param name="Default">Pre-filled on new connections; also used for Select fields left empty.</param>
public sealed record FieldDef(
    string Key,
    string Label,
    FieldKind Kind = FieldKind.Text,
    bool Required = true,
    string? Help = null,
    string? Placeholder = null,
    string? Default = null,
    IReadOnlyList<string>? Options = null)
{
    public bool IsSecret => Kind == FieldKind.Secret;
}

public sealed record PlatformDef(
    IntegrationPlatform Platform,
    string Name,
    string Description,
    IReadOnlyList<FieldDef> Fields,
    string? HelpUrl = null);

/// <summary>
/// What each platform needs from the user. The Blazor form is generated from this and the API validates against it, so
/// adding a platform's configuration is one entry here (the push implementation is a separate piece).
/// </summary>
public static class IntegrationCatalog
{
    public static IReadOnlyList<PlatformDef> All { get; } =
    [
        new(IntegrationPlatform.GitHub, "GitHub Issues", "Create an issue in a repository for each note.",
        [
            new("owner", "Owner", Help: "The user or organisation that owns the repository.", Placeholder: "octocat"),
            new("repo", "Repository", Placeholder: "hello-world"),
            new("token", "Personal access token", FieldKind.Secret,
                Help: "A fine-grained token with Issues: read and write on this repository."),
            new("labels", "Default labels", Required: false, Help: "Comma-separated; applied to every issue.", Placeholder: "distillforge, triage"),
        ], "https://github.com/settings/personal-access-tokens"),

        new(IntegrationPlatform.Trello, "Trello", "Create a card on a list for each note.",
        [
            new("apiKey", "API key", FieldKind.Secret, Help: "From the Trello Power-Up admin page."),
            new("token", "Token", FieldKind.Secret, Help: "Authorised with write access to your boards."),
            new("listId", "List ID", Help: "The list new cards are added to.", Placeholder: "5f1c…"),
        ], "https://trello.com/power-ups/admin"),

        new(IntegrationPlatform.AzureDevOps, "Azure DevOps", "Create a work item in a project for each note.",
        [
            new("organizationUrl", "Organisation URL", FieldKind.Url, Placeholder: "https://dev.azure.com/your-org"),
            new("project", "Project", Placeholder: "My Project"),
            new("workItemType", "Work item type", FieldKind.Select, Default: "Task", Options: ["Task", "Bug", "Issue", "User Story", "Product Backlog Item"],
                Help: "Must exist in the project's process template."),
            new("pat", "Personal access token", FieldKind.Secret, Help: "Needs the Work Items (Read & write) scope."),
            new("areaPath", "Area path", Required: false, Help: "Leave blank for the project root."),
        ]),

        new(IntegrationPlatform.Jira, "Jira", "Create an issue in a project for each note (Jira Cloud).",
        [
            new("siteUrl", "Site URL", FieldKind.Url, Placeholder: "https://your-site.atlassian.net"),
            new("email", "Account email", Help: "The Atlassian account the API token belongs to."),
            new("apiToken", "API token", FieldKind.Secret),
            new("projectKey", "Project key", Placeholder: "ABC"),
            new("issueType", "Issue type", Default: "Task", Help: "Name of an issue type in the project, e.g. Task, Bug, Story."),
        ], "https://id.atlassian.com/manage-profile/security/api-tokens"),
    ];

    public static PlatformDef Get(IntegrationPlatform platform) => All.First(p => p.Platform == platform);
}
