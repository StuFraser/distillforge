namespace DistillForge.Blazor.Services;

public enum DialogStyle { Primary, Secondary, Danger }

/// <param name="Value">What <see cref="DialogService.ShowAsync"/> returns when this button is chosen.</param>
/// <param name="IsCancel">Chosen by Escape and by clicking outside the dialog.</param>
/// <param name="IsDefault">Receives focus when the dialog opens.</param>
public sealed record DialogButton(string Label, string Value, DialogStyle Style = DialogStyle.Secondary, bool IsCancel = false, bool IsDefault = false);

/// <param name="Message">Plain text (HTML-encoded on render, so note titles etc. are safe); newlines are preserved.</param>
public sealed record DialogOptions(string Title, string Message, IReadOnlyList<DialogButton> Buttons);

/// <summary>
/// Replacement for the browser's confirm()/alert(). Await <see cref="ShowAsync"/> from any component; the
/// <c>DialogHost</c> in the main layout renders it. One dialog at a time: extra requests wait their turn.
/// </summary>
public sealed class DialogService
{
    public const string ConfirmValue = "confirm";
    public const string CancelValue = "cancel";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private TaskCompletionSource<string?>? _result;

    public DialogOptions? Current { get; private set; }
    public event Action? Changed;

    /// <summary>Shows a dialog and returns the chosen button's Value (null if dismissed with no cancel button defined).</summary>
    public async Task<string?> ShowAsync(DialogOptions options)
    {
        await _gate.WaitAsync();
        try
        {
            _result = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Current = options;
            Changed?.Invoke();
            return await _result.Task;
        }
        finally
        {
            Current = null;
            _result = null;
            Changed?.Invoke();
            _gate.Release();
        }
    }

    /// <summary>Called by the host when a button is chosen or the dialog is dismissed.</summary>
    public void Complete(string? value) => _result?.TrySetResult(value);

    /// <summary>OK/Cancel question. Set <paramref name="danger"/> for destructive actions (red confirm button, Cancel focused).</summary>
    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "OK", string cancelText = "Cancel", bool danger = false) =>
        await ShowAsync(new DialogOptions(title, message,
        [
            new DialogButton(cancelText, CancelValue, DialogStyle.Secondary, IsCancel: true, IsDefault: danger),
            new DialogButton(confirmText, ConfirmValue, danger ? DialogStyle.Danger : DialogStyle.Primary, IsDefault: !danger),
        ])) == ConfirmValue;

    public Task AlertAsync(string title, string message, string okText = "OK") =>
        ShowAsync(new DialogOptions(title, message, [new DialogButton(okText, ConfirmValue, DialogStyle.Primary, IsCancel: true, IsDefault: true)]));
}
