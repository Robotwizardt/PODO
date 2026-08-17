using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.Messages;
using WitchDrawer.Core.Abstractions;
using WitchDrawer.Core.Logging;
using WitchDrawer.Core.Models;
using WitchDrawer.Core.Services;
using WitchDrawer.Native.Files;

namespace WitchDrawer.App.ViewModels;

public sealed class DesktopBoxViewModel : ObservableObject
{
    private const int ProjectFolderDefaultColumns = 3;
    private const int ProjectFolderDefaultRows = 4;
    private const int ProjectFolderMinimumColumns = 2;
    private const int ProjectFolderMaximumColumns = 6;
    private const int ProjectFolderMinimumRows = 1;
    private const int ProjectFolderMaximumRows = 6;
    private const double ProjectFolderAppItemWidth = 104;
    private const double ProjectFolderAppItemHeight = 112;
    private const double ProjectFolderHorizontalChrome = 30;
    private const double ProjectFolderVerticalChrome = 26;
    private const double EdgeExpandThreshold = 14;
    private const double VisibleHeaderRowHeight = 24;
    private const double HiddenGridContentInset = 6;
    private const string MappingViewModeSettingPrefix = "MappingViewMode:";
    private const string MappingListViewMode = "List";
    private const string MappingGridViewMode = "Grid";
    private const string DrawerCoverSizeSettingPrefix = "DrawerCoverSize:";
    private const string TitleVisibilitySettingPrefix = "BoxTitleVisible:";
    private const string LegacyDrawerTitleVisibilitySettingPrefix = "DrawerTitleVisible:";
    private const string FileNameVisibilitySettingPrefix = "BoxFileNameVisible:";
    private const string DrawerSortModeSettingPrefix = "DrawerSortMode:";
    private const string NoteCollapsedSettingPrefix = "NoteCollapsed:";
    private const string ProjectAttachmentSideVisibleSettingPrefix = "ProjectAttachmentSideVisible:";
    private const double DefaultDrawerCoverWidth = 180;
    private const double DefaultDrawerCoverHeight = 112;
    private const double MaximumDrawerCoverDimension = 720;
    private const double DrawerTitleHeightCompensation = 9;

    private readonly DrawerService _drawerService;
    private readonly TodoService _todoService;
    private readonly NoteService _noteService;
    private readonly ProjectService _projectService;
    private readonly ProjectFolderService _projectFolderService;
    private readonly IProjectTodoCountProvider? _projectTodoCountProvider;
    private readonly IFileLauncher _launcher;
    private readonly IAppLogger _logger;
    private readonly DesktopBoxLayoutSettings _layoutSettings;
    private Box _box;
    private BoxVisualStyle _visualStyle;
    private bool _isBusy;
    private double _gridCanvasWidth;
    private DateTime _lastCanvasSizeChangedUtc = DateTime.MinValue;
    private double _lastCanvasWidth = double.NaN;
    private double _lastCanvasHeight = double.NaN;
    private double _gridCanvasHeight;
    private bool _isDragPreviewVisible;
    private double _dragPreviewLeft;
    private double _dragPreviewTop;
    private double? _dragPreviewWidthOverride;
    private double? _dragPreviewHeightOverride;
    private int _previewColumn;
    private int _previewRow;
    private string _statusText = "拖入文件";
    private bool _isDragOver;
    private bool _isMappingListMode;
    private string _newTodoTitle = string.Empty;
    private string _newProjectIssueTitle = string.Empty;
    private double _iconDpiScaleX = 1;
    private double _iconDpiScaleY = 1;
    private bool _isDrawerExpanded;
    private bool _isPositionLocked;
    private bool _isTitleVisible = true;
    private bool _isFileNameVisible = true;
    private double _drawerCoverWidth = DefaultDrawerCoverWidth;
    private double _drawerCoverHeight = DefaultDrawerCoverHeight;
    private int _drawerCoverColumns = 3;
    private int _drawerCoverRows = 2;
    private DrawerItemSortMode _drawerItemSortMode = DrawerItemSortMode.Free;
    private BoxSizeModeState _sizeMode = BoxSizeModeState.Adaptive;
    private int _occupiedColumns = 1;
    private int _occupiedRows = 1;
    private ProjectDetails? _project;
    private ProjectStage _selectedProjectStage = ProjectStage.Research;
    private IReadOnlyList<ProjectIssue> _projectIssueModels = [];
    private bool _areProjectModulesExpanded;
    private bool _isProjectLeftAttachmentsVisible = true;
    private bool _isProjectRightAttachmentsVisible = true;
    private bool _isProjectTopAttachmentsVisible = true;
    private bool _isProjectBottomAttachmentsVisible = true;
    private bool _isProjectAttachmentDropPreviewVisible;
    private bool _hasLoaded;
    private bool _isProjectGroupingDropPreviewVisible;
    private string _projectAssociationMessage = string.Empty;
    private int _projectLinkedPaperCount;
    private readonly Dictionary<ProjectAttachmentSide, int> _projectAttachmentCounts = [];
    private int _projectActiveIssueCount;
    private int _projectCompletedIssueCount;
    private int _projectTotalIssueCount;
    private Guid? _projectFolderId;
    private string _noteContent = string.Empty;
    private bool _isNotePreview;
    private bool _isNoteCollapsed;
    private bool _isLoadingNote;
    private bool _isSavingNote;
    private string _noteStatusText = "自动保存";
    private CancellationTokenSource? _noteSaveCts;
    private readonly SemaphoreSlim _noteSaveGate = new(1, 1);

    public DesktopBoxViewModel(
        Box box,
        DrawerService drawerService,
        TodoService todoService,
        IFileLauncher launcher,
        IAppLogger logger,
        BoxVisualStyle visualStyle,
        DesktopBoxLayoutSettings? layoutSettings = null,
        ProjectService? projectService = null,
        NoteService? noteService = null,
        ProjectFolderService? projectFolderService = null,
        IProjectTodoCountProvider? projectTodoCountProvider = null)
    {
        _box = box;
        _visualStyle = visualStyle;
        _drawerService = drawerService;
        _todoService = todoService;
        _projectService = projectService ?? new ProjectService(todoService.Repository);
        _projectFolderService = projectFolderService ?? new ProjectFolderService(todoService.Repository);
        _projectTodoCountProvider = projectTodoCountProvider;
        _noteService = noteService ?? new NoteService(todoService.Repository);
        _launcher = launcher;
        _logger = logger;
        _layoutSettings = layoutSettings ?? new DesktopBoxLayoutSettings(box.Type == BoxType.Drawer);
        _layoutSettings.PropertyChanged += OnLayoutSettingsChanged;

        OpenItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(OpenItemAsync);
        DeleteItemCommand = new AsyncRelayCommand<DrawerItemViewModel?>(DeleteItemAsync);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        UseMappingGridModeCommand = new AsyncRelayCommand(() => SetMappingViewModeAsync(useListMode: false));
        UseMappingListModeCommand = new AsyncRelayCommand(() => SetMappingViewModeAsync(useListMode: true));
        AddTodoCommand = new AsyncRelayCommand(AddTodoAsync, CanAddTodo);
        ToggleTodoCommand = new AsyncRelayCommand<TodoItemViewModel?>(ToggleTodoAsync);
        ArchiveCompletedTodosCommand = new AsyncRelayCommand(ArchiveCompletedTodosAsync, CanArchiveCompletedTodos);
        DeleteTodoCommand = new AsyncRelayCommand<TodoItemViewModel?>(DeleteTodoAsync);
        AddProjectIssueCommand = new AsyncRelayCommand(AddProjectIssueAsync, CanAddProjectIssue);
        SaveProjectStageCommand = new AsyncRelayCommand(SaveProjectStageAsync, CanSaveProjectStage);
        OpenProjectFolderMemberCommand = new AsyncRelayCommand<ProjectFolderMemberViewModel?>(
            OpenProjectFolderMemberAsync);
        DecreaseProjectFolderColumnsCommand = new AsyncRelayCommand(
            () => ResizeProjectFolderAsync(ProjectFolderColumns - 1, ProjectFolderVisibleRows),
            () => IsProjectFolder && ProjectFolderColumns > ProjectFolderMinimumColumns);
        IncreaseProjectFolderColumnsCommand = new AsyncRelayCommand(
            () => ResizeProjectFolderAsync(ProjectFolderColumns + 1, ProjectFolderVisibleRows),
            () => IsProjectFolder && ProjectFolderColumns < ProjectFolderMaximumColumns);
        DecreaseProjectFolderRowsCommand = new AsyncRelayCommand(
            () => ResizeProjectFolderAsync(ProjectFolderColumns, ProjectFolderVisibleRows - 1),
            () => IsProjectFolder && ProjectFolderVisibleRows > ProjectFolderMinimumRows);
        IncreaseProjectFolderRowsCommand = new AsyncRelayCommand(
            () => ResizeProjectFolderAsync(ProjectFolderColumns, ProjectFolderVisibleRows + 1),
            () => IsProjectFolder && ProjectFolderVisibleRows < ProjectFolderMaximumRows);
        UseAdaptiveSizeCommand = new AsyncRelayCommand(
            () => SetDesktopGridSizeAsync(BoxSizeModeState.Adaptive),
            () => SupportsDesktopGridSizeControls && IsFixedSize);
        UseFixedSizeCommand = new AsyncRelayCommand(
            () => SetDesktopGridSizeAsync(new BoxSizeModeState(
                true,
                SizeMode.Columns,
                SizeMode.Rows)),
            () => SupportsDesktopGridSizeControls && !IsFixedSize);
        DecreaseFixedColumnsCommand = new AsyncRelayCommand(
            () => ResizeDesktopGridAsync(SizeMode.Columns - 1, SizeMode.Rows),
            () => SupportsDesktopGridSizeControls
                && IsFixedSize
                && SizeMode.Columns > BoxSizeModeState.MinCells);
        IncreaseFixedColumnsCommand = new AsyncRelayCommand(
            () => ResizeDesktopGridAsync(SizeMode.Columns + 1, SizeMode.Rows),
            () => SupportsDesktopGridSizeControls
                && IsFixedSize
                && SizeMode.Columns < BoxSizeModeState.MaxColumns);
        DecreaseFixedRowsCommand = new AsyncRelayCommand(
            () => ResizeDesktopGridAsync(SizeMode.Columns, SizeMode.Rows - 1),
            () => SupportsDesktopGridSizeControls
                && IsFixedSize
                && SizeMode.Rows > BoxSizeModeState.MinCells);
        IncreaseFixedRowsCommand = new AsyncRelayCommand(
            () => ResizeDesktopGridAsync(SizeMode.Columns, SizeMode.Rows + 1),
            () => SupportsDesktopGridSizeControls
                && IsFixedSize
                && SizeMode.Rows < BoxSizeModeState.MaxRows);
        ToggleProjectIssueCommand = new AsyncRelayCommand<ProjectIssueViewModel?>(ToggleProjectIssueAsync);
        UpdateProjectModuleStateCommand = new AsyncRelayCommand<ProjectIssueViewModel?>(UpdateProjectModuleStateAsync);
        DeleteProjectModuleCommand = new AsyncRelayCommand<ProjectIssueViewModel?>(DeleteProjectModuleAsync);
        ToggleProjectModulesCommand = new RelayCommand(ToggleProjectModules, () => IsProjectBox);
        ToggleProjectLeftAttachmentsCommand = new AsyncRelayCommand(
            () => ToggleProjectAttachmentSideAsync(ProjectAttachmentSide.Left));
        ToggleProjectRightAttachmentsCommand = new AsyncRelayCommand(
            () => ToggleProjectAttachmentSideAsync(ProjectAttachmentSide.Right));
        ToggleProjectTopAttachmentsCommand = new AsyncRelayCommand(
            () => ToggleProjectAttachmentSideAsync(ProjectAttachmentSide.Top));
        ToggleProjectBottomAttachmentsCommand = new AsyncRelayCommand(
            () => ToggleProjectAttachmentSideAsync(ProjectAttachmentSide.Bottom));
        SaveNoteCommand = new AsyncRelayCommand(
            () => SaveNoteNowAsync(CancellationToken.None),
            CanSaveNote);
        ToggleNotePreviewCommand = new RelayCommand(ToggleNotePreview, () => IsNoteBox);
        ToggleNoteCollapsedCommand = new AsyncRelayCommand(ToggleNoteCollapsedAsync, () => IsNoteBox);
        UpdateGridCanvasSize();
        _ = LoadMappingViewModeAsync();
    }

    public DesktopBoxLayoutSettings LayoutSettings => _layoutSettings;

    public event EventHandler? ItemsChanged;

    public event EventHandler? ProjectLinksChanged;

    public event EventHandler? ProjectFolderChanged;

    public event Action<Guid>? ProjectFolderMemberOpenRequested;

    public ResettableObservableCollection<DrawerItemViewModel> Items { get; } = [];

    public ObservableCollection<DrawerItemViewModel> DrawerPreviewItems { get; } = [];

    public ObservableCollection<DrawerCoverTileViewModel> DrawerCoverTiles { get; } = [];

    public ResettableObservableCollection<DrawerItemViewModel> DrawerSecondaryItems { get; } = [];

    public ObservableCollection<TodoItemViewModel> TodoItems { get; } = [];

    public ObservableCollection<ProjectIssueViewModel> ProjectIssues { get; } = [];

    public ObservableCollection<ProjectLinkedBoxViewModel> ProjectLinkedBoxes { get; } = [];

    public ObservableCollection<ProjectFolderMemberViewModel> ProjectFolderMembers { get; } = [];

    public ObservableCollection<NotePreviewBlockViewModel> NotePreviewBlocks { get; } = [];

    public IAsyncRelayCommand<DrawerItemViewModel?> OpenItemCommand { get; }

    public IAsyncRelayCommand<DrawerItemViewModel?> DeleteItemCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand UseMappingGridModeCommand { get; }

    public IAsyncRelayCommand UseMappingListModeCommand { get; }

    public IAsyncRelayCommand AddTodoCommand { get; }

