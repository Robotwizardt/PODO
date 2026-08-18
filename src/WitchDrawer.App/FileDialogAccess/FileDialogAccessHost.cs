using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Services;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.FileDialogAccess;

internal sealed class FileDialogAccessHost : IDisposable
{
    private readonly DrawerService _drawerService;
    private readonly IAppLogger _logger;
    private readonly FileDialogAccessSettingsStore _settingsStore;
    private readonly FileDialogAccessViewModel _viewModel;
    private readonly DispatcherTimer _availabilityTimer;
    private FileDialogAccessSettings _settings = FileDialogAccessSettings.Default;
    private FileDialogWindowMonitor? _monitor;
    private FileDialogAccessWindow? _window;
    private FileDialogWindowInfo? _activeDialog;
    private nint _windowHandle;
    private nint _hiddenSessionDialog;
    private bool _applyingBounds;
    private bool _hasUnavailableEntries;
    private bool _disposed;

    public FileDialogAccessHost(
        DrawerService drawerService,
        IAppLogger logger,
        FileDialogAccessSettingsStore settingsStore)
    {
        _drawerService = drawerService;
        _logger = logger;
        _settingsStore = settingsStore;
        _viewModel = new FileDialogAccessViewModel(NavigateAsync);
        _availabilityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _availabilityTimer.Tick += OnAvailabilityTimerTick;
        _viewModel.RefreshRequested += OnRefreshRequested;
        _viewModel.CloseRequested += OnCloseRequested;
        _viewModel.BlacklistRequested += OnBlacklistRequested;
        _settingsStore.SettingsChanged += OnSettingsChanged;
    }

    public async Task InitializeAsync()
    {
        _settings = await _settingsStore.LoadAsync();
        ApplyEnabledState();
    }

