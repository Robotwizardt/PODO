using System.Windows;
using System.Windows.Input;

namespace PaperTodo;

/// <summary>
/// Narrow bridge used by PODO's project papers. PaperTodo continues to own the
/// paper UI and content; PODO only observes and positions an attached paper.
/// </summary>
public sealed partial class AppController
{
    private readonly Dictionary<string, int> _unfinishedTodoCounts = new(StringComparer.Ordinal);

    public event EventHandler<PaperDragCompletedEventArgs>? PaperDragCompleted;

    public event EventHandler<PaperRemovedEventArgs>? PaperRemoved;

    public event EventHandler<PaperTodoCountChangedEventArgs>? TodoCountChanged;

    public int GetUnfinishedTodoCount(string paperId)
    {
        var paper = State.Papers.FirstOrDefault(item =>
            string.Equals(item.Id, paperId, StringComparison.Ordinal));
        return paper is null ? 0 : PaperTodoCountPresentation.GetUnfinishedCount(paper);
    }

    internal void NotifyTodoCountsChanged()
    {
        var currentTodoPaperIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var paper in State.Papers.Where(paper =>
                     string.Equals(paper.Type, PaperTypes.Todo, StringComparison.Ordinal)))
        {
            currentTodoPaperIds.Add(paper.Id);
            var unfinishedCount = PaperTodoCountPresentation.GetUnfinishedCount(paper);
            if (_unfinishedTodoCounts.TryGetValue(paper.Id, out var previousCount)
                && previousCount == unfinishedCount)
            {
                continue;
            }

            _unfinishedTodoCounts[paper.Id] = unfinishedCount;
            if (_windows.TryGetValue(paper.Id, out var window) && !window.IsClosed)
            {
                window.RefreshTodoCountPresentation();
            }

            TodoCountChanged?.Invoke(
                this,
                new PaperTodoCountChangedEventArgs(paper.Id, unfinishedCount));
        }

