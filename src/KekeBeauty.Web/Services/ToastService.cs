namespace KekeBeauty.Web.Services;

public enum ToastSeverity { Success, Error, Warning, Info }

public sealed record ToastMessage(Guid Id, string Text, ToastSeverity Severity);

/// <summary>Notifications transitoires globales (toasts), affichees dans une zone flottante
/// toujours visible (voir ToastContainer.razor, monte une seule fois dans MainLayout) plutot
/// que dans des bandeaux inline au fil des pages qui finissaient hors champ en bas d'ecran.</summary>
public sealed class ToastService
{
    private const int AutoDismissMs = 5000;
    private readonly List<ToastMessage> _toasts = [];

    public event Action? OnChange;

    public IReadOnlyList<ToastMessage> Toasts => _toasts;

    public void ShowSuccess(string message) => Show(message, ToastSeverity.Success);
    public void ShowError(string message) => Show(message, ToastSeverity.Error);
    public void ShowWarning(string message) => Show(message, ToastSeverity.Warning);
    public void ShowInfo(string message) => Show(message, ToastSeverity.Info);

    public void Show(string message, ToastSeverity severity)
    {
        var toast = new ToastMessage(Guid.NewGuid(), message, severity);
        _toasts.Add(toast);
        OnChange?.Invoke();
        _ = AutoDismissAsync(toast.Id);
    }

    public void Dismiss(Guid id)
    {
        if (_toasts.RemoveAll(t => t.Id == id) > 0)
        {
            OnChange?.Invoke();
        }
    }

    private async Task AutoDismissAsync(Guid id)
    {
        await Task.Delay(AutoDismissMs);
        Dismiss(id);
    }
}