    public Task RefreshAsync() => ReloadEntriesAsync();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settingsStore.SettingsChanged -= OnSettingsChanged;
        _availabilityTimer.Stop();
        _availabilityTimer.Tick -= OnAvailabilityTimerTick;
        _viewModel.RefreshRequested -= OnRefreshRequested;
        _viewModel.CloseRequested -= OnCloseRequested;
        _viewModel.BlacklistRequested -= OnBlacklistRequested;
        StopMonitor();
        if (_window is not null)
        {
            _window.SizeChanged -= OnWindowSizeChanged;
            _window.Close();
            _window = null;
        }
    }

    private void ApplyEnabledState()
    {
        if (_settings.IsEnabled)
        {
            if (_monitor is null)
            {
                _monitor = new FileDialogWindowMonitor();
                _monitor.WindowChanged += OnWindowChanged;
                _monitor.EvaluateForeground();
            }
        }
        else
        {
            StopMonitor();
            HideWindow();
        }
    }

    private void StopMonitor()
    {
        if (_monitor is null)
        {
            return;
        }

        _monitor.WindowChanged -= OnWindowChanged;
        _monitor.Dispose();
        _monitor = null;
        _activeDialog = null;
    }

    private void OnSettingsChanged(object? sender, FileDialogAccessSettings settings)
    {
        Dispatch(async () =>
        {
            _settings = settings;
            ApplyEnabledState();
            await ReloadEntriesAsync();
            Reposition();
        });
    }

    private void OnWindowChanged(object? sender, FileDialogWindowChangedEventArgs e)
    {
        Dispatch(() => HandleWindowChangeAsync(e));
    }

    private async Task HandleWindowChangeAsync(FileDialogWindowChangedEventArgs e)
    {
        if (_disposed || !_settings.IsEnabled)
        {
            return;
        }

        switch (e.Kind)
        {
            case FileDialogWindowChangeKind.Activated when e.Dialog is not null:
                if (!CanShowFor(e.Dialog))
                {
                    HideWindow();
                    return;
                }

                if (_activeDialog?.Handle != e.Dialog.Handle)
                {
                    _hiddenSessionDialog = nint.Zero;
                }

                _activeDialog = e.Dialog;
                if (_hiddenSessionDialog == e.Dialog.Handle || e.Dialog.IsMinimized)
                {
                    HideWindow();
                    return;
                }

                await ReloadEntriesAsync();
                ShowWindow();
                break;

            case FileDialogWindowChangeKind.Updated:
                if (e.Dialog is null || e.Dialog.IsMinimized)
                {
                    HideWindow();
                    return;
                }

                _activeDialog = e.Dialog;
                if (_hiddenSessionDialog != e.Dialog.Handle)
                {
                    ShowWindow();
                }
                break;

            case FileDialogWindowChangeKind.ForegroundChanged:
                if (e.WindowHandle != _windowHandle
                    && e.WindowHandle != _activeDialog?.Handle)
                {
                    HideWindow();
                }
                break;

            case FileDialogWindowChangeKind.Closed:
                if (_activeDialog?.Handle == e.WindowHandle)
                {
                    HideWindow();
                    _activeDialog = null;
                    _hiddenSessionDialog = nint.Zero;
                }
                break;
        }
    }

    private bool CanShowFor(FileDialogWindowInfo dialog)
    {
        if (!WindowProcessAccess.CanInteractWith(dialog.ProcessId))
        {
            _logger.Info($"Skipped elevated or inaccessible file dialog from process {dialog.ProcessId}.");
            return false;
        }

        return string.IsNullOrWhiteSpace(dialog.ProcessPath)
            || !_settings.BlacklistedApplications.Contains(
                dialog.ProcessPath,
                StringComparer.OrdinalIgnoreCase);
    }

    private async Task ReloadEntriesAsync()
    {
        if (_activeDialog is null)
        {
            return;
        }

        try
        {
            var boxes = await _drawerService.GetBoxesAsync();
            var entries = await Task.Run(() => FileDialogAccessCatalog.CreateEntries(boxes));
            _hasUnavailableEntries = entries.Any(entry => !entry.IsAvailable);
            _viewModel.Load(entries, _settings.RecentBoxIds);
            _viewModel.ErrorText = null;
            UpdateAvailabilityTimer();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load boxes for the file dialog access window.");
            _viewModel.ErrorText = "无法刷新收纳盒列表";
        }
    }

    private void ShowWindow()
    {
        if (_activeDialog is null || _hiddenSessionDialog == _activeDialog.Handle)
        {
            return;
        }

        EnsureWindow();
        if (!_window!.IsVisible)
        {
            _window.Show();
        }

        UpdateAvailabilityTimer();
        Reposition();
    }

    private void EnsureWindow()
    {
        if (_window is not null)
        {
            return;
        }

        _window = new FileDialogAccessWindow(_viewModel)
        {
            Width = _settings.Width
        };
        _window.SourceInitialized += (_, _) =>
        {
            _windowHandle = new WindowInteropHelper(_window).Handle;
            FileDialogWindowInterop.ConfigureToolWindow(_windowHandle);
        };
        _window.SizeChanged += OnWindowSizeChanged;
    }

    private void Reposition()
    {
        if (_window is null
            || !_window.IsVisible
            || _activeDialog is null
            || _windowHandle == nint.Zero
            || !FileDialogWindowInterop.TryGetWorkArea(_activeDialog.Handle, out var nativeWorkArea))
        {
            return;
        }

        var dpiScale = FileDialogWindowInterop.GetWindowDpi(_activeDialog.Handle) / 96d;
        var preferredWidth = (int)Math.Round(_settings.Width * dpiScale);
        var placement = FileDialogAccessPlacement.Calculate(
            ToAppRect(_activeDialog.Bounds),
            ToAppRect(nativeWorkArea),
            preferredWidth,
            (int)Math.Round(88 * dpiScale));
        _applyingBounds = true;
        try
        {
            _ = FileDialogWindowInterop.SetBounds(
                _windowHandle,
                new NativeScreenRect(
                    placement.Bounds.Left,
                    placement.Bounds.Top,
                    placement.Bounds.Right,
                    placement.Bounds.Bottom));
        }
        finally
        {
            _applyingBounds = false;
        }
    }

    private async Task NavigateAsync(FileDialogAccessEntry entry)
    {
        if (_activeDialog is null || !entry.IsAvailable)
        {
            return;
        }

        _viewModel.IsNavigating = true;
        _viewModel.ErrorText = null;
        try
        {
            var result = await FileDialogNavigator.NavigateToDirectoryAsync(
                _activeDialog.Handle,
                entry.StoragePath);
            if (!result.Succeeded)
            {
                _viewModel.ErrorText = result.ErrorMessage;
                _logger.Info($"File dialog navigation was refused: {result.ErrorMessage}");
                return;
            }

            _settings = _settings.RecordRecentBox(entry.BoxId);
            await _settingsStore.SaveAsync(_settings);
            _viewModel.Load(
                FileDialogAccessCatalog.Search(
                    FileDialogAccessCatalog.CreateEntries(await _drawerService.GetBoxesAsync()),
                    string.Empty),
                _settings.RecentBoxIds);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "File dialog navigation failed.");
            _viewModel.ErrorText = "无法切换到收纳盒目录";
        }
        finally
        {
            _viewModel.IsNavigating = false;
            Reposition();
        }
    }

    private void OnRefreshRequested(object? sender, EventArgs e)
    {
        Dispatch(ReloadEntriesAsync);
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        if (_activeDialog is not null)
        {
            _hiddenSessionDialog = _activeDialog.Handle;
        }

        HideWindow();
    }

    private void OnBlacklistRequested(object? sender, EventArgs e)
    {
        if (_activeDialog is null || string.IsNullOrWhiteSpace(_activeDialog.ProcessPath))
        {
            return;
        }

        Dispatch(async () =>
        {
            _settings = _settings with
            {
                BlacklistedApplications = _settings.BlacklistedApplications
                    .Append(_activeDialog.ProcessPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
            await _settingsStore.SaveAsync(_settings);
            HideWindow();
        });
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_applyingBounds || !e.WidthChanged || _window is null || !_window.IsVisible)
        {
            return;
        }

        var width = Math.Clamp(_window.ActualWidth, 240, 520);
        if (Math.Abs(width - _settings.Width) < 1)
        {
            return;
        }

        _settings = _settings with { Width = width };
        Dispatch(() => _settingsStore.SaveAsync(_settings));
    }

    private void HideWindow()
    {
        _availabilityTimer.Stop();
        if (_window?.IsVisible == true)
        {
            _window.Hide();
        }
    }

    private void UpdateAvailabilityTimer()
    {
        if (_hasUnavailableEntries && _window?.IsVisible == true)
        {
            _availabilityTimer.Start();
        }
        else
        {
            _availabilityTimer.Stop();
        }
    }

    private async void OnAvailabilityTimerTick(object? sender, EventArgs e)
    {
        await ReloadEntriesAsync();
    }

    private void Dispatch(Func<Task> action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        _ = dispatcher.InvokeAsync(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "File dialog access window event failed.");
            }
        });
    }

    private static FileDialogScreenRect ToAppRect(NativeScreenRect rect) => new(
        rect.Left,
        rect.Top,
        rect.Right,
        rect.Bottom);
}
