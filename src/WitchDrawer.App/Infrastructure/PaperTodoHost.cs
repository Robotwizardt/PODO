using System.Globalization;
using System.IO;
using System.Windows;
using PaperTodo;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.Infrastructure;

/// <summary>
/// Hosts the original PaperTodo paper engine inside the PODO process.
/// PODO keeps ownership of startup, the tray icon, and its storage boxes;
/// PaperTodo owns independent todo and markdown paper windows.
/// </summary>
public sealed class PaperTodoHost : IDisposable, IDesktopPaperService, IProjectTodoCountProvider
{
    private const string LegacyPaperPrefix = "podo-";
    private const string BoxPositionSettingPrefix = "BoxPosition:";

    private readonly string _dataDirectory;
    private readonly IAppLogger _logger;
    private AppController? _controller;

    public PaperTodoHost(string dataDirectory, IAppLogger logger)
    {
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _logger = logger;
    }

    public bool IsReady => _controller?.IsRunning == true;

    public event EventHandler<PaperDragCompletedEventArgs>? PaperDragCompleted;

    /// <summary>
    /// Raised after a paper is permanently removed, whether it was removed by
    /// PODO's manager or by PaperTodo's own paper menu.
    /// </summary>
    public event EventHandler<PaperRemovedEventArgs>? PaperRemoved;

    public event EventHandler<ProjectTodoCountChangedEventArgs>? TodoCountChanged;

    public async Task InitializeAsync(
        DrawerService drawerService,
        TodoService todoService,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_controller is { IsRunning: false }, this);
        if (_controller is not null)
        {
            return;
        }

        Directory.CreateDirectory(_dataDirectory);
        var controller = new AppController(
            _dataDirectory,
            enableStandaloneTray: false,
            ownsApplicationLifetime: false);