    public IAsyncRelayCommand<TodoItemViewModel?> ToggleTodoCommand { get; }

    public IAsyncRelayCommand ArchiveCompletedTodosCommand { get; }

    public IAsyncRelayCommand<TodoItemViewModel?> DeleteTodoCommand { get; }

    public IAsyncRelayCommand AddProjectIssueCommand { get; }

    public IAsyncRelayCommand SaveProjectStageCommand { get; }

    public IAsyncRelayCommand<ProjectFolderMemberViewModel?> OpenProjectFolderMemberCommand { get; }

    public IAsyncRelayCommand DecreaseProjectFolderColumnsCommand { get; }

    public IAsyncRelayCommand IncreaseProjectFolderColumnsCommand { get; }

    public IAsyncRelayCommand DecreaseProjectFolderRowsCommand { get; }

    public IAsyncRelayCommand IncreaseProjectFolderRowsCommand { get; }

    public IAsyncRelayCommand UseAdaptiveSizeCommand { get; }

    public IAsyncRelayCommand UseFixedSizeCommand { get; }

    public IAsyncRelayCommand DecreaseFixedColumnsCommand { get; }

    public IAsyncRelayCommand IncreaseFixedColumnsCommand { get; }

    public IAsyncRelayCommand DecreaseFixedRowsCommand { get; }

    public IAsyncRelayCommand IncreaseFixedRowsCommand { get; }

    public IAsyncRelayCommand<ProjectIssueViewModel?> ToggleProjectIssueCommand { get; }

    public IAsyncRelayCommand<ProjectIssueViewModel?> UpdateProjectModuleStateCommand { get; }

    public IAsyncRelayCommand<ProjectIssueViewModel?> DeleteProjectModuleCommand { get; }

    public IRelayCommand ToggleProjectModulesCommand { get; }


    public IAsyncRelayCommand ToggleProjectLeftAttachmentsCommand { get; }

    public IAsyncRelayCommand ToggleProjectRightAttachmentsCommand { get; }

    public IAsyncRelayCommand ToggleProjectTopAttachmentsCommand { get; }

    public IAsyncRelayCommand ToggleProjectBottomAttachmentsCommand { get; }

    public IAsyncRelayCommand SaveNoteCommand { get; }

    public IRelayCommand ToggleNotePreviewCommand { get; }

    public IAsyncRelayCommand ToggleNoteCollapsedCommand { get; }

    public Guid BoxId => _box.Id;

    public string Name => _box.Name;

    public BoxType Type => _box.Type;

    public BoxVisualStyle VisualStyle => _visualStyle;

    public bool IsPixelStyle => VisualStyle == BoxVisualStyle.Pixel;

    public bool IsMappingBox => Type == BoxType.Mapping;

    public bool IsTodoBox => Type == BoxType.Todo;

    public bool IsNoteBox => Type == BoxType.Note;

    public bool IsProjectBox => Type == BoxType.Project;

    public bool IsProjectFolder => Type == BoxType.ProjectFolder;

    public bool SupportsDesktopActions =>
        Type is BoxType.Normal
            or BoxType.Mapping
            or BoxType.Pixel
            or BoxType.Drawer
            or BoxType.Bound
            or BoxType.Project
            or BoxType.ProjectFolder;

    public bool SupportsDesktopGridSizeControls =>
        Type is BoxType.Normal or BoxType.Pixel or BoxType.Bound;

    public bool SupportsFileManagement => Type is BoxType.Normal or BoxType.Bound;

    public string DesktopDeleteActionLabel => IsProjectFolder
        ? "解散项目文件夹"
        : IsProjectBox
            ? "删除项目收纳盒"
            : "删除收纳盒";

    public Guid? ProjectFolderId => _projectFolderId;

    public bool HasProjectFolderMembership => _projectFolderId is not null;

    public int ProjectFolderColumns => IsFixedSize
        ? Math.Clamp(SizeMode.Columns, ProjectFolderMinimumColumns, ProjectFolderMaximumColumns)
        : ProjectFolderDefaultColumns;

    public int ProjectFolderVisibleRows => IsFixedSize
        ? Math.Clamp(SizeMode.Rows, ProjectFolderMinimumRows, ProjectFolderMaximumRows)
        : ProjectFolderDefaultRows;

    public double ProjectFolderWidth => ProjectFolderHorizontalChrome
        + (ProjectFolderColumns * ProjectFolderAppItemWidth);

    public double ProjectFolderScrollMaxHeight =>
        ProjectFolderVisibleRows * ProjectFolderAppItemHeight;

    public double ProjectFolderMaxHeight =>
        ProjectFolderScrollMaxHeight + ProjectFolderVerticalChrome;

    public string NoteContent
    {
        get => _noteContent;
        set
        {
            if (!SetProperty(ref _noteContent, value ?? string.Empty))
            {
                return;
            }

            ReplaceNotePreview();
            OnPropertyChanged(nameof(NoteCharacterCount));
            if (!_isLoadingNote && IsNoteBox)
            {
                NoteStatusText = "正在保存…";
                ScheduleNoteSave();
            }
        }
    }

    public int NoteCharacterCount => NoteContent.Length;

    public bool IsNotePreview
    {
        get => _isNotePreview;
        private set
        {
            if (SetProperty(ref _isNotePreview, value))
            {
                OnPropertyChanged(nameof(NotePreviewButtonText));
            }
        }
    }

    public string NotePreviewButtonText => IsNotePreview ? "编辑" : "预览";

    public string NoteCollapsedButtonToolTip => IsNoteCollapsed
        ? "展开便签"
        : "折叠为桌面胶囊";

    public string NoteStatusText
    {
        get => _noteStatusText;
        private set => SetProperty(ref _noteStatusText, value);
    }

    public bool IsNoteCollapsed
    {
        get => IsNoteBox && _isNoteCollapsed;
        private set
        {
            if (SetProperty(ref _isNoteCollapsed, value))
            {
                OnPropertyChanged(nameof(IsNoteCollapsed));
            }
        }
    }

    public bool IsDrawerBox => Type == BoxType.Drawer;

    public bool IsBoundBox => Type == BoxType.Bound;

    public ProjectDetails? Project => _project;

    public IReadOnlyList<ProjectStageOption> ProjectStageOptions => ProjectStageCatalog.Options;