        foreach (var removedPaperId in _unfinishedTodoCounts.Keys
                     .Where(paperId => !currentTodoPaperIds.Contains(paperId))
                     .ToArray())
        {
            _unfinishedTodoCounts.Remove(removedPaperId);
        }
    }

    public bool TryGetPaperBounds(string paperId, out Rect bounds)
    {
        var paper = State.Papers.FirstOrDefault(item =>
            string.Equals(item.Id, paperId, StringComparison.Ordinal));
        if (paper is null)
        {
            bounds = Rect.Empty;
            return false;
        }

        bounds = new Rect(paper.X, paper.Y, paper.Width, paper.Height);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    public bool TrySetProjectAttachmentPresentation(
        string paperId,
        Rect bounds,
        bool isVisible)
    {
        var paper = State.Papers.FirstOrDefault(item =>
            string.Equals(item.Id, paperId, StringComparison.Ordinal));
        if (paper is null || paper.IsArchived)
        {
            return false;
        }

        if (!isVisible)
        {
            HidePaper(paper);
            return true;
        }

        paper.IsCollapsed = false;
        paper.X = Math.Round(bounds.Left);
        paper.Y = Math.Round(bounds.Top);
        paper.Width = Math.Round(Math.Max(bounds.Width, PaperLayoutDefaults.MinWidth));
        paper.Height = Math.Round(Math.Max(bounds.Height, PaperLayoutDefaults.MinHeight));
        ShowPaper(paper, activate: false);
        if (_windows.TryGetValue(paper.Id, out var window) && !window.IsClosed)
        {
            window.Left = paper.X;
            window.Top = paper.Y;
            window.Width = paper.Width;
            window.Height = paper.Height;
        }

        MarkDirty();
        return true;
    }

    /// <summary>
    /// Moves an already-present project paper during its owner's native drag.
    /// This is intentionally presentation-only: the normal debounced PaperTodo
    /// save remains responsible for persisting the final position after release.
    /// </summary>
    public bool TryOffsetProjectAttachmentPresentation(
        string paperId,
        double deltaLeft,
        double deltaTop)
    {
        if (!double.IsFinite(deltaLeft) || !double.IsFinite(deltaTop))
        {
            return false;
        }

        var paper = State.Papers.FirstOrDefault(item =>
            string.Equals(item.Id, paperId, StringComparison.Ordinal));
        if (paper is null || paper.IsArchived)
        {
            return false;
        }

        // Keep sub-DIP motion in memory while the owning box is being dragged.
        // The release-time layout still uses the normal rounded persistence path.
        paper.X += deltaLeft;
        paper.Y += deltaTop;
        if (_windows.TryGetValue(paper.Id, out var window) && !window.IsClosed)
        {
            window.Left = paper.X;
            window.Top = paper.Y;
        }

        return true;
    }

    public IReadOnlyList<string> SetPapersArchived(
        IEnumerable<string> paperIds,
        bool isArchived)
    {
        ArgumentNullException.ThrowIfNull(paperIds);

        var targetPaperIds = paperIds
            .Where(paperId => !string.IsNullOrWhiteSpace(paperId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (targetPaperIds.Length == 0)
        {
            return [];
        }

        var changedPaperIds = new List<string>();
        foreach (var paperId in targetPaperIds)
        {
            var paper = State.Papers.FirstOrDefault(item =>
                string.Equals(item.Id, paperId, StringComparison.Ordinal));
            if (paper is null || paper.IsArchived == isArchived)
            {
                continue;
            }

            paper.IsArchived = isArchived;
            if (isArchived)
            {
                HidePaper(paper);
            }
            else
            {
                // Restoring a project lets PODO reapply the saved attachment side and visibility.
                paper.IsVisible = false;
            }

            changedPaperIds.Add(paper.Id);
        }

        if (changedPaperIds.Count > 0)
        {
            MarkDirty();
            SaveNow(sync: true);
        }

        return changedPaperIds;
    }

    internal void NotifyPaperDragCompleted(PaperData paper, Window window)
    {
        if (double.IsNaN(window.Left) || double.IsNaN(window.Top))
        {
            return;
        }

        var width = Math.Max(
            window.ActualWidth > 0 ? window.ActualWidth : window.Width,
            PaperLayoutDefaults.MinWidth);
        var height = Math.Max(
            window.ActualHeight > 0 ? window.ActualHeight : window.Height,
            PaperLayoutDefaults.MinHeight);
        paper.X = Math.Round(window.Left);
        paper.Y = Math.Round(window.Top);
        if (!paper.IsCollapsed)
        {
            paper.Width = Math.Round(width);
            paper.Height = Math.Round(height);
        }

        MarkDirty();
        PaperDragCompleted?.Invoke(
            this,
            new PaperDragCompletedEventArgs(
                paper.Id,
                new Rect(paper.X, paper.Y, width, height),
                CapturePaperDragReleasePoint(window),
                isExplicitProjectUnlinkRequested: (Keyboard.Modifiers & ModifierKeys.Shift)
                    == ModifierKeys.Shift));
    }

    internal void NotifyPaperRemoved(string paperId)
    {
        if (!string.IsNullOrWhiteSpace(paperId))
        {
            PaperRemoved?.Invoke(this, new PaperRemovedEventArgs(paperId));
        }
    }

    private static Point? CapturePaperDragReleasePoint(Window window)
    {
        try
        {
            if (!WindowNative.TryGetCursorScreenPosition(out var cursor))
            {
                return null;
            }

            var localPoint = window.PointFromScreen(new Point(cursor.X, cursor.Y));
            return new Point(window.Left + localPoint.X, window.Top + localPoint.Y);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

public sealed class PaperDragCompletedEventArgs(
    string paperId,
    Rect bounds,
    Point? dropPoint,
    bool isExplicitProjectUnlinkRequested = false) : EventArgs
{
    public string PaperId { get; } = paperId;

    public Rect Bounds { get; } = bounds;

    public Point? DropPoint { get; } = dropPoint;

    public bool IsExplicitProjectUnlinkRequested { get; } = isExplicitProjectUnlinkRequested;
}

public sealed class PaperRemovedEventArgs(string paperId) : EventArgs
{
    public string PaperId { get; } = paperId;
}

public sealed class PaperTodoCountChangedEventArgs(
    string paperId,
    int unfinishedCount) : EventArgs
{
    public string PaperId { get; } = paperId;

    public int UnfinishedCount { get; } = unfinishedCount;
}