        try
        {
            await MigrateLegacyPaperBoxesAsync(
                controller,
                drawerService,
                todoService,
                cancellationToken);
            // An empty desktop is a valid persisted state. Users can create a paper from
            // PODO's tray menu without a deleted default paper returning on the next launch.
            await controller.StartAsync(createDefaultPaper: false);
            controller.PaperDragCompleted += OnPaperDragCompleted;
            controller.PaperRemoved += OnPaperRemoved;
            controller.TodoCountChanged += OnTodoCountChanged;
            _controller = controller;
            _logger.Info("PaperTodo desktop-paper engine is ready inside PODO.");
        }
        catch
        {
            controller.Dispose();
            throw;
        }
    }

    public void CreateTodoPaper() => CreatePaper(PaperTypes.Todo);

    public void CreateNotePaper() => CreatePaper(PaperTypes.Note);

    public void ShowAllPapers() => _controller?.ShowAllPapers();

    public void OpenSettingsWindow() => _controller?.OpenSettingsWindow();

    public void DeleteAllPapers() => _controller?.DeleteAllPapers();

    public IReadOnlyList<DesktopPaperSummary> GetPapers()
    {
        if (_controller is not { IsRunning: true } controller)
        {
            return [];
        }

        return controller.State.Papers
            .Where(paper => !paper.IsArchived)
            .Select(CreatePaperSummary)
            .OrderBy(summary => summary.IsVisible)
            .ThenBy(summary => summary.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<DesktopPaperSummary> GetArchivedPapers()
    {
        if (_controller is not { IsRunning: true } controller)
        {
            return [];
        }

        return controller.State.Papers
            .Where(paper => paper.IsArchived)
            .Select(CreatePaperSummary)
            .OrderBy(summary => summary.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public bool ShowPaper(string paperId)
    {
        if (_controller is not { IsRunning: true } controller
            || string.IsNullOrWhiteSpace(paperId))
        {
            return false;
        }

        var paper = FindPaper(controller, paperId);
        if (paper is null || paper.IsArchived)
        {
            return false;
        }

        controller.ShowPaper(paper);
        return true;
    }

    public bool DeletePaper(string paperId)
    {
        if (_controller is not { IsRunning: true } controller
            || string.IsNullOrWhiteSpace(paperId))
        {
            return false;
        }

        var paper = FindPaper(controller, paperId);
        if (paper is null)
        {
            return false;
        }

        controller.DeletePaper(paper);
        return true;
    }

    public int DeleteHiddenPapers()
    {
        if (_controller is not { IsRunning: true } controller)
        {
            return 0;
        }

        var hiddenPapers = controller.State.Papers
            .Where(paper => !paper.IsVisible && !paper.IsArchived)
            .ToArray();
        foreach (var paper in hiddenPapers)
        {
            controller.DeletePaper(paper);
        }

        return hiddenPapers.Length;
    }

    public IReadOnlyList<string> ArchivePapers(IEnumerable<string> paperIds)
    {
        if (_controller is not { IsRunning: true } controller)
        {
            return [];
        }

        return controller.SetPapersArchived(paperIds, isArchived: true);
    }

    public IReadOnlyList<string> RestoreArchivedPapers(IEnumerable<string> paperIds)
    {
        if (_controller is not { IsRunning: true } controller)
        {
            return [];
        }

        return controller.SetPapersArchived(paperIds, isArchived: false);
    }

    public bool TryGetPaperBounds(string paperId, out Rect bounds)
    {
        if (_controller is null)
        {
            bounds = Rect.Empty;
            return false;
        }

        return _controller.TryGetPaperBounds(paperId, out bounds);
    }

    public bool TrySetProjectAttachmentPresentation(
        string paperId,
        Rect bounds,
        bool isVisible) => _controller?.TrySetProjectAttachmentPresentation(
            paperId,
            bounds,
            isVisible) == true;

    public int GetUnfinishedTodoCount(string paperId) =>
        _controller?.GetUnfinishedTodoCount(paperId) ?? 0;

    public void Dispose()
    {
        var controller = Interlocked.Exchange(ref _controller, null);
        if (controller is not null)
        {
            controller.PaperDragCompleted -= OnPaperDragCompleted;
            controller.PaperRemoved -= OnPaperRemoved;
            controller.TodoCountChanged -= OnTodoCountChanged;
        }
        controller?.Dispose();
    }

    private void OnPaperDragCompleted(object? sender, PaperDragCompletedEventArgs e) =>
        PaperDragCompleted?.Invoke(this, e);

    private void OnPaperRemoved(object? sender, PaperRemovedEventArgs e) =>
        PaperRemoved?.Invoke(this, e);

    private void OnTodoCountChanged(object? sender, PaperTodoCountChangedEventArgs e) =>
        TodoCountChanged?.Invoke(
            this,
            new ProjectTodoCountChangedEventArgs(e.PaperId, e.UnfinishedCount));

    private static PaperData? FindPaper(AppController controller, string paperId) =>
        controller.State.Papers.FirstOrDefault(paper =>
            string.Equals(paper.Id, paperId, StringComparison.Ordinal));

    private static DesktopPaperSummary CreatePaperSummary(PaperData paper)
    {
        var isTodo = string.Equals(paper.Type, PaperTypes.Todo, StringComparison.Ordinal);
        var title = FirstNonEmpty(paper.Title);
        if (string.IsNullOrEmpty(title))
        {
            title = isTodo
                ? FirstNonEmpty(paper.Items
                    .OrderBy(item => item.Order)
                    .Select(item => item.Text)
                    .FirstOrDefault())
                : FirstNonEmptyLine(paper.Content);
        }

        if (string.IsNullOrEmpty(title))
        {
            title = isTodo ? "未命名待办" : "未命名笔记";
        }

        return isTodo
            ? CreateTodoSummary(paper, title)
            : CreateNoteSummary(paper, title);
    }

    private static DesktopPaperSummary CreateTodoSummary(PaperData paper, string title)
    {
        var items = paper.Items.Where(item => !string.IsNullOrWhiteSpace(item.Text)).ToArray();
        var unfinishedCount = items.Count(item => !item.Done);
        var detail = items.Length == 0
            ? "空白待办"
            : unfinishedCount == 0
                ? $"{items.Length} 项待办已完成"
                : $"{unfinishedCount}/{items.Length} 项待办未完成";

        return new DesktopPaperSummary(
            paper.Id,
            title,
            "待办便签",
            detail,
            paper.IsVisible);
    }

    private static DesktopPaperSummary CreateNoteSummary(PaperData paper, string title)
    {
        var characterCount = paper.Content?.Trim().Length ?? 0;
        return new DesktopPaperSummary(
            paper.Id,
            title,
            "笔记便签",
            characterCount == 0 ? "空白笔记" : $"{characterCount} 个字符",
            paper.IsVisible);
    }

    private static string FirstNonEmptyLine(string? value) =>
        (value ?? string.Empty)
        .Split(new[] { (char)13, (char)10 }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(FirstNonEmpty)
        .FirstOrDefault(line => !string.IsNullOrEmpty(line))
        ?? string.Empty;

    private static string FirstNonEmpty(string? value) => (value ?? string.Empty).Trim();

    private void CreatePaper(string type)
    {
        if (_controller is not { IsRunning: true } controller)
        {
            return;
        }

        controller.CreatePaper(type, show: true);
    }

    private async Task MigrateLegacyPaperBoxesAsync(
        AppController controller,
        DrawerService drawerService,
        TodoService todoService,
        CancellationToken cancellationToken)
    {
        var legacyBoxes = (await drawerService.GetBoxesAsync(cancellationToken))
            .Where(box => box.Type is BoxType.Todo or BoxType.Note)
            .ToArray();
        if (legacyBoxes.Length == 0)
        {
            return;
        }

        var noteService = new NoteService(todoService.Repository);
        var existingPaperIds = controller.State.Papers
            .Select(paper => paper.Id)
            .ToHashSet(StringComparer.Ordinal);
        var addedCount = 0;

        foreach (var box in legacyBoxes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paperId = LegacyPaperId(box.Id);
            if (existingPaperIds.Add(paperId))
            {
                controller.State.Papers.Add(
                    await CreateMigratedPaperAsync(
                        box,
                        paperId,
                        drawerService,
                        todoService,
                        noteService,
                        cancellationToken));
                addedCount++;
            }
        }

        // Persist first. The old records are removed only after the official paper state is present.
        controller.SaveNow(sync: true);
        if (!HasPersistedLegacyPapers(legacyBoxes))
        {
            _logger.Info("Legacy paper migration is waiting for PaperTodo state to be written.");
            return;
        }

        var removedCount = 0;
        foreach (var box in legacyBoxes)
        {
            try
            {
                await drawerService.DeleteBoxAsync(box.Id, cancellationToken);
                removedCount++;
            }
            catch (Exception exception)
            {
                _logger.Error(exception, $"Could not remove migrated legacy paper box {box.Id}.");
            }
        }

        _logger.Info(
            $"Migrated {addedCount} legacy paper(s) into PaperTodo and removed {removedCount} old box record(s).");
    }

    private async Task<PaperData> CreateMigratedPaperAsync(
        Box box,
        string paperId,
        DrawerService drawerService,
        TodoService todoService,
        NoteService noteService,
        CancellationToken cancellationToken)
    {
        var isNote = box.Type == BoxType.Note;
        var paper = new PaperData
        {
            Id = paperId,
            Type = isNote ? PaperTypes.Note : PaperTypes.Todo,
            Title = string.IsNullOrWhiteSpace(box.Name)
                ? (isNote ? "笔记" : "待办")
                : box.Name.Trim(),
            Width = isNote ? PaperLayoutDefaults.NoteDefaultWidth : PaperLayoutDefaults.TodoDefaultWidth,
            Height = isNote ? PaperLayoutDefaults.NoteDefaultHeight : PaperLayoutDefaults.TodoDefaultHeight,
            IsVisible = true,
            BodyProviderId = PaperBodyProviderIds.Markdown
        };

        var position = await drawerService.GetSettingAsync(
            BoxPositionSettingPrefix + box.Id.ToString("N"),
            cancellationToken);
        if (TryParsePosition(position, out var left, out var top))
        {
            paper.X = left;
            paper.Y = top;
        }

        if (isNote)
        {
            paper.Content = (await noteService.GetAsync(box.Id, cancellationToken))?.Content ?? string.Empty;
            return paper;
        }

        var todos = await todoService.GetTodosAsync(box.Id, cancellationToken);
        var archived = await todoService.GetArchivedTodosAsync(box.Id, cancellationToken);
        var order = 0;
        foreach (var todo in todos.OrderBy(todo => todo.SortOrder)
                     .Concat(archived.OrderBy(todo => todo.SortOrder)))
        {
            paper.Items.Add(new PaperItem
            {
                Id = todo.Id.ToString("N"),
                Text = todo.Title,
                Done = todo.IsCompleted || todo.IsArchived,
                Order = order++
            });
        }

        if (paper.Items.Count == 0)
        {
            paper.Items.Add(new PaperItem { Text = string.Empty, Order = 0 });
        }

        return paper;
    }

    private bool HasPersistedLegacyPapers(IEnumerable<Box> legacyBoxes)
    {
        try
        {
            var statePath = Path.Combine(_dataDirectory, "data.json");
            if (!File.Exists(statePath))
            {
                return false;
            }

            var json = File.ReadAllText(statePath);
            return legacyBoxes.All(box => json.Contains(LegacyPaperId(box.Id), StringComparison.Ordinal));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Could not verify PaperTodo migration data.");
            return false;
        }
    }

    private static string LegacyPaperId(Guid boxId) => LegacyPaperPrefix + boxId.ToString("N");

    internal static bool TryParsePosition(string? raw, out double left, out double top)
    {
        left = 120;
        top = 120;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var parts = raw.Split(',', StringSplitOptions.TrimEntries);
        return parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out left)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out top)
            && double.IsFinite(left)
            && double.IsFinite(top);
    }
}