    public string NewProjectIssueTitle
    {
        get => _newProjectIssueTitle;
        set
        {
            if (SetProperty(ref _newProjectIssueTitle, value))
            {
                AddProjectIssueCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(NewProjectModuleTitle));
            }
        }
    }

    public string NewProjectModuleTitle
    {
        get => NewProjectIssueTitle;
        set => NewProjectIssueTitle = value;
    }

    public ObservableCollection<ProjectIssueViewModel> ProjectModules => ProjectIssues;

    public IAsyncRelayCommand AddProjectModuleCommand => AddProjectIssueCommand;

    public IReadOnlyList<ProjectResolutionStateOption> ProjectModuleStateOptions =>
        ProjectIssueCatalog.ResolutionStates;

    public ProjectStage SelectedProjectStage
    {
        get => _selectedProjectStage;
        set
        {
            if (SetProperty(ref _selectedProjectStage, value))
            {
                SaveProjectStageCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool AreProjectModulesExpanded
    {
        get => _areProjectModulesExpanded;
        set
        {
            if (SetProperty(ref _areProjectModulesExpanded, value))
            {
                OnPropertyChanged(nameof(ProjectModuleToggleLabel));
                OnPropertyChanged(nameof(ProjectModuleToggleGlyph));
            }
        }
    }

    public string ProjectModuleToggleGlyph => AreProjectModulesExpanded
        ? "\uE70D"
        : "\uE70E";

    public string ProjectModuleToggleLabel => AreProjectModulesExpanded
        ? "收起模块清单"
        : ProjectTotalIssueCount == 0
            ? "添加模块"
            : $"展开模块 {ProjectTotalIssueCount}";

    public string ProjectStageLabel => _project is null
        ? "未设置"
        : ProjectStageCatalog.Get(_project.Stage).Name;

    public string ProjectStageColor => _project is null
        ? "#8A8F97"
        : ProjectStageCatalog.Get(_project.Stage).Color;

    public int ProjectActiveIssueCount => _projectActiveIssueCount;

    public int ProjectCompletedIssueCount => _projectCompletedIssueCount;

    public int ProjectTotalIssueCount => _projectTotalIssueCount;

    public int ProjectCompletionPercentage => ProjectTotalIssueCount == 0
        ? 0
        : (int)Math.Round(
            ProjectCompletedIssueCount * 100d / ProjectTotalIssueCount,
            MidpointRounding.AwayFromZero);

    public string ProjectChecklistSummary => ProjectTotalIssueCount == 0
        ? "暂无模块"
        : $"{ProjectCompletedIssueCount}/{ProjectTotalIssueCount} 已上线";

    public string ProjectModuleSummary => ProjectChecklistSummary;

    public double ProjectBoxWidth => IsFixedSize
        ? Math.Clamp(220 + (SizeMode.Columns * 42), 286, 560)
        : 286;

    public double ProjectBoxMaxHeight => IsFixedSize
        ? Math.Clamp(136 + (SizeMode.Rows * 54), 220, 620)
        : 360;

    public double ProjectModuleListMaxHeight => IsFixedSize
        ? Math.Clamp(42 + (SizeMode.Rows * 42), 96, 430)
        : 205;

    public int ProjectAttachmentCount => ProjectLinkedBoxes.Count + _projectLinkedPaperCount;

    public int ProjectLeftAttachmentCount => GetProjectAttachmentCount(ProjectAttachmentSide.Left);

    public int ProjectRightAttachmentCount => GetProjectAttachmentCount(ProjectAttachmentSide.Right);

    public int ProjectTopAttachmentCount => GetProjectAttachmentCount(ProjectAttachmentSide.Top);

    public int ProjectBottomAttachmentCount => GetProjectAttachmentCount(ProjectAttachmentSide.Bottom);

    public bool HasProjectLeftAttachments => ProjectLeftAttachmentCount > 0;

    public bool HasProjectRightAttachments => ProjectRightAttachmentCount > 0;

    public bool HasProjectTopAttachments => ProjectTopAttachmentCount > 0;

    public bool HasProjectBottomAttachments => ProjectBottomAttachmentCount > 0;

    public bool HasProjectAttachments => ProjectAttachmentCount > 0;

    public string ProjectAttachmentCountLabel => ProjectAttachmentCount > 9
        ? "9+"
        : ProjectAttachmentCount.ToString(CultureInfo.InvariantCulture);

    public bool IsProjectLeftAttachmentsVisible => _isProjectLeftAttachmentsVisible;

    public bool IsProjectRightAttachmentsVisible => _isProjectRightAttachmentsVisible;

    public bool IsProjectTopAttachmentsVisible => _isProjectTopAttachmentsVisible;

    public bool IsProjectBottomAttachmentsVisible => _isProjectBottomAttachmentsVisible;

    public bool IsProjectAttachmentDropPreviewVisible
    {
        get => _isProjectAttachmentDropPreviewVisible;
        set => SetProperty(ref _isProjectAttachmentDropPreviewVisible, value);
    }

    public bool IsProjectGroupingDropPreviewVisible
    {
        get => _isProjectGroupingDropPreviewVisible;
        set => SetProperty(ref _isProjectGroupingDropPreviewVisible, value);
    }

    public string ProjectAssociationMessage => _projectAssociationMessage;

    public bool HasProjectAssociationMessage =>
        !string.IsNullOrWhiteSpace(ProjectAssociationMessage);

    /// <summary>
    /// 固定尺寸适用于网格收纳盒和项目收纳盒；项目盒用它控制宽度与模块区高度。
    /// </summary>
    public bool SupportsFixedSize => Type is BoxType.Normal or BoxType.Pixel or BoxType.Bound or BoxType.Project or BoxType.ProjectFolder;

    public BoxSizeModeState SizeMode => _sizeMode;

    public bool IsFixedSize => SupportsFixedSize && _sizeMode.IsFixed;

    /// <summary>
    /// 网格视口宽度：固定模式下按 m×n 格物理尺寸 + 共享 chrome 预留渲染，
    /// 与自适应模式物理尺寸像素级对齐；自适应模式下为 NaN（Auto 贴合内容）。
    /// </summary>
    public double GridViewportWidth => IsFixedSize
        ? (SizeMode.Columns * LayoutSettings.ItemSlotWidth) + DesktopBoxLayoutSettings.GridViewportFixedChromeInset
        : double.NaN;

    public double GridViewportHeight => IsFixedSize
        ? (SizeMode.Rows * LayoutSettings.ItemSlotHeight) + DesktopBoxLayoutSettings.GridViewportFixedChromeInset
        : double.NaN;

    /// <summary>
    /// 自适应模式继续限制最大可见范围并通过滚动查看内容；固定模式的上限必须
    /// 跟随用户选择的真实行列尺寸，否则 12 × 8 之后只会变化数字而窗口不增长。
    /// </summary>
    public double GridViewportMaxWidth => IsFixedSize
        ? GridViewportWidth
        : LayoutSettings.GridViewportMaxWidth;

    public double GridViewportMaxHeight => IsFixedSize
        ? GridViewportHeight
        : LayoutSettings.GridViewportMaxHeight;

    /// <summary>
    /// 固定规格控制可见视口；已有内容超出视口时仍可通过滚动查看。
    /// </summary>
    public ScrollBarVisibility GridHorizontalScrollBarVisibility =>
        ScrollBarVisibility.Auto;

    public ScrollBarVisibility GridVerticalScrollBarVisibility =>
        ScrollBarVisibility.Auto;

    public int OccupiedColumns => _occupiedColumns;

    public int OccupiedRows => _occupiedRows;

    public bool IsDrawerExpanded
    {
        get => IsDrawerBox && _isDrawerExpanded;
        set
        {
            if (SetProperty(ref _isDrawerExpanded, value))
            {
                OnPropertyChanged(nameof(IsDrawerCollapsed));
                OnPropertyChanged(nameof(IsHeaderVisible));
                OnPropertyChanged(nameof(HeaderRowHeight));
                OnPropertyChanged(nameof(ShowFileEmptyState));
            }
        }
    }

    public bool IsDrawerCollapsed => IsDrawerBox && !IsDrawerExpanded;

    public bool IsTitleVisible => _isTitleVisible;

    public bool IsPositionLocked => _isPositionLocked;

    public string PositionLockActionLabel => IsPositionLocked
        ? "解锁桌面位置"
        : "锁定桌面位置";

    public bool IsFileNameVisible => _isFileNameVisible;

    public bool IsHeaderVisible => SupportsDesktopActions || ShouldShowHeader(
        IsDrawerBox,
        IsDrawerExpanded,
        IsTitleVisible);

    public double HeaderRowHeight => CalculateHeaderRowHeight(
        IsHeaderVisible,
        IsDrawerBox,
        IsMappingListMode,
        LayoutSettings.MappingListMargin.Top,
        LayoutSettings.MappingListMargin.Bottom);

    public double DrawerCoverWidth => _drawerCoverWidth;

    public double DrawerCoverHeight => _drawerCoverHeight;

    public double DrawerContentHeight => CalculateDrawerContentHeight(
        DrawerCoverHeight,
        IsTitleVisible);

    public int DrawerCoverColumns => _drawerCoverColumns;

    public int DrawerCoverRows => _drawerCoverRows;

    public int DrawerCoverCapacity => DrawerCoverColumns * DrawerCoverRows;

    public bool DrawerHasOverflow => Items.Count > DrawerCoverCapacity;

    public int DrawerDirectItemCount => CalculateDrawerDirectItemCount(
        Items.Count,
        DrawerCoverCapacity);

    public DrawerItemSortMode DrawerItemSortMode => _drawerItemSortMode;

    public int DrawerSecondaryColumns => CalculateDrawerSecondaryColumns(
        DrawerSecondaryItems.Count);

    public int DrawerSecondaryRows => CalculateDrawerSecondaryRows(
        DrawerSecondaryItems.Count,
        DrawerSecondaryColumns);

    public bool DrawerSecondaryHasScrollableOverflow => DrawerSecondaryRows > 5;

    public double DrawerSecondaryPanelWidth => Math.Clamp(
        (DrawerSecondaryColumns
            * (LayoutSettings.DrawerPrimaryIconFrameSize + 8))
        + 20,
        110,
        320);

    public double DrawerSecondaryPanelHeight => Math.Clamp(
        (Math.Min(5, DrawerSecondaryRows)
            * (LayoutSettings.DrawerPrimaryIconFrameSize + 8))
        + 20,
        96,
        320);

    public bool IsMappingListMode => IsMappingBox && _isMappingListMode;

    public bool IsGridMode => !IsMappingListMode;

    public string TypeLabel => _box.Type switch
    {
        BoxType.Normal or BoxType.Pixel => "普通",
        BoxType.Mapping => "映射",
        BoxType.Todo => "便签",
        BoxType.Note => "笔记",
        BoxType.Drawer => "抽屉",
        BoxType.Project => "项目收纳盒",
        BoxType.ProjectFolder => "项目文件夹",
        BoxType.Bound => "目标",
        _ => "未知"
    };

    public string Description => _box.Type switch
    {
        BoxType.Normal or BoxType.Pixel => "移动收纳",
        BoxType.Mapping => "路径映射",
        BoxType.Todo => "桌面便签",
        BoxType.Note => "Markdown 笔记",
        BoxType.Drawer => "点击展开",
        BoxType.Project => "项目阶段与模块",
        BoxType.ProjectFolder => "项目视觉分组",
        BoxType.Bound => "绑定硬盘文件夹",
        _ => string.Empty
    };

    public string ItemCountLabel => IsNoteBox
        ? $"{NoteCharacterCount} 字"
        : $"{(IsTodoBox
            ? TodoItems.Count
            : IsProjectBox
                ? ProjectIssues.Count
                : IsProjectFolder
                    ? ProjectFolderMembers.Count
                    : Items.Count)} 项";

    public bool IsEmpty => IsProjectBox
        ? ProjectIssues.Count == 0
        : IsProjectFolder
            ? ProjectFolderMembers.Count == 0
            : Items.Count == 0;

    public bool ShowFileEmptyState => !IsProjectBox && !IsProjectFolder && ShouldShowFileEmptyState(
        IsTodoBox || IsNoteBox,
        IsEmpty,
        IsDrawerCollapsed);

    internal static bool ShouldShowFileEmptyState(
        bool isTodoBox,
        bool isEmpty,
        bool isDrawerCollapsed) =>
        !isTodoBox && isEmpty && !isDrawerCollapsed;

    internal static bool ShouldShowGridDragPreview(
        bool isMappingListMode,
        bool isDrawerCollapsed) =>
        !isMappingListMode && !isDrawerCollapsed;

    internal static bool ShouldShowHeader(
        bool isDrawerBox,
        bool isDrawerExpanded,
        bool isTitleVisible) =>
        isTitleVisible || (isDrawerBox && isDrawerExpanded);

    internal static double CalculateHeaderRowHeight(
        bool isHeaderVisible,
        bool isDrawerBox,
        bool isMappingListMode,
        double contentTopMargin,
        double contentBottomMargin)
    {
        if (isHeaderVisible)
        {
            return VisibleHeaderRowHeight;
        }

        if (isDrawerBox)
        {
            return 0;
        }

        return isMappingListMode
            ? Math.Max(0, contentBottomMargin - contentTopMargin)
            : HiddenGridContentInset;
    }

    public string NewTodoTitle
    {
        get => _newTodoTitle;
        set
        {
            if (SetProperty(ref _newTodoTitle, value))
            {
                AddTodoCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int TodoRemainingCount => TodoItems.Count(todo => !todo.IsCompleted);

    public int TodoCompletedCount => TodoItems.Count(todo => todo.IsCompleted);

    public double GridCanvasWidth
    {
        get => _gridCanvasWidth;
        private set => SetProperty(ref _gridCanvasWidth, value);
    }

    public double GridCanvasHeight
    {
        get => _gridCanvasHeight;
        private set => SetProperty(ref _gridCanvasHeight, value);
    }

    public bool IsDragPreviewVisible
    {
        get => _isDragPreviewVisible;
        private set => SetProperty(ref _isDragPreviewVisible, value);
    }

    public double DragPreviewLeft
    {
        get => _dragPreviewLeft;
        private set => SetProperty(ref _dragPreviewLeft, value);
    }

    public double DragPreviewTop
    {
        get => _dragPreviewTop;
        private set => SetProperty(ref _dragPreviewTop, value);
    }

    public double DragPreviewWidth => _dragPreviewWidthOverride
        ?? Math.Max(1, LayoutSettings.ItemSlotWidth - (LayoutSettings.ItemSpacing * 2));

    public double DragPreviewHeight => _dragPreviewHeightOverride
        ?? Math.Max(1, LayoutSettings.ItemSlotHeight - (LayoutSettings.ItemSpacing * 2));

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public bool IsDragOver
    {
        get => _isDragOver;
        set => SetProperty(ref _isDragOver, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public void UpdateBox(Box box, BoxVisualStyle visualStyle)
    {
        _box = box;
        _visualStyle = visualStyle;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(VisualStyle));
        OnPropertyChanged(nameof(IsPixelStyle));
        OnPropertyChanged(nameof(IsMappingBox));
        OnPropertyChanged(nameof(IsTodoBox));
        OnPropertyChanged(nameof(IsNoteBox));
        OnPropertyChanged(nameof(IsProjectBox));
        OnPropertyChanged(nameof(IsProjectFolder));
        OnPropertyChanged(nameof(SupportsDesktopActions));
        OnPropertyChanged(nameof(SupportsDesktopGridSizeControls));
        OnPropertyChanged(nameof(SupportsFileManagement));
        OnPropertyChanged(nameof(DesktopDeleteActionLabel));
        OnPropertyChanged(nameof(IsDrawerBox));
        OnPropertyChanged(nameof(IsBoundBox));
        OnPropertyChanged(nameof(IsDrawerExpanded));
        OnPropertyChanged(nameof(IsDrawerCollapsed));
        OnPropertyChanged(nameof(IsTitleVisible));
        OnPropertyChanged(nameof(IsPositionLocked));
        OnPropertyChanged(nameof(PositionLockActionLabel));
        OnPropertyChanged(nameof(IsFileNameVisible));
        OnPropertyChanged(nameof(IsHeaderVisible));
        OnPropertyChanged(nameof(HeaderRowHeight));
        OnPropertyChanged(nameof(DrawerContentHeight));
        OnPropertyChanged(nameof(IsMappingListMode));
        OnPropertyChanged(nameof(IsGridMode));
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowFileEmptyState));
        OnPropertyChanged(nameof(ItemCountLabel));
        AddTodoCommand.NotifyCanExecuteChanged();
        ArchiveCompletedTodosCommand.NotifyCanExecuteChanged();
        SaveNoteCommand.NotifyCanExecuteChanged();
        ToggleNotePreviewCommand.NotifyCanExecuteChanged();
        ToggleNoteCollapsedCommand.NotifyCanExecuteChanged();
        UpdateItemIconSizes();
    }

    public void UpdateIconDisplayMetrics(double dpiScaleX, double dpiScaleY)
    {
        _iconDpiScaleX = NormalizeDpiScale(dpiScaleX);
        _iconDpiScaleY = NormalizeDpiScale(dpiScaleY);
        UpdateItemIconSizes();
    }

    public (int Column, int Row) GetGridSlot(
        double x,
        double y,
        double surfaceWidth = 0,
        double surfaceHeight = 0)
    {
        var column = Math.Max(0, (int)Math.Floor(x / Math.Max(1, LayoutSettings.ItemSlotWidth)));
        var row = Math.Max(0, (int)Math.Floor(y / Math.Max(1, LayoutSettings.ItemSlotHeight)));

        // Edge expansion: when the pointer reaches the right/bottom edge of the *content*
        // grid, target a brand-new column/row so the box grows by one cell. 固定模式下
        // 边缘扩展仍然生效（窗口随内容生长），但最终格位会被钳制在 m×n 上限内。
        // The reference is the item-grid extent ((maxCol+1)*slotWidth), which stays constant
        // while dragging. Using the live window/IconList size here would create a feedback
        // loop: expanding grows the window, which moves the edge away from the pointer, which
        // un-expands, which shrinks the window... — the box would flicker at the threshold.

        if (surfaceWidth > 0 && surfaceHeight > 0)
        {
            // 画布刚因预览扩展而改尺寸后的极短窗口内，指针坐标读取处于布局过渡态，
            // 会读出瞬时错位值：此时直接保持当前预览格，等布局稳定后再跟随指针。
            // 否则扩展帧与错位帧交替 → 扩展/收缩来回打摆（空盒上表现为疯狂频闪）。
            // 50ms ≈ 60Hz 下 3 帧 / 120Hz 下 6 帧，足够覆盖过渡态又不会影响跟随手感。
            if (IsDragPreviewVisible
                && (DateTime.UtcNow - _lastCanvasSizeChangedUtc).TotalMilliseconds < 50)
            {
                return (_previewColumn, _previewRow);
            }

            var maxCol = Items.Count == 0 ? 0 : Items.Max(item => item.GridColumn);
            var maxRow = Items.Count == 0 ? 0 : Items.Max(item => item.GridRow);

            var contentRight = (maxCol + 1) * LayoutSettings.ItemSlotWidth;
            var contentBottom = (maxRow + 1) * LayoutSettings.ItemSlotHeight;

            if (x >= contentRight - EdgeExpandThreshold)
            {
                column = Math.Max(column, maxCol + 1);
            }

            if (y >= contentBottom - EdgeExpandThreshold)
            {
                row = Math.Max(row, maxRow + 1);
            }
        }

        if (IsFixedSize)
        {
            column = Math.Min(column, _sizeMode.Columns - 1);
            row = Math.Min(row, _sizeMode.Rows - 1);
        }

        return (column, row);
    }

    public void UpdateDragPreview(double x, double y)
    {
        IsDragPreviewVisible = true;
        var column = Math.Max(0, (int)Math.Floor(x / Math.Max(1, LayoutSettings.ItemSlotWidth)));
        var row = Math.Max(0, (int)Math.Floor(y / Math.Max(1, LayoutSettings.ItemSlotHeight)));
        _previewColumn = column;
        _previewRow = row;
    }

    public void ShowDragPreview(int column, int row)
    {
        if (!ShouldShowGridDragPreview(IsMappingListMode, IsDrawerCollapsed))
        {
            // A collapsed drawer shows the cover tiles, not the item grid, so a positional
            // frame cannot line up with what the user sees. Growing the preview canvas
            // here would also resize the SizeToContent window under the stationary
            // cursor, feeding back into the slot calculation and oscillating.
            IsDragPreviewVisible = false;
            return;
        }

        _previewColumn = column;
        _previewRow = row;
        _dragPreviewWidthOverride = null;
        _dragPreviewHeightOverride = null;
        IsDragPreviewVisible = true;
        UpdateGridCanvasSize();

        DragPreviewLeft = (column * LayoutSettings.ItemSlotWidth) + LayoutSettings.ItemSpacing;
        DragPreviewTop = (row * LayoutSettings.ItemSlotHeight) + LayoutSettings.ItemSpacing;
    }

    public void HideDragPreview()
    {
        IsDragPreviewVisible = false;
        _previewColumn = 0;
        _previewRow = 0;
        _dragPreviewWidthOverride = null;
        _dragPreviewHeightOverride = null;
        UpdateGridCanvasSize();
    }

    // Free-form preview used by the collapsed drawer cover: the frame is placed
    // directly over the cover cell the dropped item will occupy, in the preview
    // canvas' coordinate space, instead of using item-grid slot math.
    public void ShowDragPreviewAt(double left, double top, double width, double height)
    {
        _previewColumn = 0;
        _previewRow = 0;
        _dragPreviewWidthOverride = Math.Max(1, width);
        _dragPreviewHeightOverride = Math.Max(1, height);
        DragPreviewLeft = left;
        DragPreviewTop = top;
        IsDragPreviewVisible = true;
        OnPropertyChanged(nameof(DragPreviewWidth));
        OnPropertyChanged(nameof(DragPreviewHeight));
    }

    public (int Column, int Row) GetAvailableDropSlot(int targetColumn, int targetRow, Guid? movingItemId = null)
    {
        var targetSlot = NormalizeGridSlot(targetColumn, targetRow);
        var occupiedSlots = Items
            .Where(item => movingItemId is null || item.Id != movingItemId.Value)
            .Select(item => (item.GridColumn, item.GridRow))
            .ToHashSet();

        return FindFirstFreeSlot(targetSlot.Column, targetSlot.Row, occupiedSlots);
    }

    /// <summary>
    /// 固定模式下的总格数；自适应模式视为无限。
    /// </summary>
    public int FixedCapacity => IsFixedSize ? _sizeMode.Columns * _sizeMode.Rows : int.MaxValue;

    /// <summary>
    /// 固定模式下是否还有空位可以放入（拖入校验用；自适应模式恒为 true）。
    /// </summary>
    public bool HasFreeSlotForDrop(Guid? movingItemId = null)
    {
        if (!IsFixedSize)
        {
            return true;
        }

        var occupied = Items.Count(item => movingItemId is null || item.Id != movingItemId.Value);
        return occupied < FixedCapacity;
    }

    /// <summary>
    /// 自适应模式等价于 <see cref="GetAvailableDropSlot"/>；固定模式把目标格钳制在
    /// m×n 边界内，找不到空位时返回 false（硬约束：放不下就是放不下）。
    /// </summary>
    public bool TryGetAvailableDropSlot(
        int targetColumn,
        int targetRow,
        Guid? movingItemId,
        out (int Column, int Row) slot)
    {
        if (!IsFixedSize)
        {
            slot = GetAvailableDropSlot(targetColumn, targetRow, movingItemId);
            return true;
        }

        var occupiedSlots = Items
            .Where(item => movingItemId is null || item.Id != movingItemId.Value)
            .Select(item => (item.GridColumn, item.GridRow))
            .ToHashSet();

        return TryFindFreeSlotInFixedBounds(targetColumn, targetRow, occupiedSlots, out slot);
    }

    private bool TryFindFreeSlotInFixedBounds(
        int startColumn,
        int startRow,
        HashSet<(int Column, int Row)> occupiedSlots,
        out (int Column, int Row) slot)
    {
        var columns = _sizeMode.Columns;
        var rows = _sizeMode.Rows;
        var preferred = (
            Math.Clamp(startColumn, 0, columns - 1),
            Math.Clamp(startRow, 0, rows - 1));
        if (!occupiedSlots.Contains(preferred))
        {
            slot = preferred;
            return true;
        }

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                if (!occupiedSlots.Contains((column, row)))
                {
                    slot = (column, row);
                    return true;
                }
            }
        }

        slot = preferred;
        return false;
    }

    public (int Column, int Row) GetListDropSlot(Guid? movingItemId = null)
    {
        var maxRow = Items
            .Where(item => movingItemId is null || item.Id != movingItemId.Value)
            .Select(item => item.GridRow)
            .DefaultIfEmpty(-1)
            .Max();

        return (0, maxRow + 1);
    }

    public Task EnsureLoadedAsync() => _hasLoaded ? Task.CompletedTask : LoadAsync();

    public async Task LoadAsync()
    {
        try
        {
            if (IsTodoBox)
            {
                Items.ReplaceAll([]);
                await LoadTodoItemsAsync();
                UpdateGridCanvasSize();
                _hasLoaded = true;
                return;
            }

            if (IsNoteBox)
            {
                Items.ReplaceAll([]);
                await LoadNoteCollapsedAsync();
                await LoadNoteAsync();
                UpdateGridCanvasSize();
                _hasLoaded = true;
                return;
            }

            if (IsProjectBox)
            {
                Items.ReplaceAll([]);
                await LoadProjectAsync();
                UpdateGridCanvasSize();
                _hasLoaded = true;
                return;
            }

            if (IsProjectFolder)
            {
                Items.ReplaceAll([]);
                await LoadProjectFolderAsync();
                UpdateGridCanvasSize();
                _hasLoaded = true;
                return;
            }

            // Each desktop box owns its layout settings. The manager restores the preset
            // before the window is created so boxes can use different icon sizes.

            var items = await _drawerService.GetItemsAsync(BoxId);
            var isPixelated = IsPixelStyle;
            var existingById = Items.ToDictionary(item => item.Id);
            var nextItems = new List<DrawerItemViewModel>(items.Count);

            foreach (var item in items)
            {
                if (!existingById.TryGetValue(item.Id, out var itemViewModel)
                    || itemViewModel.Model != item)
                {
                    itemViewModel = new DrawerItemViewModel(
                        item,
                        Name,
                        isPixelated,
                        GetIconPixelSize(isPixelated),
                        _logger);
                }

                itemViewModel.RequestIconSize(GetIconPixelSize(isPixelated));
                nextItems.Add(itemViewModel);
            }

            if (IsFreeSort)
            {
                // 自由排序：按持久化格位摆放（含无格位项目的空位分配）。
                var positions = ResolveItemPositions(items);
                foreach (var itemViewModel in nextItems)
                {
                    var itemPosition = positions[itemViewModel.Id];
                    itemViewModel.SetGridPosition(itemPosition.Column, itemPosition.Row, LayoutSettings);
                }

                Items.ReplaceAll(nextItems);
            }
            else
            {
                // 自动排序：按排序键行优先展示；不写库，自由布局不受污染。
                var ordered = await Task.Run(() => SortDrawerItems(nextItems, _drawerItemSortMode));
                var sortedPositions = AssignSortedGridPositions(ordered);
                foreach (var itemViewModel in ordered)
                {
                    var itemPosition = sortedPositions[itemViewModel.Id];
                    itemViewModel.SetGridPosition(itemPosition.Column, itemPosition.Row, LayoutSettings);
                }

                Items.ReplaceAll(ordered.ToList());
            }

            StatusText = Items.Count == 0 ? "拖入文件" : "已同步";
            UpdateGridCanvasSize();
            OnPropertyChanged(nameof(ItemCountLabel));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(ShowFileEmptyState));
            RefreshDrawerPreview();
            _hasLoaded = true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load desktop box.");
            StatusText = exception.Message;
        }
    }

    public void ReleaseHiddenWindowItems()
    {
        _hasLoaded = false;
        Items.ReplaceAll([]);
        DrawerPreviewItems.Clear();
        DrawerCoverTiles.Clear();
        DrawerSecondaryItems.ReplaceAll([]);
        TodoItems.Clear();
        ProjectIssues.Clear();
        ProjectLinkedBoxes.Clear();
        ProjectFolderMembers.Clear();
        _project = null;
        _projectFolderId = null;
        _projectIssueModels = [];
        _newProjectIssueTitle = string.Empty;
        _selectedProjectStage = ProjectStage.Research;
        _areProjectModulesExpanded = false;
        _isProjectLeftAttachmentsVisible = true;
        _isProjectRightAttachmentsVisible = true;
        _isProjectTopAttachmentsVisible = true;
        _isProjectBottomAttachmentsVisible = true;
        _projectAssociationMessage = string.Empty;
        _projectLinkedPaperCount = 0;
        _projectActiveIssueCount = 0;
        _projectCompletedIssueCount = 0;
        _projectTotalIssueCount = 0;
        _noteSaveCts?.Cancel();
        _isLoadingNote = true;
        _noteContent = string.Empty;
        _isNotePreview = false;
        _isNoteCollapsed = false;
        _noteStatusText = "自动保存";
        NotePreviewBlocks.Clear();
        _isLoadingNote = false;
        UpdateGridCanvasSize();
        OnPropertyChanged(nameof(ItemCountLabel));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowFileEmptyState));
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(ProjectFolderId));
        OnPropertyChanged(nameof(HasProjectFolderMembership));
        OnPropertyChanged(nameof(SelectedProjectStage));
        OnPropertyChanged(nameof(NewProjectIssueTitle));
        OnPropertyChanged(nameof(NewProjectModuleTitle));
        OnPropertyChanged(nameof(AreProjectModulesExpanded));
        OnPropertyChanged(nameof(ProjectModuleToggleLabel));
        OnPropertyChanged(nameof(ProjectModuleToggleGlyph));
        OnPropertyChanged(nameof(ProjectStageLabel));
        OnPropertyChanged(nameof(ProjectStageColor));
        OnPropertyChanged(nameof(ProjectActiveIssueCount));
        OnPropertyChanged(nameof(ProjectCompletedIssueCount));
        OnPropertyChanged(nameof(ProjectTotalIssueCount));
        OnPropertyChanged(nameof(ProjectCompletionPercentage));
        OnPropertyChanged(nameof(ProjectChecklistSummary));
        OnPropertyChanged(nameof(ProjectModuleSummary));
        OnPropertyChanged(nameof(ProjectBoxWidth));
        OnPropertyChanged(nameof(ProjectBoxMaxHeight));
        OnPropertyChanged(nameof(ProjectModuleListMaxHeight));
        OnPropertyChanged(nameof(ProjectAttachmentCount));
        OnPropertyChanged(nameof(ProjectAttachmentCountLabel));
        OnPropertyChanged(nameof(HasProjectAttachments));
        OnPropertyChanged(nameof(IsProjectLeftAttachmentsVisible));
        OnPropertyChanged(nameof(IsProjectRightAttachmentsVisible));
        OnPropertyChanged(nameof(IsProjectTopAttachmentsVisible));
        OnPropertyChanged(nameof(IsProjectBottomAttachmentsVisible));
        OnPropertyChanged(nameof(ProjectAssociationMessage));
        OnPropertyChanged(nameof(HasProjectAssociationMessage));
        OnPropertyChanged(nameof(NoteContent));
        OnPropertyChanged(nameof(NoteCharacterCount));
        OnPropertyChanged(nameof(IsNotePreview));
        OnPropertyChanged(nameof(NotePreviewButtonText));
        OnPropertyChanged(nameof(IsNoteCollapsed));
        OnPropertyChanged(nameof(NoteCollapsedButtonToolTip));
        OnPropertyChanged(nameof(NoteStatusText));
        OnPropertyChanged(nameof(ItemCountLabel));
        SaveNoteCommand.NotifyCanExecuteChanged();
        ToggleNotePreviewCommand.NotifyCanExecuteChanged();
        ToggleNoteCollapsedCommand.NotifyCanExecuteChanged();
    }

    private bool CanAddTodo()
    {
        return IsTodoBox && !IsBusy && !string.IsNullOrWhiteSpace(NewTodoTitle);
    }

    private async Task AddTodoAsync()
    {
        var title = NewTodoTitle;
        await RunTodoOperationAsync(async () =>
        {
            await _todoService.AddTodoAsync(BoxId, title);
            NewTodoTitle = string.Empty;
            StatusText = "已添加";
        });
    }

    private async Task ToggleTodoAsync(TodoItemViewModel? todo)
    {
        if (todo is null || !IsTodoBox)
        {
            return;
        }

        await RunTodoOperationAsync(async () =>
        {
            await _todoService.SetCompletedAsync(todo.Id, !todo.IsCompleted);
            StatusText = todo.IsCompleted ? "已恢复" : "已完成";
        });
    }

    private bool CanArchiveCompletedTodos()
    {
        return IsTodoBox && !IsBusy && TodoCompletedCount > 0;
    }

    private async Task ArchiveCompletedTodosAsync()
    {
        await RunTodoOperationAsync(async () =>
        {
            var archivedCount = await _todoService.ArchiveCompletedAsync(BoxId);
            StatusText = archivedCount == 0 ? "没有可归档事项" : $"已归档 {archivedCount} 项";
        });
    }

    private async Task DeleteTodoAsync(TodoItemViewModel? todo)
    {
        if (todo is null || !IsTodoBox)
        {
            return;
        }

        await RunTodoOperationAsync(async () =>
        {
            await _todoService.DeleteTodoAsync(todo.Id);
            StatusText = "已删除";
        });
    }

    private async Task RunTodoOperationAsync(Func<Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            AddTodoCommand.NotifyCanExecuteChanged();
            ArchiveCompletedTodosCommand.NotifyCanExecuteChanged();
            await operation();
            await LoadTodoItemsAsync();
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to update todo box.");
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
            AddTodoCommand.NotifyCanExecuteChanged();
            ArchiveCompletedTodosCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task LoadTodoItemsAsync()
    {
        var todos = await _todoService.GetTodosAsync(BoxId);
        TodoItems.Clear();
        foreach (var todo in todos)
        {
            TodoItems.Add(new TodoItemViewModel(todo));
        }

        StatusText = TodoItems.Count == 0 ? "添加待办" : "已同步";
        OnPropertyChanged(nameof(ItemCountLabel));
        OnPropertyChanged(nameof(TodoRemainingCount));
        OnPropertyChanged(nameof(TodoCompletedCount));
        OnPropertyChanged(nameof(ShowFileEmptyState));
        ArchiveCompletedTodosCommand.NotifyCanExecuteChanged();
    }

    private bool CanSaveNote() => IsNoteBox && !_isSavingNote && !_isLoadingNote;

    private void ToggleNotePreview()
    {
        if (!IsNoteBox)
        {
            return;
        }

        IsNotePreview = !IsNotePreview;
    }

    public void ScheduleNoteSave()
    {
        if (!IsNoteBox || _isLoadingNote)
        {
            return;
        }

        _noteSaveCts?.Cancel();
        var cancellation = new CancellationTokenSource();
        _noteSaveCts = cancellation;
        _ = SaveNoteAfterDelayAsync(cancellation);
    }

    private async Task SaveNoteAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(420, cancellation.Token);
            await SaveNoteNowAsync(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private async Task SaveNoteNowAsync()
    {
        await SaveNoteNowAsync(CancellationToken.None);
    }

    public async Task FlushNoteAsync()
    {
        if (!IsNoteBox)
        {
            return;
        }

        _noteSaveCts?.Cancel();
        await SaveNoteNowAsync(CancellationToken.None);
    }

    private async Task SaveNoteNowAsync(CancellationToken cancellationToken)
    {
        if (!IsNoteBox || _isLoadingNote)
        {
            return;
        }

        await _noteSaveGate.WaitAsync(cancellationToken);
        try
        {
            _isSavingNote = true;
            SaveNoteCommand.NotifyCanExecuteChanged();
            var saved = await _noteService.SaveAsync(BoxId, NoteContent, cancellationToken);
            NoteStatusText = $"已保存 {saved.UpdatedAt.ToLocalTime():HH:mm}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save note box.");
            NoteStatusText = "保存失败";
        }
        finally
        {
            _isSavingNote = false;
            SaveNoteCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task ToggleNoteCollapsedAsync()
    {
        if (!IsNoteBox)
        {
            return;
        }

        var collapsed = !_isNoteCollapsed;
        await _drawerService.SetSettingAsync(
            NoteCollapsedSettingPrefix + BoxId.ToString("N"),
            collapsed.ToString());
        _isNoteCollapsed = collapsed;
        OnPropertyChanged(nameof(IsNoteCollapsed));
        OnPropertyChanged(nameof(NoteCollapsedButtonToolTip));
        StatusText = collapsed ? "已折叠为便签胶囊" : "已展开便签";
    }

    internal async Task LoadNoteCollapsedAsync()
    {
        var saved = await _drawerService.GetSettingAsync(
            NoteCollapsedSettingPrefix + BoxId.ToString("N"));
        _isNoteCollapsed = bool.TryParse(saved, out var collapsed) && collapsed;
        OnPropertyChanged(nameof(IsNoteCollapsed));
        OnPropertyChanged(nameof(NoteCollapsedButtonToolTip));
    }

    private async Task LoadNoteAsync()
    {
        var note = await _noteService.EnsureAsync(BoxId);
        _isLoadingNote = true;
        _noteContent = note.Content;
        _isNotePreview = false;
        _isLoadingNote = false;
        ReplaceNotePreview();
        NoteStatusText = $"已保存 {note.UpdatedAt.ToLocalTime():HH:mm}";
        OnPropertyChanged(nameof(NoteContent));
        OnPropertyChanged(nameof(NoteCharacterCount));
        OnPropertyChanged(nameof(NotePreviewButtonText));
        OnPropertyChanged(nameof(NoteStatusText));
        OnPropertyChanged(nameof(ItemCountLabel));
        StatusText = NoteContent.Length == 0 ? "开始写笔记" : "已同步";
        SaveNoteCommand.NotifyCanExecuteChanged();
    }

    private void ReplaceNotePreview()
    {
        NotePreviewBlocks.Clear();
        foreach (var block in NoteMarkdownPreview.Parse(NoteContent))
        {
            NotePreviewBlocks.Add(block);
        }
    }

    private async Task LoadProjectAsync()
    {
        var project = await _projectService.GetOrCreateProjectAsync(BoxId);
        var issues = await _projectService.GetIssuesAsync(
            BoxId,
            includeResolved: true);
        var linkedBoxes = await _projectService.GetLinkedBoxesAsync(BoxId);
        var linkedPapers = await _projectService.GetLinkedPapersAsync(BoxId);
        _projectFolderId = await _projectFolderService.GetFolderForProjectAsync(BoxId);
        await LoadProjectAttachmentVisibilityAsync();

        _project = project;
        _selectedProjectStage = project.Stage;
        _projectIssueModels = issues;
        _projectActiveIssueCount = issues.Count(issue =>
            ProjectIssueCatalog.NormalizeModuleState(issue.ResolutionState) != ProjectResolutionState.Released);
        _projectCompletedIssueCount = issues.Count(issue =>
            ProjectIssueCatalog.NormalizeModuleState(issue.ResolutionState) == ProjectResolutionState.Released);
        _projectTotalIssueCount = issues.Count;
        ApplyProjectIssues();
        ReplaceProjectLinkedBoxes(linkedBoxes);
        _projectLinkedPaperCount = linkedPapers.Count;
        ApplyProjectAttachmentCounts(linkedBoxes, linkedPapers);

        StatusText = _projectActiveIssueCount == 0
            ? "添加项目模块"
            : $"{_projectActiveIssueCount} 个模块待开发或待上线";
        OnPropertyChanged(nameof(Project));
        OnPropertyChanged(nameof(ProjectFolderId));
        OnPropertyChanged(nameof(HasProjectFolderMembership));
        OnPropertyChanged(nameof(SelectedProjectStage));
        OnPropertyChanged(nameof(ProjectStageLabel));
        OnPropertyChanged(nameof(ProjectStageColor));
        OnPropertyChanged(nameof(ProjectActiveIssueCount));
        OnPropertyChanged(nameof(ProjectCompletedIssueCount));
        OnPropertyChanged(nameof(ProjectTotalIssueCount));
        OnPropertyChanged(nameof(ProjectCompletionPercentage));
        OnPropertyChanged(nameof(ProjectChecklistSummary));
        OnPropertyChanged(nameof(ProjectModuleSummary));
        OnPropertyChanged(nameof(ProjectModuleToggleLabel));
        OnPropertyChanged(nameof(ProjectBoxWidth));
        OnPropertyChanged(nameof(ProjectBoxMaxHeight));
        OnPropertyChanged(nameof(ProjectModuleListMaxHeight));
        OnPropertyChanged(nameof(ProjectAttachmentCount));
        OnPropertyChanged(nameof(ProjectAttachmentCountLabel));
        OnPropertyChanged(nameof(HasProjectAttachments));
        OnPropertyChanged(nameof(ItemCountLabel));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowFileEmptyState));
    }

    private async Task LoadProjectFolderAsync()
    {
        var members = await _projectFolderService.GetMembersAsync(BoxId);
        var memberCards = await Task.WhenAll(members.Select(async member =>
        {
            var remainingTodoCount = 0;
            if (_projectTodoCountProvider is not null)
            {
                var paperLinks = await _projectService.GetLinkedPapersAsync(member.ProjectBoxId);
                remainingTodoCount = paperLinks.Sum(link =>
                    _projectTodoCountProvider.GetUnfinishedTodoCount(link.PaperId));
            }

            return new ProjectFolderMemberViewModel(member, remainingTodoCount);
        }));
        ProjectFolderMembers.Clear();
        foreach (var memberCard in memberCards)
        {
            ProjectFolderMembers.Add(memberCard);
        }

        StatusText = $"{ProjectFolderMembers.Count} 个项目";
        OnPropertyChanged(nameof(ItemCountLabel));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private Task OpenProjectFolderMemberAsync(ProjectFolderMemberViewModel? member)
    {
        if (member is not null)
        {
            ProjectFolderMemberOpenRequested?.Invoke(member.ProjectBoxId);
        }

        return Task.CompletedTask;
    }

    private void ApplyProjectIssues()
    {
        ProjectIssues.Clear();
        foreach (var issue in _projectIssueModels.Take(80))
        {
            ProjectIssues.Add(new ProjectIssueViewModel(issue));
        }
    }

    public async Task RefreshProjectLinksAsync()
    {
        if (!IsProjectBox)
        {
            return;
        }

        var linkedBoxes = await _projectService.GetLinkedBoxesAsync(BoxId);
        var linkedPapers = await _projectService.GetLinkedPapersAsync(BoxId);
        ReplaceProjectLinkedBoxes(linkedBoxes);
        _projectLinkedPaperCount = linkedPapers.Count;
        ApplyProjectAttachmentCounts(linkedBoxes, linkedPapers);
        OnPropertyChanged(nameof(ProjectAttachmentCount));
        OnPropertyChanged(nameof(ProjectAttachmentCountLabel));
        OnPropertyChanged(nameof(HasProjectAttachments));
    }

    private void ReplaceProjectLinkedBoxes(IEnumerable<ProjectBoxLink> links)
    {
        ProjectLinkedBoxes.Clear();
        foreach (var link in links)
        {
            ProjectLinkedBoxes.Add(new ProjectLinkedBoxViewModel(link));
        }

        OnPropertyChanged(nameof(ProjectAttachmentCount));
        OnPropertyChanged(nameof(ProjectAttachmentCountLabel));
        OnPropertyChanged(nameof(HasProjectAttachments));
    }

    private int GetProjectAttachmentCount(ProjectAttachmentSide side) =>
        _projectAttachmentCounts.TryGetValue(
            ProjectAttachmentSideCatalog.Normalize(side),
            out var count)
            ? count
            : 0;

    private void ApplyProjectAttachmentCounts(
        IEnumerable<ProjectBoxLink> boxLinks,
        IEnumerable<ProjectPaperLink> paperLinks)
    {
        _projectAttachmentCounts.Clear();
        foreach (var side in boxLinks.Select(link => link.AttachmentSide)
                     .Concat(paperLinks.Select(link => link.AttachmentSide)))
        {
            var normalized = ProjectAttachmentSideCatalog.Normalize(side);
            _projectAttachmentCounts[normalized] = GetProjectAttachmentCount(normalized) + 1;
        }

        OnPropertyChanged(nameof(ProjectLeftAttachmentCount));
        OnPropertyChanged(nameof(ProjectRightAttachmentCount));
        OnPropertyChanged(nameof(ProjectTopAttachmentCount));
        OnPropertyChanged(nameof(ProjectBottomAttachmentCount));
        OnPropertyChanged(nameof(HasProjectLeftAttachments));
        OnPropertyChanged(nameof(HasProjectRightAttachments));
        OnPropertyChanged(nameof(HasProjectTopAttachments));
        OnPropertyChanged(nameof(HasProjectBottomAttachments));
    }

    private bool CanAddProjectIssue() => IsProjectBox
        && !IsBusy
        && !string.IsNullOrWhiteSpace(NewProjectIssueTitle);

    private bool CanSaveProjectStage() => IsProjectBox && !IsBusy && _project is not null;

    private async Task AddProjectIssueAsync()
    {
        var title = NewProjectIssueTitle.Trim();
        await RunProjectOperationAsync(
            async () =>
            {
                await _projectService.AddIssueAsync(
                    BoxId,
                    title,
                    string.Empty,
                    ProjectSolutionState.None,
                    string.Empty,
                    ProjectPriority.Normal,
                    string.Empty,
                    null);
                NewProjectIssueTitle = string.Empty;
            },
            "Failed to add project module.");
    }

    private async Task SaveProjectStageAsync()
    {
        if (_project is null)
        {
            return;
        }

        await RunProjectOperationAsync(
            async () =>
            {
                _project = await _projectService.UpdateProjectAsync(
                    _project with { Stage = SelectedProjectStage });
                ProjectFolderChanged?.Invoke(this, EventArgs.Empty);
            },
            "Failed to update project stage.");
    }

    public async Task<bool> LinkProjectBoxAtSideAsync(
        Guid linkedBoxId,
        ProjectAttachmentSide side,
        CancellationToken cancellationToken = default)
    {
        if (!IsProjectBox || IsBusy)
        {
            return false;
        }

        try
        {
            IsBusy = true;
            await _projectService.LinkBoxAsync(BoxId, linkedBoxId, cancellationToken);
            await _projectService.SetLinkedBoxAttachmentSideAsync(
                BoxId,
                linkedBoxId,
                side,
                cancellationToken);
            await LoadProjectAsync();
            await SetProjectAttachmentSideVisibilityAsync(side, isVisible: true);
            SetProjectAssociationMessage($"已关联文件收纳盒到{ProjectAttachmentSideCatalog.GetLabel(side)}");
            ProjectLinksChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to attach file box to project paper.");
            StatusText = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> MoveProjectBoxHereAtSideAsync(
        Guid linkedBoxId,
        ProjectAttachmentSide side,
        CancellationToken cancellationToken = default)
    {
        if (!IsProjectBox || IsBusy)
        {
            return false;
        }

        try
        {
            IsBusy = true;
            await _projectService.MoveBoxLinkAsync(
                BoxId,
                linkedBoxId,
                side,
                cancellationToken);
            await LoadProjectAsync();
            await SetProjectAttachmentSideVisibilityAsync(side, isVisible: true);
            SetProjectAssociationMessage(
                $"已将文件收纳盒移到{ProjectAttachmentSideCatalog.GetLabel(side)}");
            ProjectLinksChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to move a file box between project papers.");
            StatusText = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> LinkProjectPaperAtSideAsync(
        string paperId,
        ProjectAttachmentSide side,
        CancellationToken cancellationToken = default)
    {
        if (!IsProjectBox || IsBusy)
        {
            return false;
        }

        try
        {
            IsBusy = true;
            await _projectService.LinkPaperAsync(BoxId, paperId, side, cancellationToken);
            await LoadProjectAsync();
            await SetProjectAttachmentSideVisibilityAsync(side, isVisible: true);
            SetProjectAssociationMessage($"已关联桌面便签到{ProjectAttachmentSideCatalog.GetLabel(side)}");
            ProjectLinksChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to attach PaperTodo paper to project paper.");
            StatusText = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ToggleProjectModules()
    {
        if (IsProjectBox)
        {
            AreProjectModulesExpanded = !AreProjectModulesExpanded;
        }
    }

    public bool IsProjectAttachmentSideVisible(ProjectAttachmentSide side) =>
        ProjectAttachmentSideCatalog.Normalize(side) switch
        {
            ProjectAttachmentSide.Left => IsProjectLeftAttachmentsVisible,
            ProjectAttachmentSide.Top => IsProjectTopAttachmentsVisible,
            ProjectAttachmentSide.Bottom => IsProjectBottomAttachmentsVisible,
            _ => IsProjectRightAttachmentsVisible
        };

    internal void SetProjectAssociationMessage(string message)
    {
        _projectAssociationMessage = message?.Trim() ?? string.Empty;
        OnPropertyChanged(nameof(ProjectAssociationMessage));
        OnPropertyChanged(nameof(HasProjectAssociationMessage));
    }

    private async Task ToggleProjectAttachmentSideAsync(ProjectAttachmentSide side)
    {
        if (!IsProjectBox || IsBusy)
        {
            return;
        }

        await SetProjectAttachmentSideVisibilityAsync(side, !IsProjectAttachmentSideVisible(side));
    }

    private async Task SetProjectAttachmentSideVisibilityAsync(
        ProjectAttachmentSide side,
        bool isVisible)
    {
        var normalizedSide = ProjectAttachmentSideCatalog.Normalize(side);
        SetProjectAttachmentSideVisibility(normalizedSide, isVisible);
        try
        {
            await _drawerService.SetSettingAsync(
                GetProjectAttachmentSideVisibleSettingKey(BoxId, normalizedSide),
                isVisible.ToString());
            SetProjectAssociationMessage(
                isVisible
                    ? $"已显示{ProjectAttachmentSideCatalog.GetLabel(normalizedSide)}关联"
                    : $"已隐藏{ProjectAttachmentSideCatalog.GetLabel(normalizedSide)}关联");
            ProjectLinksChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save project attachment side visibility.");
            StatusText = exception.Message;
        }
    }

    private async Task LoadProjectAttachmentVisibilityAsync()
    {
        foreach (var side in new[]
                 {
                     ProjectAttachmentSide.Left,
                     ProjectAttachmentSide.Right,
                     ProjectAttachmentSide.Top,
                     ProjectAttachmentSide.Bottom
                 })
        {
            var raw = await _drawerService.GetSettingAsync(
                GetProjectAttachmentSideVisibleSettingKey(BoxId, side));
            var isVisible = !bool.TryParse(raw, out var parsed) || parsed;
            SetProjectAttachmentSideVisibility(side, isVisible);
        }
    }

    private void SetProjectAttachmentSideVisibility(ProjectAttachmentSide side, bool isVisible)
    {
        switch (ProjectAttachmentSideCatalog.Normalize(side))
        {
            case ProjectAttachmentSide.Left:
                _isProjectLeftAttachmentsVisible = isVisible;
                OnPropertyChanged(nameof(IsProjectLeftAttachmentsVisible));
                break;
            case ProjectAttachmentSide.Top:
                _isProjectTopAttachmentsVisible = isVisible;
                OnPropertyChanged(nameof(IsProjectTopAttachmentsVisible));
                break;
            case ProjectAttachmentSide.Bottom:
                _isProjectBottomAttachmentsVisible = isVisible;
                OnPropertyChanged(nameof(IsProjectBottomAttachmentsVisible));
                break;
            default:
                _isProjectRightAttachmentsVisible = isVisible;
                OnPropertyChanged(nameof(IsProjectRightAttachmentsVisible));
                break;
        }
    }

    private static string GetProjectAttachmentSideVisibleSettingKey(
        Guid boxId,
        ProjectAttachmentSide side) =>
        $"{ProjectAttachmentSideVisibleSettingPrefix}{boxId:N}:{(int)ProjectAttachmentSideCatalog.Normalize(side)}";

    private async Task UpdateProjectModuleStateAsync(ProjectIssueViewModel? module)
    {
        if (module is null || !IsProjectBox || IsBusy)
        {
            return;
        }

        await RunProjectOperationAsync(
            async () =>
            {
                await _projectService.UpdateIssueAsync(module.ToModel());
            },
            "Failed to update project module state.");
    }

    private async Task DeleteProjectModuleAsync(ProjectIssueViewModel? module)
    {
        if (module is null || !IsProjectBox || IsBusy)
        {
            return;
        }

        var deleted = await RunProjectOperationAsync(
            () => _projectService.DeleteIssueAsync(module.Id),
            "Failed to delete project module.");
        if (deleted)
        {
            StatusText = $"已删除模块“{module.Title}”";
        }
    }

    private async Task ToggleProjectIssueAsync(ProjectIssueViewModel? issue)
    {
        if (issue is null || !IsProjectBox || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            if (issue.IsResolved)
            {
                await _projectService.ReopenIssueAsync(issue.Id);
                StatusText = "模块已恢复为未开发";
            }
            else
            {
                await _projectService.ResolveIssueAsync(issue.Id);
                StatusText = "模块已标记为上线完成";
            }

            await LoadProjectAsync();
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to toggle project checklist item.");
            StatusText = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> RunProjectOperationAsync(
        Func<Task> operation,
        string logMessage)
    {
        if (!IsProjectBox || IsBusy)
        {
            return false;
        }

        try
        {
            IsBusy = true;
            await operation();
            await LoadProjectAsync();
            ItemsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, logMessage);
            StatusText = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
            AddProjectIssueCommand.NotifyCanExecuteChanged();
            SaveProjectStageCommand.NotifyCanExecuteChanged();
        }
    }

    public Task ImportPathsAsync(IEnumerable<string> paths)
    {
        return ImportPathsAsync(paths, null, null);
    }

    public async Task<IReadOnlyList<Guid>> ImportPathsAsync(IEnumerable<string> paths, int? startColumn, int? startRow)
    {
        var pathList = paths.ToArray();
        if (pathList.Length == 0 || IsBusy)
        {
            return Array.Empty<Guid>();
        }

        try
        {
            IsBusy = true;
            var importedIds = new List<Guid>(pathList.Length);
            var reservedSlots = IsFreeSort
                ? ResolveItemPositions(Items.Select(item => item.Model).ToArray())
                    .Values
                    .ToHashSet()
                : new HashSet<(int Column, int Row)>();
            var nextColumn = startColumn ?? 0;
            var nextRow = startRow ?? 0;
            foreach (var path in pathList)
            {
                if (!IsFreeSort)
                {
                    // 排序模式：不写格位（显示位置由排序键决定），自由布局不受污染；
                    // 固定盒容量硬约束仍然生效：装满即停止导入。
                    if (IsFixedSize && Items.Count + importedIds.Count >= FixedCapacity)
                    {
                        break;
                    }

                    var sortedImport = await _drawerService.ImportPathAsync(BoxId, path);
                    importedIds.Add(sortedImport.Id);
                    continue;
                }

                (int Column, int Row) slot;
                if (IsFixedSize)
                {
                    // 硬约束：固定模式装满即停止导入，剩余文件保持原样。
                    if (!TryFindFreeSlotInFixedBounds(nextColumn, nextRow, reservedSlots, out slot))
                    {
                        break;
                    }
                }
                else
                {
                    slot = FindFirstFreeSlot(nextColumn, nextRow, reservedSlots);
                }

                reservedSlots.Add(slot);
                var importedItem = await _drawerService.ImportPathAsync(BoxId, path, slot.Column, slot.Row);
                importedIds.Add(importedItem.Id);
                nextColumn = slot.Column + 1;
                nextRow = slot.Row;
            }

            await LoadAsync();
            StatusText = importedIds.Count < pathList.Length
                ? importedIds.Count > 0
                    ? $"已收纳 {importedIds.Count} 项，盒子已满"
                    : "盒子已满，无法收纳"
                : $"已收纳 {importedIds.Count} 项";
            ItemsChanged?.Invoke(this, EventArgs.Empty);
            return importedIds;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to import into desktop box.");
            StatusText = exception.Message;
            return Array.Empty<Guid>();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<Guid> CreateFileSystemItemAsync(ItemKind itemKind)
    {
        if (!SupportsFileManagement || IsBusy)
        {
            return Guid.Empty;
        }

        try
        {
            IsBusy = true;
            var desiredName = itemKind == ItemKind.Directory
                ? "新建文件夹"
                : "新建文本文档.txt";
            var item = await _drawerService.CreateFileSystemItemAsync(BoxId, itemKind, desiredName);
            await LoadAsync();
            StatusText = itemKind == ItemKind.Directory ? "已新建文件夹" : "已新建文本文档";
            ItemsChanged?.Invoke(this, EventArgs.Empty);
            return item.Id;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to create a desktop box file-system item.");
            StatusText = exception.Message;
            return Guid.Empty;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> RenameFileSystemItemAsync(
        DrawerItemViewModel item,
        string newName)
    {
        if (!SupportsFileManagement || IsBusy || !Items.Contains(item))
        {
            return false;
        }

        try
        {
            IsBusy = true;
            var renamed = await _drawerService.RenameFileSystemItemAsync(item.Id, newName);
            await LoadAsync();
            StatusText = $"已改名为 {renamed.DisplayName}";
            ItemsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to rename a desktop box file-system item.");
            StatusText = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<IReadOnlyList<Guid>> CopyPathsIntoBoxAsync(IEnumerable<string> paths)
    {
        if (!SupportsFileManagement || IsBusy)
        {
            return [];
        }

        try
        {
            IsBusy = true;
            var pathList = paths.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray();
            if (IsFixedSize)
            {
                pathList = pathList.Take(Math.Max(0, FixedCapacity - Items.Count)).ToArray();
            }

            if (pathList.Length == 0)
            {
                StatusText = IsFixedSize ? "盒子已满，无法粘贴" : "剪贴板中没有文件";
                return [];
            }

            var copied = await _drawerService.CopyPathsIntoBoxAsync(BoxId, pathList);
            await LoadAsync();
            StatusText = $"已粘贴 {copied.Count} 项";
            ItemsChanged?.Invoke(this, EventArgs.Empty);
            return copied.Select(item => item.Id).ToArray();
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to paste files into a desktop box.");
            StatusText = exception.Message;
            return [];
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> DropDrawerItemAsync(Guid itemId, int targetColumn, int targetRow)
    {
        if (IsBusy)
        {
            return false;
        }

        try
        {
            IsBusy = true;
            var movedAcrossBoxes = false;
            var currentItem = Items.FirstOrDefault(item => item.Id == itemId);
            if (currentItem is not null)
            {
                // 排序模式：盒内拖动不换位（显示顺序由排序键决定），落放为空操作。
                if (IsFreeSort)
                {
                    await MoveItemWithinBoxAsync(currentItem, targetColumn, targetRow);
                }
            }
            else
            {
                if (IsFreeSort)
                {
                    var occupiedSlots = Items.Select(item => (item.GridColumn, item.GridRow)).ToHashSet();
                    (int Column, int Row) targetSlot;
                    if (IsFixedSize)
                    {
                        // 硬约束：目标盒已满时拒绝跨盒移入。
                        if (!TryFindFreeSlotInFixedBounds(targetColumn, targetRow, occupiedSlots, out targetSlot))
                        {
                            StatusText = "目标收纳盒已满";
                            return false;
                        }
                    }
                    else
                    {
                        targetSlot = FindFirstFreeSlot(targetColumn, targetRow, occupiedSlots);
                    }
                    await _drawerService.MoveItemToBoxAsync(itemId, BoxId, targetSlot.Column, targetSlot.Row);
                }
                else
                {
                    // 排序模式：固定盒容量校验后直接移入，不写格位。
                    if (IsFixedSize && !HasFreeSlotForDrop())
                    {
                        StatusText = "目标收纳盒已满";
                        return false;
                    }

                    await _drawerService.MoveItemToBoxAsync(itemId, BoxId);
                }

                await LoadAsync();
                movedAcrossBoxes = true;
            }

            if (movedAcrossBoxes)
            {
                ItemsChanged?.Invoke(this, EventArgs.Empty);
            }

            return true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to move desktop box item.");
            StatusText = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task CompleteDragOutAsync(DrawerItemViewModel? item)
    {
        return DeleteItemAsync(item);
    }

    public async Task<bool> ExportItemToDesktopAsync(DrawerItemViewModel? item)
    {
        if (item is null || IsBusy)
        {
            return false;
        }

        try
        {
            IsBusy = true;
            var desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktopDirectory))
            {
                desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            var exportedPath = await _drawerService.ExportItemToDirectoryAsync(item.Id, desktopDirectory);
            ShellChangeNotifier.NotifyFolderItemCreated(
                exportedPath,
                item.Model.ItemKind == ItemKind.Directory);
            await LoadAsync();
            StatusText = $"已移到桌面：{Path.GetFileName(exportedPath)}";
            ItemsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to export desktop box item.");
            StatusText = exception.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadMappingViewModeAsync()
    {
        if (!IsMappingBox)
        {
            return;
        }

        try
        {
            var savedMode = await _drawerService.GetSettingAsync(MappingViewModeSettingPrefix + BoxId.ToString("N"));
            SetMappingListMode(string.Equals(savedMode, MappingListViewMode, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to load mapping view mode.");
        }
    }

    private async Task SetMappingViewModeAsync(bool useListMode)
    {
        if (!IsMappingBox)
        {
            return;
        }

        try
        {
            SetMappingListMode(useListMode);
            var mode = useListMode ? MappingListViewMode : MappingGridViewMode;
            await _drawerService.SetSettingAsync(MappingViewModeSettingPrefix + BoxId.ToString("N"), mode);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to save mapping view mode.");
        }
    }

    private void SetMappingListMode(bool value)
    {
        if (SetProperty(ref _isMappingListMode, value, nameof(IsMappingListMode)))
        {
            OnPropertyChanged(nameof(IsGridMode));
            OnPropertyChanged(nameof(HeaderRowHeight));
            HideDragPreview();
            UpdateItemIconSizes();
        }
    }

    private async Task OpenItemAsync(DrawerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            await _drawerService.OpenItemAsync(item.Id, _launcher);
            StatusText = $"已打开 {item.DisplayName}";
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to open desktop box item.");
            StatusText = exception.Message;
        }
    }

    private async Task DeleteItemAsync(DrawerItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            var result = await _drawerService.DeleteItemAsync(item.Id);
            await LoadAsync();
            StatusText = result.StatusMessage;
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            _logger.Error(exception, "Failed to delete desktop box item.");
            StatusText = exception.Message;
        }
    }

    private async Task MoveItemWithinBoxAsync(DrawerItemViewModel item, int targetColumn, int targetRow)
    {
        var targetSlot = NormalizeGridSlot(targetColumn, targetRow);
        targetColumn = targetSlot.Column;
        targetRow = targetSlot.Row;

        if (item.GridColumn == targetColumn && item.GridRow == targetRow)
        {
            return;
        }

        var occupiedSlots = Items
            .Where(candidate => candidate.Id != item.Id)
            .Select(candidate => (candidate.GridColumn, candidate.GridRow))
            .ToHashSet();
        var availableSlot = IsFixedSize
            ? TryFindFreeSlotInFixedBounds(targetColumn, targetRow, occupiedSlots, out var fixedSlot)
                ? fixedSlot
                : (Column: item.GridColumn, Row: item.GridRow)
            : FindFirstFreeSlot(targetColumn, targetRow, occupiedSlots);
        targetColumn = availableSlot.Column;
        targetRow = availableSlot.Row;

        await _drawerService.UpdateItemGridPositionAsync(item.Id, targetColumn, targetRow);
        item.SetGridPosition(targetColumn, targetRow, LayoutSettings);
        UpdateGridCanvasSize();
    }

    /// <summary>
    /// 自动排序模式的格位分配：按排序后的顺序行优先填充。不写库——仅显示层。
    /// 自适应模式沿用当前内容列宽（至少 4 列）；固定模式 wrap 到 m 列并钳制在边界内。
    /// </summary>
    private Dictionary<Guid, (int Column, int Row)> AssignSortedGridPositions(
        IReadOnlyList<DrawerItemViewModel> orderedItems)
    {
        var wrapColumns = IsFixedSize
            ? Math.Max(1, _sizeMode.Columns)
            : Math.Max(4, _occupiedColumns);
        var positions = new Dictionary<Guid, (int Column, int Row)>(orderedItems.Count);
        for (var index = 0; index < orderedItems.Count; index++)
        {
            var column = index % wrapColumns;
            var row = index / wrapColumns;
            positions[orderedItems[index].Id] = (column, row);
        }

        return positions;
    }

    private Dictionary<Guid, (int Column, int Row)> ResolveItemPositions(IReadOnlyList<DrawerItem> items)
    {
        var positions = new Dictionary<Guid, (int Column, int Row)>();
        var usedSlots = new HashSet<(int Column, int Row)>();
        var nextColumn = 0;
        var nextRow = 0;
        var maxUsedColumn = 0;

        foreach (var item in items)
        {
            (int Column, int Row)? persisted = item.GridColumn >= 0 && item.GridRow >= 0
                ? (item.GridColumn.Value, item.GridRow.Value)
                : null;
            // 固定规格只限制可见列宽；超出列宽的旧格位需要按当前列数重新换行，
            // 行方向允许超过视口并通过滚动查看。
            if (persisted is { } persistedSlot
                && IsFixedSize
                && persistedSlot.Column >= _sizeMode.Columns)
            {
                persisted = null;
            }

            (int Column, int Row) slot;
            if (persisted is { } validSlot && !usedSlots.Contains(validSlot))
            {
                slot = validSlot;
            }
            else if (IsFixedSize)
            {
                slot = FindFirstFreeViewportSlot(
                    0,
                    0,
                    usedSlots,
                    _sizeMode.Columns);
            }
            else
            {
                slot = FindFirstFreeSlot(
                    0,
                    0,
                    usedSlots,
                    maxUsedColumn);
            }

            usedSlots.Add(slot);
            positions[item.Id] = slot;
            maxUsedColumn = Math.Max(maxUsedColumn, slot.Column);
            nextColumn = slot.Column + 1;
            nextRow = slot.Row;
        }

        return positions;
    }

    private static (int Column, int Row) FindFirstFreeViewportSlot(
        int startColumn,
        int startRow,
        HashSet<(int Column, int Row)> occupiedSlots,
        int viewportColumns)
    {
        var columns = Math.Max(1, viewportColumns);
        var index = (Math.Max(0, startRow) * columns) + Math.Max(0, startColumn);
        while (occupiedSlots.Contains((index % columns, index / columns)))
        {
            index++;
        }

        return (index % columns, index / columns);
    }

    private (int Column, int Row) FindFirstFreeSlot(
        int startColumn,
        int startRow,
        HashSet<(int Column, int Row)> occupiedSlots,
        int? knownMaxOccupiedColumn = null)
    {

        var column = Math.Max(0, startColumn);
        var row = Math.Max(0, startRow);
        var maxOccupiedColumn = knownMaxOccupiedColumn
            ?? (occupiedSlots.Count > 0 ? occupiedSlots.Max(slot => slot.Column) : 0);
        var wrapColumn = Math.Max(4, Math.Max(column, maxOccupiedColumn));

        while (occupiedSlots.Contains((column, row)))
        {
            column++;
            if (column > wrapColumn)
            {
                column = Math.Max(0, startColumn);
                row++;
            }
        }

        return (column, row);
    }

    private (int Column, int Row) NormalizeGridSlot(int column, int row)
    {
        return (Math.Max(0, column), Math.Max(0, row));
    }

    private void UpdateGridCanvasSize()
    {
        var maxCol = Items.Count == 0 ? 0 : Items.Max(item => item.GridColumn);
        var maxRow = Items.Count == 0 ? 0 : Items.Max(item => item.GridRow);

        // 内容实际撑开的格子范围（不含拖拽预览），供规格面板显示滚动内容规模。
        PublishGridExtentIfChanged(maxCol + 1, maxRow + 1);

        // While a drag preview is showing, grow the canvas just enough to include the previewed
        // slot, so dropping at the right/bottom edge visibly extends the box by one cell and it
        // shrinks back as soon as the pointer moves off the edge (or the preview is hidden on
        // drop / leave). The edge threshold itself is anchored to the item grid (see
        // GetGridSlot), so this no longer oscillates continuously — at most a brief flicker when
        // the pointer sits right on the boundary.
        if (IsDragPreviewVisible)
        {
            maxCol = Math.Max(maxCol, _previewColumn);
            maxRow = Math.Max(maxRow, _previewRow);
        }

        foreach (var item in Items)
        {
            item.SetTempOffset(0, 0, LayoutSettings);
        }

        if (IsFixedSize)
        {
            GridCanvasWidth = Math.Max(SizeMode.Columns, maxCol + 1)
                * LayoutSettings.ItemSlotWidth;
            GridCanvasHeight = Math.Max(SizeMode.Rows, maxRow + 1)
                * LayoutSettings.ItemSlotHeight;
        }
        else
        {
            GridCanvasWidth = Math.Max(1, maxCol + 1) * LayoutSettings.ItemSlotWidth;
            GridCanvasHeight = Math.Max(1, maxRow + 1) * LayoutSettings.ItemSlotHeight;
        }

        // 记录画布尺寸实际变化的时刻：GetGridSlot 在此后的极短窗口内冻结落点计算。
        if (GridCanvasWidth != _lastCanvasWidth || GridCanvasHeight != _lastCanvasHeight)
        {
            _lastCanvasWidth = GridCanvasWidth;
            _lastCanvasHeight = GridCanvasHeight;
            _lastCanvasSizeChangedUtc = DateTime.UtcNow;
        }

        OnPropertyChanged(nameof(DragPreviewWidth));
        OnPropertyChanged(nameof(DragPreviewHeight));
    }

    private void PublishGridExtentIfChanged(int columns, int rows)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        if (_occupiedColumns == columns && _occupiedRows == rows)
        {
            return;
        }

        _occupiedColumns = columns;
        _occupiedRows = rows;
        OnPropertyChanged(nameof(OccupiedColumns));
        OnPropertyChanged(nameof(OccupiedRows));
        WeakReferenceMessenger.Default.Send(new BoxGridExtentChangedMessage(BoxId, columns, rows));
    }

    /// <summary>
    /// 应用尺寸模式（不触发持久化；持久化由设置页 ViewModel 负责）。
    /// </summary>
    public void ApplySizeMode(BoxSizeModeState state)
    {
        var normalized = SupportsFixedSize && state.IsFixed
            ? new BoxSizeModeState(
                true,
                BoxSizeModeState.ClampColumns(state.Columns),
                BoxSizeModeState.ClampRows(state.Rows))
            : BoxSizeModeState.Adaptive;
        if (_sizeMode == normalized)
        {
            return;
        }

        var shouldReflowGridItems = normalized.IsFixed
            && Items.Count > 0
            && (!_sizeMode.IsFixed || _sizeMode.Columns != normalized.Columns)
            && !IsProjectBox
            && !IsProjectFolder;
        _sizeMode = normalized;
        if (shouldReflowGridItems)
        {
            ReflowItemsForFixedViewport();
        }

        OnPropertyChanged(nameof(SizeMode));
        OnPropertyChanged(nameof(IsFixedSize));
        OnPropertyChanged(nameof(GridViewportWidth));
        OnPropertyChanged(nameof(GridViewportHeight));
        OnPropertyChanged(nameof(GridViewportMaxWidth));
        OnPropertyChanged(nameof(GridViewportMaxHeight));
        OnPropertyChanged(nameof(FixedCapacity));
        OnPropertyChanged(nameof(GridHorizontalScrollBarVisibility));
        OnPropertyChanged(nameof(GridVerticalScrollBarVisibility));
        OnPropertyChanged(nameof(ProjectBoxWidth));
        OnPropertyChanged(nameof(ProjectBoxMaxHeight));
        OnPropertyChanged(nameof(ProjectModuleListMaxHeight));
        OnPropertyChanged(nameof(ProjectFolderColumns));
        OnPropertyChanged(nameof(ProjectFolderVisibleRows));
        OnPropertyChanged(nameof(ProjectFolderWidth));
        OnPropertyChanged(nameof(ProjectFolderScrollMaxHeight));
        OnPropertyChanged(nameof(ProjectFolderMaxHeight));
        DecreaseProjectFolderColumnsCommand.NotifyCanExecuteChanged();
        IncreaseProjectFolderColumnsCommand.NotifyCanExecuteChanged();
        DecreaseProjectFolderRowsCommand.NotifyCanExecuteChanged();
        IncreaseProjectFolderRowsCommand.NotifyCanExecuteChanged();
        UseAdaptiveSizeCommand.NotifyCanExecuteChanged();
        UseFixedSizeCommand.NotifyCanExecuteChanged();
        DecreaseFixedColumnsCommand.NotifyCanExecuteChanged();
        IncreaseFixedColumnsCommand.NotifyCanExecuteChanged();
        DecreaseFixedRowsCommand.NotifyCanExecuteChanged();
        IncreaseFixedRowsCommand.NotifyCanExecuteChanged();
        UpdateGridCanvasSize();
    }

    private Task ResizeDesktopGridAsync(int columns, int rows) =>
        SetDesktopGridSizeAsync(new BoxSizeModeState(
            true,
            BoxSizeModeState.ClampColumns(columns),
            BoxSizeModeState.ClampRows(rows)));

    private async Task SetDesktopGridSizeAsync(BoxSizeModeState state)
    {
        if (!SupportsDesktopGridSizeControls)
        {
            return;
        }

        await _drawerService.SetSettingAsync(
            BoxViewModel.GetSizeModeSettingKey(BoxId),
            state.Serialize());
        ApplySizeMode(state);
        WeakReferenceMessenger.Default.Send(
            new BoxSizeModeChangedMessage(BoxId, state.IsFixed, state.Columns, state.Rows));
    }

    private void ReflowItemsForFixedViewport()
    {
        var columns = Math.Max(1, _sizeMode.Columns);
        for (var index = 0; index < Items.Count; index++)
        {
            Items[index].SetGridPosition(
                index % columns,
                index / columns,
                LayoutSettings);
        }
    }

    private async Task ResizeProjectFolderAsync(int columns, int rows)
    {
        if (!IsProjectFolder)
        {
            return;
        }

        var state = new BoxSizeModeState(
            true,
            Math.Clamp(columns, ProjectFolderMinimumColumns, ProjectFolderMaximumColumns),
            Math.Clamp(rows, ProjectFolderMinimumRows, ProjectFolderMaximumRows));
        await _drawerService.SetSettingAsync(
            BoxViewModel.GetSizeModeSettingKey(BoxId),
            state.Serialize());
        ApplySizeMode(state);
        WeakReferenceMessenger.Default.Send(
            new BoxSizeModeChangedMessage(BoxId, true, state.Columns, state.Rows));
    }

    internal async Task LoadSizeModeAsync()
    {
        var saved = await _drawerService.GetSettingAsync(BoxViewModel.GetSizeModeSettingKey(BoxId));
        ApplySizeMode(BoxSizeModeState.Parse(saved));
    }

    public void ResizeDrawerCover(double width, double height)
    {
        var normalized = NormalizeDrawerCoverSize(width, height, LayoutSettings.DrawerCoverCellSize);
        var widthChanged = SetProperty(
            ref _drawerCoverWidth,
            normalized.Width,
            nameof(DrawerCoverWidth));
        var heightChanged = SetProperty(
            ref _drawerCoverHeight,
            normalized.Height,
            nameof(DrawerCoverHeight));
        var columnsChanged = SetProperty(
            ref _drawerCoverColumns,
            normalized.Columns,
            nameof(DrawerCoverColumns));
        var rowsChanged = SetProperty(
            ref _drawerCoverRows,
            normalized.Rows,
            nameof(DrawerCoverRows));
        if (!widthChanged && !heightChanged && !columnsChanged && !rowsChanged)
        {
            return;
        }

        OnPropertyChanged(nameof(DrawerCoverCapacity));
        OnPropertyChanged(nameof(DrawerHasOverflow));
        OnPropertyChanged(nameof(DrawerDirectItemCount));
        OnPropertyChanged(nameof(DrawerContentHeight));
        RefreshDrawerPreview();
    }

    public async Task LoadDrawerCoverSizeAsync()
    {
        if (!IsDrawerBox)
        {
            return;
        }

        var saved = await _drawerService.GetSettingAsync(GetDrawerCoverSizeSettingKey(BoxId));
        if (TryParseDrawerCoverSize(saved, out var width, out var height))
        {
            ResizeDrawerCover(width, height);
            return;
        }

        ResizeDrawerCover(DefaultDrawerCoverWidth, DefaultDrawerCoverHeight);
    }

    public async Task LoadTitleVisibilityAsync()
    {
        var saved = await _drawerService.GetSettingAsync(GetTitleVisibilitySettingKey(BoxId));
        if (saved is null && IsDrawerBox)
        {
            saved = await _drawerService.GetSettingAsync(
                GetLegacyDrawerTitleVisibilitySettingKey(BoxId));
        }

        ApplyTitleVisibility(!bool.TryParse(saved, out var isVisible) || isVisible);
    }

    public void ApplyTitleVisibility(bool isVisible)
    {
        if (!SetProperty(
                ref _isTitleVisible,
                isVisible,
                nameof(IsTitleVisible)))
        {
            return;
        }

        OnPropertyChanged(nameof(IsHeaderVisible));
        OnPropertyChanged(nameof(HeaderRowHeight));
        OnPropertyChanged(nameof(DrawerContentHeight));
    }

    public void ApplyPositionLockState(bool isPositionLocked)
    {
        if (SetProperty(ref _isPositionLocked, isPositionLocked, nameof(IsPositionLocked)))
        {
            OnPropertyChanged(nameof(PositionLockActionLabel));
        }
    }

    public async Task LoadFileNameVisibilityAsync()
    {
        var saved = await _drawerService.GetSettingAsync(GetFileNameVisibilitySettingKey(BoxId));
        ApplyFileNameVisibility(!bool.TryParse(saved, out var isVisible) || isVisible);
    }

    public void ApplyFileNameVisibility(bool isVisible)
    {
        LayoutSettings.IsFileNameVisible = isVisible;
        SetProperty(
            ref _isFileNameVisible,
            isVisible,
            nameof(IsFileNameVisible));
    }

    public async Task LoadSortModeAsync()
    {
        if (!SupportsSorting)
        {
            return;
        }

        var saved = await _drawerService.GetSettingAsync(GetBoxSortModeSettingKey(BoxId));
        if (saved is null && IsDrawerBox)
        {
            // 迁移抽屉盒旧的 DrawerSortMode: 设置值。
            saved = await _drawerService.GetSettingAsync(GetDrawerSortModeSettingKey(BoxId));
        }

        ApplyDrawerSortMode(
            Enum.TryParse<DrawerItemSortMode>(saved, ignoreCase: true, out var sortMode)
                ? sortMode
                : DrawerItemSortMode.Free);
    }

    /// <summary>
    /// 应用排序模式；返回是否有变化。变化时调用方应触发重新加载以重排显示。
    /// </summary>
    public bool ApplyDrawerSortMode(DrawerItemSortMode sortMode)
    {
        if (_drawerItemSortMode == sortMode)
        {
            return false;
        }

        _drawerItemSortMode = sortMode;
        OnPropertyChanged(nameof(DrawerItemSortMode));
        OnPropertyChanged(nameof(IsFreeSort));
        return true;
    }

    /// <summary>
    /// 自由排序：显示顺序 = 格位/导入顺序（网格盒可拖拽摆放）。
    /// 非自由模式：显示顺序由排序键决定，盒内拖拽换位与格位写入均被禁用，
    /// 因此切回自由时自由布局原样恢复（天然有记忆）。
    /// </summary>
    public bool IsFreeSort => _drawerItemSortMode == DrawerItemSortMode.Free;

    /// <summary>
    /// 排序（自由/名称/大小/类型/修改日期）适用于所有收纳类盒型；待办盒有自己的排序语义。
    /// </summary>
    public bool SupportsSorting => Type is BoxType.Normal or BoxType.Pixel or BoxType.Mapping or BoxType.Drawer or BoxType.Bound;

    public Task SaveDrawerCoverSizeAsync()
    {
        var value = string.Create(
            CultureInfo.InvariantCulture,
            $"{DrawerCoverWidth:0.##},{DrawerCoverHeight:0.##}");
        return _drawerService.SetSettingAsync(GetDrawerCoverSizeSettingKey(BoxId), value);
    }

    internal static string GetDrawerCoverSizeSettingKey(Guid boxId) =>
        $"{DrawerCoverSizeSettingPrefix}{boxId:N}";

    internal static string GetTitleVisibilitySettingKey(Guid boxId) =>
        $"{TitleVisibilitySettingPrefix}{boxId:N}";

    internal static string GetLegacyDrawerTitleVisibilitySettingKey(Guid boxId) =>
        $"{LegacyDrawerTitleVisibilitySettingPrefix}{boxId:N}";

    internal static string GetFileNameVisibilitySettingKey(Guid boxId) =>
        $"{FileNameVisibilitySettingPrefix}{boxId:N}";

    internal static string GetDrawerSortModeSettingKey(Guid boxId) =>
        $"{DrawerSortModeSettingPrefix}{boxId:N}";

    internal static string GetBoxSortModeSettingKey(Guid boxId) =>
        $"BoxSortMode:{boxId:N}";

    internal static bool TryParseDrawerCoverSize(
        string? value,
        out double width,
        out double height)
    {
        width = 0;
        height = 0;
        var parts = value?.Split(',', StringSplitOptions.TrimEntries);
        return parts is { Length: 2 }
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out width)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out height)
            && double.IsFinite(width)
            && double.IsFinite(height)
            && width > 0
            && height > 0;
    }

    internal static (double Width, double Height, int Columns, int Rows) NormalizeDrawerCoverSize(
        double width,
        double height,
        double cellSize)
    {
        var normalizedCellSize = Math.Clamp(cellSize, 24, 120);
        const double surfaceInsets = DesktopBoxLayoutSettings.DrawerSurfaceInset * 2;
        var requestedWidth = double.IsFinite(width) ? width : DefaultDrawerCoverWidth;
        var requestedHeight = double.IsFinite(height) ? height : DefaultDrawerCoverHeight;
        var maximumCells = Math.Max(
            2,
            (int)Math.Floor((MaximumDrawerCoverDimension - surfaceInsets) / normalizedCellSize));
        var columns = Math.Clamp(
            (int)Math.Round(
                Math.Max(1, requestedWidth - surfaceInsets) / normalizedCellSize,
                MidpointRounding.AwayFromZero),
            1,
            maximumCells);
        var rows = Math.Clamp(
            (int)Math.Round(
                Math.Max(1, requestedHeight - surfaceInsets) / normalizedCellSize,
                MidpointRounding.AwayFromZero),
            1,
            maximumCells);
        if (columns * rows < 2 || (columns == 1 && rows == 2))
        {
            // The minimum drawer is always the established horizontal "1 + four previews"
            // shape. A 1x2 cover makes the primary and composite tiles stack vertically and
            // visually turns the already-finished drawer into a different component.
            columns = 2;
            rows = 1;
        }

        return (
            Math.Round((columns * normalizedCellSize) + surfaceInsets, 1),
            Math.Round((rows * normalizedCellSize) + surfaceInsets, 1),
            columns,
            rows);
    }

    internal static int CalculateDrawerDirectItemCount(int itemCount, int capacity)
    {
        var normalizedItemCount = Math.Max(0, itemCount);
        var normalizedCapacity = Math.Max(2, capacity);
        return normalizedItemCount > normalizedCapacity
            ? normalizedCapacity - 1
            : Math.Min(normalizedItemCount, normalizedCapacity);
    }

    internal static double CalculateDrawerContentHeight(
        double coverHeight,
        bool isTitleVisible) => Math.Max(
            1,
            coverHeight - (isTitleVisible ? DrawerTitleHeightCompensation : 0));

    /// <summary>
    /// 展开抽屉二级弹窗前调用：弹窗只展示外层封面装不下的溢出项（顺序与盒内显示顺序
    /// Items，已按排序模式排好保持一致），避免封面已显示的图标在弹窗里重复出现。
    /// </summary>
    public void SyncDrawerSecondaryFromItems()
    {
        // 封面已占据前 DrawerDirectItemCount 个位置；弹窗只承接其后的溢出项。
        // DrawerDirectItemCount 在有溢出时为 封面容量-1（留一格给展开按钮），所以这里的
        // Skip 结果必非空；无溢出时根本没有展开按钮，不会走到这里。
        var overflowItems = Items.Skip(DrawerDirectItemCount).ToArray();
        DrawerSecondaryItems.ReplaceAll(overflowItems);

        OnPropertyChanged(nameof(DrawerSecondaryColumns));
        OnPropertyChanged(nameof(DrawerSecondaryRows));
        OnPropertyChanged(nameof(DrawerSecondaryHasScrollableOverflow));
        OnPropertyChanged(nameof(DrawerSecondaryPanelWidth));
        OnPropertyChanged(nameof(DrawerSecondaryPanelHeight));
    }

    internal static IReadOnlyList<DrawerItemViewModel> SortDrawerItems(
        IReadOnlyList<DrawerItemViewModel> items,
        DrawerItemSortMode sortMode)
    {
        var entries = items.Select(CreateDrawerSortEntry).ToArray();
        IOrderedEnumerable<DrawerSortEntry> ordered = sortMode switch
        {
            // 自由排序：保持原顺序（格位/导入序），排序键不参与。
            DrawerItemSortMode.Free => entries.OrderBy(entry => 0),
            DrawerItemSortMode.Size => entries
                .OrderBy(entry => entry.Size)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            DrawerItemSortMode.ItemType => entries
                .OrderBy(entry => entry.ItemType, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            DrawerItemSortMode.ModifiedDate => entries
                .OrderByDescending(entry => entry.ModifiedDateUtc)
                .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => entries.OrderBy(
                entry => entry.Name,
                StringComparer.CurrentCultureIgnoreCase)
        };

        return ordered.Select(entry => entry.Item).ToArray();
    }

    internal static int CalculateDrawerSecondaryColumns(int itemCount) => Math.Clamp(
        (int)Math.Ceiling(Math.Sqrt(Math.Max(1, itemCount))),
        2,
        5);

    internal static int CalculateDrawerSecondaryRows(int itemCount, int columns) =>
        Math.Max(1, (int)Math.Ceiling(Math.Max(1, itemCount) / (double)Math.Max(1, columns)));

    private static DrawerSortEntry CreateDrawerSortEntry(DrawerItemViewModel item)
    {
        var path = item.PathLabel;
        try
        {
            if (item.Model.ItemKind == ItemKind.Directory)
            {
                return new DrawerSortEntry(
                    item,
                    item.DisplayName,
                    "文件夹",
                    -1,
                    Directory.GetLastWriteTimeUtc(path));
            }

            var fileInfo = new FileInfo(path);
            var itemType = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(itemType))
            {
                itemType = "文件";
            }

            return new DrawerSortEntry(
                item,
                item.DisplayName,
                itemType,
                fileInfo.Exists ? fileInfo.Length : long.MaxValue,
                fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.MinValue);
        }
        catch
        {
            return new DrawerSortEntry(
                item,
                item.DisplayName,
                item.KindLabel,
                long.MaxValue,
                DateTime.MinValue);
        }
    }

    private sealed record DrawerSortEntry(
        DrawerItemViewModel Item,
        string Name,
        string ItemType,
        long Size,
        DateTime ModifiedDateUtc);

    private void RefreshDrawerPreview()
    {
        DrawerCoverTiles.Clear();
        DrawerPreviewItems.Clear();
        var directItemCount = DrawerDirectItemCount;
        for (var index = 0; index < directItemCount; index++)
        {
            DrawerCoverTiles.Add(DrawerCoverTileViewModel.ForItem(Items[index]));
        }

        if (DrawerHasOverflow)
        {
            DrawerCoverTiles.Add(DrawerCoverTileViewModel.Expand());
            foreach (var item in Items.Skip(directItemCount).Take(4))
            {
                DrawerPreviewItems.Add(item);
            }
        }
    }

    private void OnLayoutSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        foreach (var item in Items)
        {
            item.UpdateCanvasPosition(LayoutSettings);
        }

        UpdateItemIconSizes();
        UpdateGridCanvasSize();
        OnPropertyChanged(nameof(HeaderRowHeight));
        OnPropertyChanged(nameof(GridViewportWidth));
        OnPropertyChanged(nameof(GridViewportHeight));
        OnPropertyChanged(nameof(GridViewportMaxWidth));
        OnPropertyChanged(nameof(GridViewportMaxHeight));
        if (IsDrawerBox && e.PropertyName is nameof(DesktopBoxLayoutSettings.CurrentPreset))
        {
            ResizeDrawerCover(
                (DrawerCoverColumns * LayoutSettings.DrawerCoverCellSize)
                + (DesktopBoxLayoutSettings.DrawerSurfaceInset * 2),
                (DrawerCoverRows * LayoutSettings.DrawerCoverCellSize)
                + (DesktopBoxLayoutSettings.DrawerSurfaceInset * 2));
            OnPropertyChanged(nameof(DrawerCoverCapacity));
            OnPropertyChanged(nameof(DrawerHasOverflow));
            OnPropertyChanged(nameof(DrawerDirectItemCount));
            OnPropertyChanged(nameof(DrawerSecondaryPanelWidth));
            OnPropertyChanged(nameof(DrawerSecondaryPanelHeight));
            RefreshDrawerPreview();
        }
    }

    private void UpdateItemIconSizes()
    {
        var iconPixelSize = GetIconPixelSize(IsPixelStyle);
        foreach (var item in Items)
        {
            item.RequestIconSize(iconPixelSize);
        }
    }

    private int GetIconPixelSize(bool isPixelated)
    {
        var displaySizeDip = IsMappingListMode
            ? LayoutSettings.MappingListIconSize
            : IsDrawerBox
                ? Math.Max(LayoutSettings.IconSize, LayoutSettings.DrawerPrimaryIconSize)
                : LayoutSettings.IconSize;

        return DpiAwareIconSize.Calculate(
            displaySizeDip,
            displaySizeDip,
            _iconDpiScaleX,
            _iconDpiScaleY,
            isPixelated);
    }

    private static double NormalizeDpiScale(double value)
    {
        return double.IsFinite(value) && value > 0 ? value : 1;
    }
}
