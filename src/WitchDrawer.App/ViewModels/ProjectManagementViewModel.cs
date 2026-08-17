using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.ViewModels;

public sealed class ProjectManagementViewModel : ObservableObject
{
    private readonly ProjectService _projectService;
    private readonly IAppLogger _logger;
    private CancellationTokenSource? _loadCts;
    private int _loadVersion;
    private Guid? _boxId;
    private ProjectDetails? _project;
    private ProjectIssueViewModel? _selectedIssue;
    private ProjectStage _selectedStage = ProjectStage.Research;
    private string _projectName = "选择一个项目收纳盒";
    private bool _showCompleted;
    private bool _areModulesExpanded;
    private string _newModuleTitle = string.Empty;
    private bool _isBusy;
    private string _statusText = "准备管理项目模块";

    public ProjectManagementViewModel(
        ProjectService projectService,
        IAppLogger logger)
    {
        _projectService = projectService;
        _logger = logger;

        SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync, CanSaveProject);
        SaveIssueCommand = new AsyncRelayCommand(SaveIssueAsync, CanSaveIssue);
        ResolveIssueCommand = new AsyncRelayCommand<ProjectIssueViewModel?>(ResolveIssueAsync);
        ReopenIssueCommand = new AsyncRelayCommand<ProjectIssueViewModel?>(ReopenIssueAsync);
        ToggleIssueCompletionCommand = new AsyncRelayCommand<ProjectIssueViewModel?>(ToggleIssueCompletionAsync);
        SetResolutionStateCommand = new AsyncRelayCommand<ProjectIssueViewModel?>(SetResolutionStateAsync);
        AddModuleCommand = new AsyncRelayCommand(AddModuleAsync, CanAddModule);
        DeleteModuleCommand = new AsyncRelayCommand<ProjectIssueViewModel?>(
            DeleteModuleAsync,
            module => module is not null && !IsBusy);
        ToggleModulesCommand = new RelayCommand(ToggleModules, () => BoxId is not null);
    }

    public ObservableCollection<ProjectIssueViewModel> ActiveIssues { get; } = [];

    public ObservableCollection<ProjectIssueViewModel> CompletedIssues { get; } = [];

    public ObservableCollection<ProjectIssueViewModel> Modules { get; } = [];

    public event EventHandler<Guid>? ProjectChanged;

    public IReadOnlyList<ProjectStageOption> StageOptions => ProjectStageCatalog.Options;

    public IReadOnlyList<ProjectSolutionStateOption> SolutionStateOptions =>
        ProjectIssueCatalog.SolutionStates;

    public IReadOnlyList<ProjectResolutionStateOption> ResolutionStateOptions =>
        ProjectIssueCatalog.ResolutionStates;

    public IReadOnlyList<ProjectResolutionStateOption> ModuleStateOptions =>
        ProjectIssueCatalog.ResolutionStates;

    public IAsyncRelayCommand SaveProjectCommand { get; }

    public IAsyncRelayCommand SaveIssueCommand { get; }

    public IAsyncRelayCommand<ProjectIssueViewModel?> ResolveIssueCommand { get; }

    public IAsyncRelayCommand<ProjectIssueViewModel?> ReopenIssueCommand { get; }

    public IAsyncRelayCommand<ProjectIssueViewModel?> ToggleIssueCompletionCommand { get; }

    public IAsyncRelayCommand<ProjectIssueViewModel?> SetResolutionStateCommand { get; }

    public IAsyncRelayCommand AddModuleCommand { get; }

    public IAsyncRelayCommand<ProjectIssueViewModel?> DeleteModuleCommand { get; }

    public IRelayCommand ToggleModulesCommand { get; }

    public Guid? BoxId
    {
        get => _boxId;
        private set
        {
            if (SetProperty(ref _boxId, value))
            {
                AddModuleCommand.NotifyCanExecuteChanged();
                ToggleModulesCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ProjectName
    {
        get => _projectName;
        private set => SetProperty(ref _projectName, value);
    }

    public string NewModuleTitle
    {
        get => _newModuleTitle;
        set
        {
            if (SetProperty(ref _newModuleTitle, value))
            {
                AddModuleCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool AreModulesExpanded
    {
        get => _areModulesExpanded;
        set
        {
            if (SetProperty(ref _areModulesExpanded, value))
            {
                OnPropertyChanged(nameof(ModuleToggleGlyph));
                OnPropertyChanged(nameof(ModuleToggleToolTip));
            }
        }
    }

    public string ModuleToggleGlyph => AreModulesExpanded ? "\uE70D" : "\uE70E";

    public string ModuleToggleToolTip => AreModulesExpanded
        ? "收起模块清单"
        : TotalIssueCount == 0
            ? "添加模块"
            : $"展开模块清单（{TotalIssueCount}）";

    public ProjectStage SelectedStage
    {
        get => _selectedStage;
        set
        {
            if (SetProperty(ref _selectedStage, value))
            {
                SaveProjectCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public ProjectIssueViewModel? SelectedIssue
    {
        get => _selectedIssue;
        set
        {
            if (SetProperty(ref _selectedIssue, value))
            {
                SaveIssueCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool ShowCompleted
    {
        get => _showCompleted;
        set => SetProperty(ref _showCompleted, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SaveProjectCommand.NotifyCanExecuteChanged();
                SaveIssueCommand.NotifyCanExecuteChanged();
                AddModuleCommand.NotifyCanExecuteChanged();
                DeleteModuleCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public int ActiveIssueCount => ActiveIssues.Count;

    public int CompletedIssueCount => CompletedIssues.Count;

    public int TotalIssueCount => ActiveIssueCount + CompletedIssueCount;

    public int ModuleCount => Modules.Count;

    public int CompletionPercentage => TotalIssueCount == 0
        ? 0
        : (int)Math.Round(
            CompletedIssueCount * 100d / TotalIssueCount,
            MidpointRounding.AwayFromZero);

    public async Task LoadAsync(Guid? boxId, string? projectName)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var cancellationToken = _loadCts.Token;
        var version = Interlocked.Increment(ref _loadVersion);

        var projectChanged = BoxId != boxId;
        BoxId = boxId;
        ProjectName = projectName ?? "选择一个项目收纳盒";
        if (projectChanged)
        {
            NewModuleTitle = string.Empty;
            AreModulesExpanded = boxId is not null;
        }

        if (boxId is null)
        {
            _project = null;
            ReplaceIssues([]);
            StatusText = "选择一个项目收纳盒";
            IsBusy = false;
            return;
        }

        IsBusy = true;
        try
        {
            var project = await _projectService.GetOrCreateProjectAsync(boxId.Value, cancellationToken);
            var issues = await _projectService.GetIssuesAsync(
                boxId.Value,
                includeResolved: true,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentLoad(boxId.Value, version))
            {
                return;
            }

            ApplyProject(project);
            ReplaceIssues(issues);
            StatusText = TotalIssueCount == 0
                ? "当前没有模块"
                : $"共 {TotalIssueCount} 个模块";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load project management data.");
            StatusText = exception.Message;
        }
        finally
        {
            if (IsCurrentLoad(boxId.Value, version))
            {
                IsBusy = false;
            }
        }
    }

    private bool CanSaveProject() => _project is not null && BoxId is not null && !IsBusy;

    private bool CanSaveIssue() => SelectedIssue is not null && !IsBusy;

    private bool CanAddModule() =>
        BoxId is not null
        && !IsBusy
        && !string.IsNullOrWhiteSpace(NewModuleTitle);

    private async Task SaveProjectAsync()
    {
        if (_project is null || BoxId is null)
        {
            return;
        }

        await RunMutationAsync(
            async cancellationToken =>
            {
                var updated = _project with
                {
                    Stage = SelectedStage
                };
                _project = await _projectService.UpdateProjectAsync(updated, cancellationToken);
                ApplyProject(_project);
                return "项目阶段已保存";
            });
    }

    private async Task SaveIssueAsync()
    {
        var issue = SelectedIssue;
        if (issue is null)
        {
            return;
        }

        await RunMutationAsync(
            async cancellationToken =>
            {
                var updated = await _projectService.UpdateIssueAsync(
                    issue.ToModel(),
                    cancellationToken);
                await ReloadIssuesAsync(updated.Id, cancellationToken);
                return "模块状态已保存";
            });
    }

    private async Task AddModuleAsync()
    {
        if (BoxId is not Guid boxId)
        {
            return;
        }

        var title = NewModuleTitle.Trim();
        await RunMutationAsync(
            async cancellationToken =>
            {
                var created = await _projectService.AddIssueAsync(
                    boxId,
                    title,
                    string.Empty,
                    ProjectSolutionState.None,
                    string.Empty,
                    ProjectPriority.Normal,
                    string.Empty,
                    null,
                    cancellationToken);
                NewModuleTitle = string.Empty;
                await ReloadIssuesAsync(created.Id, cancellationToken);
                return "模块已添加";
            });
    }

    private async Task DeleteModuleAsync(ProjectIssueViewModel? module)
    {
        if (module is null)
        {
            return;
        }

        await RunMutationAsync(
            async cancellationToken =>
            {
                await _projectService.DeleteIssueAsync(module.Id, cancellationToken);
                await ReloadIssuesAsync(null, cancellationToken);
                return $"已删除模块“{module.Title}”";
            });
    }

    private void ToggleModules()
    {
        if (BoxId is not null)
        {
            AreModulesExpanded = !AreModulesExpanded;
        }
    }

    private async Task ResolveIssueAsync(ProjectIssueViewModel? issue)
    {
        if (issue is null)
        {
            return;
        }

        await RunMutationAsync(
            async cancellationToken =>
            {
                await _projectService.ResolveIssueAsync(issue.Id, cancellationToken);
                await ReloadIssuesAsync(issue.Id, cancellationToken);
                return "模块已标记为上线完成";
            });
    }

    private async Task ToggleIssueCompletionAsync(ProjectIssueViewModel? issue)
    {
        if (issue is null)
        {
            return;
        }

        await RunMutationAsync(
            async cancellationToken =>
            {
                if (issue.IsResolved)
                {
                    await _projectService.ReopenIssueAsync(issue.Id, cancellationToken);
                    await ReloadIssuesAsync(issue.Id, cancellationToken);
                    return "模块已恢复为未开发";
                }

                await _projectService.ResolveIssueAsync(issue.Id, cancellationToken);
                await ReloadIssuesAsync(issue.Id, cancellationToken);
                return "模块已标记为上线完成";
            });
    }

    private async Task ReopenIssueAsync(ProjectIssueViewModel? issue)
    {
        if (issue is null)
        {
            return;
        }

        await RunMutationAsync(
            async cancellationToken =>
            {
                var reopened = await _projectService.ReopenIssueAsync(issue.Id, cancellationToken);
                await ReloadIssuesAsync(reopened.Id, cancellationToken);
                return "模块已恢复为未开发";
            });
    }

    private async Task SetResolutionStateAsync(ProjectIssueViewModel? issue)
    {
        if (issue is null || issue.ResolutionState == ProjectResolutionState.Resolved)
        {
            return;
        }

        await RunMutationAsync(
            async cancellationToken =>
            {
                var updated = await _projectService.SetResolutionStateAsync(
                    issue.Id,
                    issue.ResolutionState,
                    cancellationToken);
                await ReloadIssuesAsync(updated.Id, cancellationToken);
                return "模块状态已更新";
            });
    }

    private async Task RunMutationAsync(
        Func<CancellationToken, Task<string>> mutation)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            StatusText = await mutation(CancellationToken.None);
            if (BoxId is Guid boxId)
            {
                ProjectChanged?.Invoke(this, boxId);
            }
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Project management mutation failed.");
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ReloadIssuesAsync(
        Guid? selectedIssueId,
        CancellationToken cancellationToken)
    {
        if (BoxId is null)
        {
            return;
        }

        var issues = await _projectService.GetIssuesAsync(
            BoxId.Value,
            includeResolved: true,
            cancellationToken);
        ReplaceIssues(issues, selectedIssueId);
        OnPropertyChanged(nameof(ActiveIssueCount));
        OnPropertyChanged(nameof(CompletedIssueCount));
        OnPropertyChanged(nameof(TotalIssueCount));
        OnPropertyChanged(nameof(ModuleCount));
        OnPropertyChanged(nameof(CompletionPercentage));
        OnPropertyChanged(nameof(ModuleToggleToolTip));
    }

    private void ApplyProject(ProjectDetails project)
    {
        _project = project;
        SelectedStage = project.Stage;
    }

    private void ReplaceIssues(
        IEnumerable<ProjectIssue> issues,
        Guid? selectedIssueId = null)
    {
        var models = issues.ToArray();
        ActiveIssues.Clear();
        CompletedIssues.Clear();
        Modules.Clear();
        foreach (var issue in models.Where(issue => !issue.IsResolved()))
        {
            ActiveIssues.Add(new ProjectIssueViewModel(issue));
        }

        foreach (var issue in models.Where(issue => issue.IsResolved()))
        {
            CompletedIssues.Add(new ProjectIssueViewModel(issue));
        }

        foreach (var issue in models)
        {
            Modules.Add(new ProjectIssueViewModel(issue));
        }

        SelectedIssue = ActiveIssues.FirstOrDefault(issue => issue.Id == selectedIssueId)
            ?? CompletedIssues.FirstOrDefault(issue => issue.Id == selectedIssueId)
            ?? ActiveIssues.FirstOrDefault()
            ?? CompletedIssues.FirstOrDefault();
        OnPropertyChanged(nameof(ActiveIssueCount));
        OnPropertyChanged(nameof(CompletedIssueCount));
        OnPropertyChanged(nameof(TotalIssueCount));
        OnPropertyChanged(nameof(ModuleCount));
        OnPropertyChanged(nameof(CompletionPercentage));
        OnPropertyChanged(nameof(ModuleToggleToolTip));
    }

    private bool IsCurrentLoad(Guid boxId, int version) =>
        version == Volatile.Read(ref _loadVersion) && BoxId == boxId;

}

internal static class ProjectIssueExtensions
{
    public static bool IsResolved(this ProjectIssue issue) =>
        issue.ResolutionState == ProjectResolutionState.Resolved;
}
