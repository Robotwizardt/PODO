using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.FileDialogAccess;

internal sealed class FileDialogAccessItemViewModel
{
    private readonly FileDialogAccessEntry _entry;

    public FileDialogAccessItemViewModel(
        FileDialogAccessEntry entry,
        Func<FileDialogAccessEntry, Task> navigate)
    {
        _entry = entry;
        NavigateCommand = new AsyncRelayCommand(
            () => navigate(_entry),
            () => _entry.IsAvailable);
    }

    public Guid BoxId => _entry.BoxId;

    public string Name => _entry.Name;

    public string StoragePath => _entry.StoragePath;

    public bool IsAvailable => _entry.IsAvailable;

    public string? StatusText => _entry.StatusText;

    public string TypeLabel => _entry.Type switch
    {
        BoxType.Normal => "普通收纳盒",
        BoxType.Pixel => "像素收纳盒",
        BoxType.Drawer => "抽屉收纳盒",
        BoxType.Bound => "目标收纳盒",
        _ => "收纳盒"
    };

    public string TypeGlyph => _entry.Type switch
    {
        BoxType.Drawer => "\uE7C3",
        BoxType.Bound => "\uE8B7",
        _ => "\uE8B7"
    };

    public IAsyncRelayCommand NavigateCommand { get; }
}

internal sealed class FileDialogAccessViewModel : ObservableObject
{
    private readonly Func<FileDialogAccessEntry, Task> _navigate;
    private IReadOnlyList<FileDialogAccessEntry> _allEntries = [];
    private Guid[] _recentBoxIds = [];
    private string _searchText = string.Empty;
    private string? _errorText;
    private bool _isNavigating;

    public FileDialogAccessViewModel(Func<FileDialogAccessEntry, Task> navigate)
    {
        _navigate = navigate;
        RefreshCommand = new RelayCommand(() => RefreshRequested?.Invoke(this, EventArgs.Empty));
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
        BlacklistCommand = new RelayCommand(
            () => BlacklistRequested?.Invoke(this, EventArgs.Empty));
    }

    public event EventHandler? RefreshRequested;

    public event EventHandler? CloseRequested;

    public event EventHandler? BlacklistRequested;

    public ObservableCollection<FileDialogAccessItemViewModel> RecentEntries { get; } = [];

    public ObservableCollection<FileDialogAccessItemViewModel> Entries { get; } = [];

    public IRelayCommand RefreshCommand { get; }

    public IRelayCommand CloseCommand { get; }

    public IRelayCommand BlacklistCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                RebuildLists();
            }
        }
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

    public bool IsNavigating
    {
        get => _isNavigating;
        set => SetProperty(ref _isNavigating, value);
    }

    public bool HasRecentEntries => RecentEntries.Count > 0;

    public bool HasEntries => Entries.Count > 0;

    public void Load(
        IReadOnlyList<FileDialogAccessEntry> entries,
        IReadOnlyList<Guid> recentBoxIds)
    {
        _allEntries = entries;
        _recentBoxIds = recentBoxIds.Where(id => id != Guid.Empty).Take(3).ToArray();
        RebuildLists();
    }

    private void RebuildLists()
    {
        var filtered = FileDialogAccessCatalog.Search(_allEntries, SearchText);
        var filteredById = filtered.ToDictionary(entry => entry.BoxId);

        RecentEntries.Clear();
        foreach (var boxId in _recentBoxIds)
        {
            if (filteredById.TryGetValue(boxId, out var entry))
            {
                RecentEntries.Add(new FileDialogAccessItemViewModel(entry, _navigate));
            }
        }

        Entries.Clear();
        foreach (var entry in filtered)
        {
            Entries.Add(new FileDialogAccessItemViewModel(entry, _navigate));
        }

        OnPropertyChanged(nameof(HasRecentEntries));
        OnPropertyChanged(nameof(HasEntries));
    }
}
