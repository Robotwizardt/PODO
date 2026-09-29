using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

/// <summary>
/// 回收站列表中的一条记录。还原/彻底删除都通过 <see cref="RecycleBinService"/> 完成，
/// UI 不直接触碰用户文件。
/// </summary>
public sealed class RecycleBinEntryViewModel
{
    private readonly RecycleEntry _entry;
    private readonly Func<RecycleBinEntryViewModel, Task> _restore;
    private readonly Func<RecycleBinEntryViewModel, Task> _delete;

    public RecycleBinEntryViewModel(
        RecycleEntry entry,
        Func<RecycleBinEntryViewModel, Task> restore,
        Func<RecycleBinEntryViewModel, Task> delete)
    {
        _entry = entry;
        _restore = restore;
        _delete = delete;
        RestoreCommand = new AsyncRelayCommand(() => _restore(this));
        DeleteCommand = new AsyncRelayCommand(() => _delete(this));
    }

    public Guid Id => _entry.Id;

    public string DisplayName => _entry.DisplayName;

    public string BoxName => _entry.BoxName;

    public string OriginalPath => _entry.OriginalPath ?? string.Empty;

    public bool HasOriginalPath => !string.IsNullOrWhiteSpace(_entry.OriginalPath);

    public bool WasDirectory => _entry.WasDirectory;

    public string KindLabel => _entry.WasDirectory ? "文件夹" : "文件";

    public string DeletedAtLabel => FormatTimestamp(_entry.DeletedAtUtc);

    public string ExpiresAtLabel => FormatTimestamp(_entry.ExpiresAtUtc);

    public long SizeBytes => _entry.SizeBytes;

    public int RemainingDays
    {
        get
        {
            var remaining = (_entry.ExpiresAtUtc - DateTimeOffset.UtcNow).TotalDays;
            return remaining <= 0 ? 0 : (int)Math.Ceiling(remaining);
        }
    }

    public bool IsExpiringSoon => RemainingDays <= 3;

    public string RemainingDaysLabel => RemainingDays <= 0 ? "已过期" : $"还剩 {RemainingDays} 天";

    public string TypeGlyph => _entry.WasDirectory ? "\uE8B7" : "\uE8A5";

    public IAsyncRelayCommand RestoreCommand { get; }

    public IAsyncRelayCommand DeleteCommand { get; }

    private static string FormatTimestamp(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}

/// <summary>
/// 回收站页视图模型：列出记录、还原、彻底删除、全部清空。
/// </summary>
public sealed class RecycleBinViewModel : ObservableObject
{
    private readonly RecycleBinService _recycleBinService;
    private readonly IAppLogger _logger;
    private readonly Func<RecycleBinEntryViewModel, bool> _confirmItemDeletion;
    private readonly Func<bool> _confirmEmptyAll;
    private string? _statusText;
    private string? _errorText;
    private bool _isBusy;

    public RecycleBinViewModel(
        RecycleBinService recycleBinService,
        IAppLogger logger,
        Func<RecycleBinEntryViewModel, bool>? confirmItemDeletion = null,
        Func<bool>? confirmEmptyAll = null)
    {
        _recycleBinService = recycleBinService;
        _logger = logger;
        _confirmItemDeletion = confirmItemDeletion ?? (_ => true);
        _confirmEmptyAll = confirmEmptyAll ?? (() => true);

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        RestoreCommand = new AsyncRelayCommand<RecycleBinEntryViewModel?>(RestoreAsync);
        DeleteCommand = new AsyncRelayCommand<RecycleBinEntryViewModel?>(DeleteAsync);
        EmptyCommand = new AsyncRelayCommand(EmptyAsync);
    }

    /// <summary>
    /// 还原成功后触发，参数是被还原条目原来所属的盒子（可能为 null 表示落到了磁盘）。
    /// </summary>
    public event EventHandler<Guid?>? ItemsChanged;

    /// <summary>
    /// 列表内容发生变化后触发，供导航角标刷新。
    /// </summary>
    public event EventHandler? EntriesChanged;

    public ObservableCollection<RecycleBinEntryViewModel> Entries { get; } = [];

    public bool HasEntries => Entries.Count > 0;

    public string? StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string? ErrorText
    {
        get => _errorText;
        set
        {
            if (SetProperty(ref _errorText, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string SummaryText
    {
        get
        {
            if (Entries.Count == 0)
            {
                return "回收站是空的";
            }

            var totalBytes = Entries.Sum(entry => entry.SizeBytes);
            return $"{Entries.Count} 项 · {FormatSize(totalBytes)}";
        }
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand<RecycleBinEntryViewModel?> RestoreCommand { get; }

    public IAsyncRelayCommand<RecycleBinEntryViewModel?> DeleteCommand { get; }

    public IAsyncRelayCommand EmptyCommand { get; }

    public async Task RefreshAsync()
    {
        try
        {
            var entries = await _recycleBinService.GetEntriesAsync();
            Rebuild(entries);
            ErrorText = null;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load recycle bin entries.");
            ErrorText = "读取回收站失败：" + exception.Message;
        }
    }

    private async Task RestoreAsync(RecycleBinEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (IsBusy)
        {
            return;
        }

        await RunAsync(
            async () =>
            {
                var result = await _recycleBinService.RestoreAsync(entry.Id);
                StatusText = result.StatusMessage;
                if (result.Missing)
                {
                    ErrorText = null;
                }

                var restoredBoxId = result.RestoredIntoBox ? ResolveBoxId(result) : null;
                Entries.Remove(entry);
                RaiseEntriesChanged();
                ItemsChanged?.Invoke(this, restoredBoxId);
            },
            "还原回收站记录");
    }

    private async Task DeleteAsync(RecycleBinEntryViewModel? entry)
    {
        if (entry is null || IsBusy)
        {
            return;
        }

        if (!_confirmItemDeletion(entry))
        {
            return;
        }

        await RunAsync(
            async () =>
            {
                await _recycleBinService.DeleteAsync(entry.Id);
                StatusText = $"已彻底删除 {entry.DisplayName}";
                Entries.Remove(entry);
                RaiseEntriesChanged();
            },
            "彻底删除回收站记录");
    }

    private async Task EmptyAsync()
    {
        if (IsBusy || Entries.Count == 0)
        {
            return;
        }

        if (!_confirmEmptyAll())
        {
            return;
        }

        await RunAsync(
            async () =>
            {
                var removed = await _recycleBinService.EmptyAsync();
                StatusText = removed == 0 ? "回收站已是空的" : $"已彻底删除 {removed} 项";
                Entries.Clear();
                RaiseEntriesChanged();
            },
            "清空回收站");
    }

    private async Task RunAsync(Func<Task> action, string operation)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, operation + " failed.");
            ErrorText = operation + "失败：" + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Rebuild(IReadOnlyList<RecycleEntry> entries)
    {
        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(new RecycleBinEntryViewModel(entry, RestoreAsync, DeleteAsync));
        }

        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(SummaryText));
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseEntriesChanged()
    {
        OnPropertyChanged(nameof(HasEntries));
        OnPropertyChanged(nameof(SummaryText));
        EntriesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Guid? ResolveBoxId(RecycleRestoreResult result)
    {
        return result.Entry.BoxId;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{(long)value} {units[unitIndex]}"
            : $"{value:0.#} {units[unitIndex]}";
    }
}
