using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using WitchDrawer.App.Infrastructure;

namespace WitchDrawer.App.Views;

/// <summary>
/// Small themed modal dialog used instead of the native Windows message box so
/// that every confirmation and notification keeps the app's glass look.
/// </summary>
internal partial class AppDialogWindow : Window
{
    internal enum DialogIcon
    {
        None,
        Information,
        Warning,
        Question,
        Error
    }

    internal sealed class DialogState : INotifyPropertyChanged
    {
        private string _title = string.Empty;
        private string _message = string.Empty;
        private DialogIcon _iconKind = DialogIcon.None;
        private string _glyph = string.Empty;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Title
        {
            get => _title;
            set => Set(ref _title, value);
        }

        public string Message
        {
            get => _message;
            set => Set(ref _message, value);
        }

        public DialogIcon IconKind
        {
            get => _iconKind;
            set => Set(ref _iconKind, value);
        }

        public string Glyph
        {
            get => _glyph;
            set => Set(ref _glyph, value);
        }

        private void Set<T>(
            ref T field,
            T value,
            [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
    }

    private readonly DialogState _state = new();

    internal AppDialogWindow(
        string message,
        string title,
        MessageBoxButton button,
        MessageBoxImage image)
    {
        InitializeComponent();

        _state.Title = string.IsNullOrWhiteSpace(title) ? "PODO" : title;
        _state.Message = message ?? string.Empty;
        _state.IconKind = MapIcon(image);
        _state.Glyph = MapGlyph(_state.IconKind);
        DataContext = _state;

        ApplyButtons(button);

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>Gets the dialog result mapped from the native message box result.</summary>
    internal MessageBoxResult Result { get; private set; } = MessageBoxResult.Cancel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        AppThemeManager.ApplyToWindow(this, WindowBackdropKind.Transient);

        // A dialog without an owner still needs to sit above the main window.
        if (Owner is null && Application.Current?.MainWindow is { IsLoaded: true } mainWindow
            && !ReferenceEquals(mainWindow, this))
        {
            Owner = mainWindow;
        }

        AffirmativeButton.Focus();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        SourceInitialized -= OnSourceInitialized;
        if (Owner is null)
        {
            // Keep the dialog inside the work area even without an owner.
            Left = SystemParameters.WorkArea.Left
                + Math.Max(0, (SystemParameters.WorkArea.Width - ActualWidth) / 2);
            Top = SystemParameters.WorkArea.Top
                + Math.Max(0, (SystemParameters.WorkArea.Height - ActualHeight) / 2);
        }
    }

    private void ApplyButtons(MessageBoxButton button)
    {
        switch (button)
        {
            case MessageBoxButton.OKCancel:
                AffirmativeButton.Content = "确定";
                NegativeButton.Content = "取消";
                NegativeButton.Visibility = Visibility.Visible;
                break;
            case MessageBoxButton.YesNo:
                AffirmativeButton.Content = "是";
                AffirmativeButton.Style = (Style)FindResource("DialogDangerButtonStyle");
                NegativeButton.Content = "否";
                NegativeButton.Visibility = Visibility.Visible;
                break;
            case MessageBoxButton.YesNoCancel:
                AffirmativeButton.Content = "是";
                AffirmativeButton.Style = (Style)FindResource("DialogDangerButtonStyle");
                NegativeButton.Content = "否";
                NegativeButton.Visibility = Visibility.Visible;
                CancelButton.Content = "取消";
                CancelButton.Visibility = Visibility.Visible;
                break;
            default:
                AffirmativeButton.Content = "确定";
                break;
        }
    }

    private static DialogIcon MapIcon(MessageBoxImage image) => image switch
    {
        MessageBoxImage.Information => DialogIcon.Information,
        MessageBoxImage.Warning => DialogIcon.Warning,
        MessageBoxImage.Question => DialogIcon.Question,
        MessageBoxImage.Error => DialogIcon.Error,
        _ => DialogIcon.None
    };

    private static string MapGlyph(DialogIcon icon) => icon switch
    {
        DialogIcon.Information => "\uE946",
        DialogIcon.Warning => "\uE7BA",
        DialogIcon.Question => "\uE897",
        DialogIcon.Error => "\uEA39",
        _ => string.Empty
    };

    private void OnAffirmativeClick(object sender, RoutedEventArgs e)
    {
        Result = AffirmativeButton.Content as string == "是"
            ? MessageBoxResult.Yes
            : MessageBoxResult.OK;
        DialogResult = true;
    }

    private void OnNegativeClick(object sender, RoutedEventArgs e)
    {
        Result = NegativeButton.Content as string == "否"
            ? MessageBoxResult.No
            : MessageBoxResult.Cancel;
        DialogResult = false;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Cancel;
        DialogResult = null;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && CancelButton.Visibility != Visibility.Visible)
        {
            Result = NegativeButton.Visibility == Visibility.Visible
                ? MessageBoxResult.No
                : MessageBoxResult.Cancel;
            DialogResult = NegativeButton.Visibility == Visibility.Visible ? false : null;
            e.Handled = true;
        }
    }
}
