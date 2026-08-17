using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public sealed class ProjectIssueViewModel : ObservableObject
{
    private ProjectIssue _model;
    private string _title;
    private string _description;
    private ProjectSolutionState _solutionState;
    private string _solutionText;
    private ProjectResolutionState _resolutionState;
    private ProjectPriority _priority;
    private string _assigneeName;
    private string _dueAtText;

    public ProjectIssueViewModel(ProjectIssue model)
    {
        _model = model;
        _title = model.Title;
        _description = model.Description;
        _solutionState = model.SolutionState;
        _solutionText = model.SolutionText;
        _resolutionState = ProjectIssueCatalog.NormalizeModuleState(model.ResolutionState);
        _priority = model.Priority;
        _assigneeName = model.AssigneeName;
        _dueAtText = model.DueAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? string.Empty;
    }

    public ProjectIssue Model => _model;

    public Guid Id => _model.Id;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public ProjectSolutionState SolutionState
    {
        get => _solutionState;
        set
        {
            if (SetProperty(ref _solutionState, value))
            {
                OnPropertyChanged(nameof(SolutionStateLabel));
                OnPropertyChanged(nameof(SolutionStateColor));
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(StatusColor));
            }
        }
    }

    public string SolutionText
    {
        get => _solutionText;
        set => SetProperty(ref _solutionText, value);
    }

    public ProjectResolutionState ResolutionState
    {
        get => _resolutionState;
        set
        {
            var normalized = ProjectIssueCatalog.NormalizeModuleState(value);
            if (SetProperty(ref _resolutionState, normalized))
            {
                OnPropertyChanged(nameof(ResolutionStateLabel));
                OnPropertyChanged(nameof(ModuleState));
                OnPropertyChanged(nameof(ModuleStateLabel));
                OnPropertyChanged(nameof(ModuleStateColor));
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(StatusColor));
                OnPropertyChanged(nameof(IsResolved));
                OnPropertyChanged(nameof(IsReleased));
            }
        }
    }

    public ProjectResolutionState ModuleState
    {
        get => ResolutionState;
        set => ResolutionState = value;
    }

    public ProjectPriority Priority
    {
        get => _priority;
        set
        {
            if (SetProperty(ref _priority, value))
            {
                OnPropertyChanged(nameof(PriorityLabel));
            }
        }
    }

    public string AssigneeName
    {
        get => _assigneeName;
        set => SetProperty(ref _assigneeName, value);
    }

    public string DueAtText
    {
        get => _dueAtText;
        set => SetProperty(ref _dueAtText, value);
    }

    public string SolutionStateLabel => ProjectIssueCatalog.GetSolutionStateLabel(SolutionState);

    public string SolutionStateColor => SolutionState switch
    {
        ProjectSolutionState.Confirmed => "#4E9B73",
        ProjectSolutionState.Proposed => "#C59A3A",
        _ => "#D8665D"
    };

    public string ResolutionStateLabel => ProjectIssueCatalog.GetResolutionStateLabel(ResolutionState);

    public string ModuleStateLabel => ResolutionStateLabel;

    public string ModuleStateColor => ProjectIssueCatalog.NormalizeModuleState(ResolutionState) switch
    {
        ProjectResolutionState.Released => "#4E9B73",
        ProjectResolutionState.DevelopmentCompleted => "#4F7FB7",
        _ => "#8A8F97"
    };

    public string PriorityLabel => ProjectIssueCatalog.GetPriorityLabel(Priority);

    public string StatusLabel => ModuleStateLabel;

    public string StatusColor => ModuleStateColor;

    public bool IsResolved =>
        ProjectIssueCatalog.NormalizeModuleState(ResolutionState) == ProjectResolutionState.Released;

    public bool IsReleased => IsResolved;

    public string ResolvedAtLabel =>
        Model.ResolvedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? string.Empty;

    public ProjectIssue ToModel()
    {
        DateTimeOffset? dueAt = null;
        if (!string.IsNullOrWhiteSpace(DueAtText))
        {
            if (!DateTimeOffset.TryParse(
                    DueAtText.Trim(),
                    CultureInfo.CurrentCulture,
                    DateTimeStyles.AssumeLocal,
                    out var parsed))
            {
                throw new ArgumentException("截止日期格式无效，请使用 2026-08-31。", nameof(DueAtText));
            }

            dueAt = parsed.ToUniversalTime();
        }

        return _model with
        {
            Title = Title,
            Description = Description,
            SolutionState = SolutionState,
            SolutionText = SolutionText,
            ResolutionState = ProjectIssueCatalog.NormalizeModuleState(ResolutionState),
            Priority = Priority,
            AssigneeName = AssigneeName,
            DueAt = dueAt
        };
    }

    public void Apply(ProjectIssue model)
    {
        _model = model;
        Title = model.Title;
        Description = model.Description;
        SolutionState = model.SolutionState;
        SolutionText = model.SolutionText;
        ResolutionState = ProjectIssueCatalog.NormalizeModuleState(model.ResolutionState);
        Priority = model.Priority;
        AssigneeName = model.AssigneeName;
        DueAtText = model.DueAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? string.Empty;
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(ResolvedAtLabel));
    }
}
