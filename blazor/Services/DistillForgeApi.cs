using System.Net;
using System.Net.Http.Json;
using DistillForge.Core.Contracts;

namespace DistillForge.Blazor.Services;

public sealed class ApiException(string message) : Exception(message);

/// <summary>Typed client for the Functions API. The browser never talks to the database or the AI provider.</summary>
public sealed class DistillForgeApi(HttpClient http)
{
    public async Task<List<WorkspaceSummaryDto>> ListWorkspacesAsync(bool includeArchived = false, CancellationToken ct = default) =>
        await Send<List<WorkspaceSummaryDto>>(() => http.GetAsync($"workspaces?includeArchived={(includeArchived ? "true" : "false")}", ct), ct) ?? [];

    public async Task SetArchivedAsync(Guid id, bool archived, CancellationToken ct = default) =>
        await Send<WorkspaceSummaryDto>(() => http.PostAsync($"workspaces/{id}/{(archived ? "archive" : "unarchive")}", null, ct), ct);

    public async Task<ExtractionRunDto?> GetLatestRunAsync(Guid workspaceId, CancellationToken ct = default) =>
        (await Send<List<ExtractionRunDto>>(() => http.GetAsync($"workspaces/{workspaceId}/runs", ct), ct))?.FirstOrDefault();

    public Task<WorkspaceDto?> GetWorkspaceAsync(Guid id, CancellationToken ct = default) =>
        Send<WorkspaceDto>(() => http.GetAsync($"workspaces/{id}", ct), ct);

    public async Task<WorkspaceDto> CreateWorkspaceAsync(CreateWorkspaceRequest request, CancellationToken ct = default) =>
        (await Send<WorkspaceDto>(() => http.PostAsJsonAsync("workspaces", request, ct), ct))!;

    public async Task<WorkspaceDto> ReplaceSourceAsync(Guid id, ReplaceSourceRequest request, CancellationToken ct = default) =>
        (await Send<WorkspaceDto>(() => http.PutAsJsonAsync($"workspaces/{id}/source", request, ct), ct))!;

    public async Task<List<ActionItemDto>> GetActionsAsync(Guid workspaceId, CancellationToken ct = default) =>
        await Send<List<ActionItemDto>>(() => http.GetAsync($"workspaces/{workspaceId}/actions", ct), ct) ?? [];

    public async Task<ActionItemDto> UpdateActionAsync(Guid id, UpdateActionItemRequest request, CancellationToken ct = default) =>
        await Send<ActionItemDto>(() => http.PutAsJsonAsync($"actions/{id}", request, ct), ct)
        ?? throw new ApiException("That note no longer exists (the workspace may have been re-extracted).");

    public async Task<List<CategoryDto>> GetCategoriesAsync(Guid workspaceId, CancellationToken ct = default) =>
        await Send<List<CategoryDto>>(() => http.GetAsync($"workspaces/{workspaceId}/categories", ct), ct) ?? [];

    public async Task<CategoryDto> CreateCategoryAsync(Guid workspaceId, CategoryRequest request, CancellationToken ct = default) =>
        (await Send<CategoryDto>(() => http.PostAsJsonAsync($"workspaces/{workspaceId}/categories", request, ct), ct))!;

    public async Task<CategoryDto> UpdateCategoryAsync(Guid id, CategoryRequest request, CancellationToken ct = default) =>
        await Send<CategoryDto>(() => http.PutAsJsonAsync($"categories/{id}", request, ct), ct)
        ?? throw new ApiException("That category no longer exists.");

    public async Task DeleteCategoryAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await http.DeleteAsync($"categories/{id}", ct);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound) throw await ToException(response, ct);
    }

    public async Task<ActionItemDto> MoveActionAsync(Guid id, Guid? categoryId, CancellationToken ct = default) =>
        await Send<ActionItemDto>(() => http.PutAsJsonAsync($"actions/{id}/category", new MoveActionItemRequest(categoryId), ct), ct)
        ?? throw new ApiException("That note no longer exists (the workspace may have been re-extracted).");

    /// <summary>Starts a run (202), or returns the run already in progress (409).</summary>
    public async Task<ExtractionRunDto> StartExtractionAsync(Guid workspaceId, CancellationToken ct = default)
    {
        using var response = await http.PostAsync($"workspaces/{workspaceId}/extract", null, ct);
        if (response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.Conflict)
            return (await response.Content.ReadFromJsonAsync<ExtractionRunDto>(ct))!;
        throw await ToException(response, ct);
    }

    public Task<ExtractionRunDto?> GetRunAsync(Guid runId, CancellationToken ct = default) =>
        Send<ExtractionRunDto>(() => http.GetAsync($"runs/{runId}", ct), ct);

    private static async Task<T?> Send<T>(Func<Task<HttpResponseMessage>> call, CancellationToken ct)
    {
        HttpResponseMessage response;
        try { response = await call(); }
        catch (HttpRequestException ex) { throw new ApiException($"Could not reach the API ({ex.Message}). Is `func start` running?"); }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound) return default;
            if (!response.IsSuccessStatusCode) throw await ToException(response, ct);
            return await response.Content.ReadFromJsonAsync<T>(ct);
        }
    }

    private static async Task<ApiException> ToException(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        return new ApiException($"API error {(int)response.StatusCode}: {(string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body.Trim('"'))}");
    }
}
