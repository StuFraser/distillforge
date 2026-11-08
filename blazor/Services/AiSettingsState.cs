namespace DistillForge.Blazor.Services;

/// <summary>Lets the banner's settings dialog (in the layout) tell the page the model list changed.</summary>
public sealed class AiSettingsState
{
    public event Action? Changed;
    public void NotifyChanged() => Changed?.Invoke();
}
