using System.Windows;
using WitchDrawer.App.Views;

namespace WitchDrawer.App;

/// <summary>
/// Shows the app's themed dialogs instead of the native Windows message box so that
/// confirmations and warnings match the dark glass language used by every window.
/// </summary>
internal static class AppDialog
{
    /// <summary>Shows a dialog and returns true when the user picks the affirmative action.</summary>
    public static bool Show(
        string message,
        string title = "PODO",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
        => Show(owner: null, message, title, button, image);

    /// <summary>Shows a dialog owned by <paramref name="owner"/>.</summary>
    public static bool Show(
        Window? owner,
        string message,
        string title = "PODO",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = new AppDialogWindow(message, title, button, image);
        if (owner is not null && owner.IsLoaded && !ReferenceEquals(owner, dialog))
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
        return dialog.Result is MessageBoxResult.OK or MessageBoxResult.Yes;
    }

    /// <summary>Asks a yes/no question. Returns true when the user answers yes.</summary>
    public static bool Confirm(
        Window? owner,
        string message,
        string title = "确认")
        => Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);

    /// <summary>Asks a yes/no question. Returns true when the user answers yes.</summary>
    public static bool Confirm(
        string message,
        string title = "确认")
        => Show(null, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);

    /// <summary>Shows an error message.</summary>
    public static void Error(
        Window? owner,
        string message,
        string title = "出错了")
        => Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>Shows a warning message.</summary>
    public static void Warn(
        Window? owner,
        string message,
        string title = "PODO")
        => Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>Shows an informational message.</summary>
    public static void Info(
        Window? owner,
        string message,
        string title = "PODO")
        => Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
}
