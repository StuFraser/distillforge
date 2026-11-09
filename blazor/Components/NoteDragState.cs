namespace DistillForge.Blazor.Components;

/// <summary>
/// Transient UI state for dragging notes: which note is being dragged, which drop target (a category tab, the category body or
/// the Uncategorised panel) is highlighted, and which note is hovered as a merge target. Owned by the page, which shares it with
/// the note grids; the page decides what a drop means and whether editing is allowed.
/// </summary>
public sealed class NoteDragState
{
    public const string UncategorisedTarget = "uncategorised";
    public const string BodyTarget = "body";

    public Guid? DragId { get; private set; }
    /// <summary>The highlighted drop target: <see cref="UncategorisedTarget"/>, <see cref="BodyTarget"/> or a category id as a string.</summary>
    public string? DropTarget { get; private set; }
    public Guid? MergeHoverId { get; private set; }

    public bool IsDragging => DragId is not null;

    public void Begin(Guid noteId) => DragId = noteId;

    /// <summary>Clears everything and returns the note that was being dragged. Drops call this first, because the dragged
    /// card can vanish (merge, move) and then <c>dragend</c> never fires.</summary>
    public Guid? End()
    {
        var id = DragId;
        DragId = null;
        DropTarget = null;
        MergeHoverId = null;
        return id;
    }

    public void Over(string target) { if (IsDragging) DropTarget = target; }

    public void Leave(string target) { if (DropTarget == target) DropTarget = null; }

    public void HoverNote(Guid noteId) => MergeHoverId = noteId;

    public void LeaveNote(Guid noteId) { if (MergeHoverId == noteId) MergeHoverId = null; }

    /// <summary>The dragged note is a different note from <paramref name="noteId"/>, so it could be merged into it.</summary>
    public bool CanMergeInto(Guid noteId) => DragId is Guid d && d != noteId;
}
