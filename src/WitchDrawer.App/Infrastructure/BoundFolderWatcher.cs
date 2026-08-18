using System.IO;

namespace WitchDrawer.App.Infrastructure;

/// <summary>
/// Debounces the burst of FileSystemWatcher notifications produced by one
/// Explorer operation (for example, a rename or a cross-volume move).
/// </summary>
public sealed class BoundFolderWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly object _gate = new();
    private CancellationTokenSource? _pendingChange;
    private bool _disposed;

    public BoundFolderWatcher(string folderPath)
    {
        _watcher = new FileSystemWatcher(folderPath)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Created += OnFileSystemChanged;
        _watcher.Deleted += OnFileSystemChanged;
        _watcher.Renamed += OnFileSystemRenamed;
        _watcher.Changed += OnFileSystemChanged;
        _watcher.Error += OnWatcherError;
    }

    public event EventHandler? Changed;

    public event EventHandler<StorageFolderRenamedEventArgs>? Renamed;

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        QueueChanged();
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        Renamed?.Invoke(
            this,
            new StorageFolderRenamedEventArgs(e.OldFullPath, e.FullPath));
        QueueChanged();
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        QueueChanged();
    }

    private void QueueChanged()
    {
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pendingChange?.Cancel();
            _pendingChange = new CancellationTokenSource();
            cancellation = _pendingChange;
        }

        _ = RaiseChangedAfterDelayAsync(cancellation);
    }

    private async Task RaiseChangedAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(220, cancellation.Token);
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(_pendingChange, cancellation))
                {
                    return;
                }
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pendingChange?.Cancel();
            _pendingChange?.Dispose();
            _pendingChange = null;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFileSystemChanged;
        _watcher.Deleted -= OnFileSystemChanged;
        _watcher.Renamed -= OnFileSystemRenamed;
        _watcher.Changed -= OnFileSystemChanged;
        _watcher.Error -= OnWatcherError;
        _watcher.Dispose();
    }
}

public sealed record StorageFolderRenamedEventArgs(string OldPath, string NewPath);
