using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using WitchDrawer.App.Infrastructure;
using WitchDrawer.App.ViewModels;
using WitchDrawer.Core.Models;
using WitchDrawer.Native.Windows;

namespace WitchDrawer.App.Views;

public partial class DesktopBoxWindow : Window
{
    private const string InternalDrawerItemDragFormat = "WitchDrawer.DesktopBoxItem";
    private const string InternalProjectFolderMemberDragFormat = "WitchDrawer.ProjectFolderMember";
    private const double DrawerPopupGap = 8;
    private const double DrawerPopupCollisionPadding = 4;

    private static readonly HashSet<Guid> CompletedInternalDragIds = [];
    private static readonly HashSet<Guid> CompletedInternalItemIds = [];
    private bool _forceClose;
    private Point? _dragStartPoint;
    private DrawerItemViewModel? _dragStartItem;
    private readonly DragOperationGate _itemDragGate = new();
    private readonly ListCollectionView _fileItemsView;
    private DrawerItemViewModel? _keyboardDeleteTarget;
    private DrawerItemViewModel? _fileItemRenameTarget;
    private string _fileItemRenameExtension = string.Empty;
    private Func<Guid, Point?, bool, Task>? _positionChangedCallback;
    private Func<Guid, string, Task>? _renameBoxCallback;
    private Func<Guid, Task>? _togglePositionLockCallback;
    private Func<Guid, Task>? _toggleTitleVisibilityCallback;
    private Func<Guid, Task>? _deleteBoxCallback;
    private Func<Guid, Task>? _returnProjectToFolderCallback;
    private Func<Guid, ProjectFolderDragOutcome, Task>? _projectFolderMemberDraggedOutCallback;
    private Func<Guid, Guid, Task>? _projectFolderMemberReorderedCallback;
    private bool _isMappingViewTransitioning;
    private bool _restoreAfterMinimizeQueued;
    private bool _desktopIsForeground;
    private bool _isPositionLocked;
    private HwndSource? _source;
    private DesktopToolWindow? _nativeWindow;
    private double _drawerResizeStartWidth;
    private double _drawerResizeStartHeight;
    private NativePoint _drawerResizeStartCursor;
    private bool _suppressDrawerItemClick;
    private Point? _projectFolderMemberDragStartPoint;
    private ProjectFolderMemberViewModel? _projectFolderMemberDragSource;

    internal sealed class DesktopBoxDragPayload(Guid dragId, Guid itemId, Guid sourceBoxId)
    {
        private readonly TaskCompletionSource<bool> _dropCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Guid DragId { get; } = dragId;

        public Guid ItemId { get; } = itemId;

        public Guid SourceBoxId { get; } = sourceBoxId;

        public bool WasDroppedInsideWitchDrawer { get; set; }

        public Task<bool> DropCompletion => _dropCompletion.Task;

        public void CompleteDrop(bool succeeded)
        {
            _dropCompletion.TrySetResult(succeeded);
        }

        public static DesktopBoxDragPayload Create(Guid itemId, Guid sourceBoxId)
        {
            return new DesktopBoxDragPayload(Guid.NewGuid(), itemId, sourceBoxId);
        }
    }

    public DesktopBoxWindow(DesktopBoxViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
        _fileItemsView = new ListCollectionView(ViewModel.Items)
        {
            Filter = MatchesFileSearch
        };
        IconList.ItemsSource = _fileItemsView;
        FileList.ItemsSource = _fileItemsView;
        FileSearchBox.TextChanged += OnFileSearchTextChanged;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        DpiChanged += OnDpiChanged;
        SizeChanged += OnWindowSizeChanged;
        AppThemeManager.ThemeChanged += OnThemeChanged;
        AppThemeManager.CrystalBoxTransparencyChanged += OnCrystalBoxTransparencyChanged;
        Activated += OnWindowActivated;
        Deactivated += OnWindowDeactivated;
        StateChanged += OnWindowStateChanged;
        // Desktop boxes often stay non-activated (ShowActivated=false + HWND_BOTTOM/NOACTIVATE).
        // Window.Deactivated therefore never runs after an external drop selection; clear when
        // the whole app loses foreground so a desktop click removes the selected-item chrome.
        Application.Current.Deactivated += OnApplicationDeactivated;
    }

    public DesktopBoxViewModel ViewModel => (DesktopBoxViewModel)DataContext;

    private void SendToBottom()
    {
        // 盒子永远停留在桌面层（桌面壳窗口之上、普通应用窗口之下），不再随前台状态上浮。
        // 桌面父子关系（TryAttachToDesktop）保证 Win+D 显示桌面时盒子跟随桌面一起出现。
        _nativeWindow?.SendToBottom();
    }

    public void QueueSendToBottom()
    {
        SendToBottom();
        Dispatcher.BeginInvoke(new Action(SendToBottom), DispatcherPriority.ApplicationIdle);
    }

    internal void CloseDesktopActionsMenuIfOutside(nint clickedHandle)
    {
        if (!ProjectDesktopActionsPopup.IsOpen
            || clickedHandle == NativeHandle
            || GetDesktopActionsPopupHandle() == clickedHandle)
        {
            return;
        }

        ProjectDesktopActionsPopup.IsOpen = false;
    }

    internal bool IsOwnedInteractiveWindow(nint windowHandle)
    {
        if (windowHandle == nint.Zero)
        {
            return false;
        }

        return windowHandle == NativeHandle
            || GetPresentationHandle(ProjectDesktopActionsPopup.Child) == windowHandle
            || GetPresentationHandle(ProjectDesktopRenamePopup.Child) == windowHandle
            || GetPresentationHandle(FileItemRenamePopup.Child) == windowHandle
            || GetPresentationHandle(FileManagementContextMenu) == windowHandle;
    }

    private nint GetDesktopActionsPopupHandle()
    {
        return GetPresentationHandle(ProjectDesktopActionsPopup.Child);
    }

    private static nint GetPresentationHandle(Visual? visual) =>
        visual is null
            ? nint.Zero
            : (PresentationSource.FromVisual(visual) as HwndSource)?.Handle ?? nint.Zero;

    /// <summary>
    /// 把所有桌面盒压回桌面层。弹窗打开时属主链被 Windows 整体提前，单个盒子沉底不够，
    /// 必须遍历所有盒子窗口统一复位。
    /// </summary>
    internal static void QueueSendToBottomAll()
    {
        if (Application.Current is null)
        {
            return;
        }

        foreach (var window in Application.Current.Windows.OfType<DesktopBoxWindow>())
        {
            window.QueueSendToBottom();
        }
    }

    /// <summary>
    /// 断开弹窗 HWND 的属主关系：之后对弹窗的置顶/沉底不再沿属主链
    /// （盒子→桌面壳→所有盒子）传播。在 Opened 时同步执行，消除窗口期。
    /// </summary>
    private void DetachDrawerPopupOwner()
    {
        if (PresentationSource.FromVisual(DrawerSecondaryPopupRoot) is HwndSource popupSource
            && popupSource.Handle != nint.Zero)
        {
            SetWindowLongPtr(popupSource.Handle, WindowOwnerIndex, 0);
        }
    }

    /// <summary>
    /// 弹窗按"菜单"语义激活：置顶并获取前台。弹窗已断开属主（Opened 时），激活只影响
    /// 弹窗自身——盒子不动。激活后 WPF 原生的 StaysOpen=False 完整生效：
    /// 点击桌面/其他程序/其他盒子都会自动收起，无需额外兜底。
    /// </summary>
    private void BringDrawerPopupToFront()
    {
        if (PresentationSource.FromVisual(DrawerSecondaryPopupRoot) is HwndSource popupSource
            && popupSource.Handle != nint.Zero)
        {
            SetWindowPos(
                popupSource.Handle,
                WindowPositionTopmost,
                0,
                0,
                0,
                0,
                SetWindowPosNoMove | SetWindowPosNoSize | SetWindowPosNoActivate);
            SetForegroundWindow(popupSource.Handle);
        }
    }

    public void SetPositionLocked(bool isPositionLocked)
    {
        if (_isPositionLocked == isPositionLocked)
        {
            return;
        }

        _isPositionLocked = isPositionLocked;
        ViewModel.ApplyPositionLockState(isPositionLocked);

        // A lock transition must never leave a control holding mouse capture.
        // In particular, the old drawer-cover Thumb path could keep a completed
        // locked gesture around and make the next unlocked gesture appear inert.
        if (Mouse.Captured is DependencyObject captured
            && (ReferenceEquals(captured, this) || IsAncestorOf(captured)))
        {
            Mouse.Capture(null);
        }
    }

    public nint NativeHandle => _nativeWindow?.Handle ?? nint.Zero;

    public bool IsNativeWindowAlive => _nativeWindow?.IsAlive == true;

    public void RestoreWithoutActivation()
    {
        _nativeWindow?.RestoreWithoutActivation();
    }

    public bool RefreshDesktopHost()
    {
        return _nativeWindow?.TryAttachToDesktop() == true;
    }

    public void SetDesktopForeground(bool isForeground)
    {
        // 层级已与前台状态解耦：盒子永远留在桌面层，这里只记录状态并保持沉底。
        _desktopIsForeground = isForeground;
        SendToBottom();
    }

    private ListBox ActiveItemsList => ViewModel.IsMappingListMode ? FileList : IconList;

    public void SetPositionChangedCallback(Func<Guid, Point?, bool, Task> callback)
    {
        _positionChangedCallback = callback;
    }

    public void SetProjectBoxActionsCallbacks(
        Func<Guid, string, Task> renameBox,
        Func<Guid, Task> togglePositionLock,
        Func<Guid, Task> toggleTitleVisibility,
        Func<Guid, Task> deleteBox)
    {
        _renameBoxCallback = renameBox;
        _togglePositionLockCallback = togglePositionLock;
        _toggleTitleVisibilityCallback = toggleTitleVisibility;
        _deleteBoxCallback = deleteBox;
    }

    public void SetReturnProjectToFolderCallback(Func<Guid, Task> callback)
    {
        _returnProjectToFolderCallback = callback;
    }

    public void SetProjectFolderMemberDraggedOutCallback(
        Func<Guid, ProjectFolderDragOutcome, Task> callback)
    {
        _projectFolderMemberDraggedOutCallback = callback;
    }

    public void SetProjectFolderMemberReorderedCallback(Func<Guid, Guid, Task> callback)
    {
        _projectFolderMemberReorderedCallback = callback;
    }

    private async void OnReturnProjectToFolder(object sender, RoutedEventArgs e)
    {
        if (_returnProjectToFolderCallback is not null)
        {
            await _returnProjectToFolderCallback(ViewModel.BoxId);
        }

        e.Handled = true;
    }

    private void OnProjectFolderMemberPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is Button { DataContext: ProjectFolderMemberViewModel member })
        {
            _projectFolderMemberDragStartPoint = e.GetPosition(this);
            _projectFolderMemberDragSource = member;
        }
    }

    private void OnProjectFolderMemberPreviewMouseLeftButtonUp(
        object sender,
        MouseButtonEventArgs e)
    {
        ClearPendingProjectFolderMemberDrag();
    }

    private async void OnProjectFolderMemberPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Button dragSource
            || _projectFolderMemberDragStartPoint is not Point startPoint
            || _projectFolderMemberDragSource is not ProjectFolderMemberViewModel member)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ClearPendingProjectFolderMemberDrag();
            return;
        }

        var currentPoint = e.GetPosition(this);
        if (Math.Abs(currentPoint.X - startPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPoint.Y - startPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        ClearPendingProjectFolderMemberDrag();
        var dragWasCanceled = false;
        QueryContinueDragEventHandler queryContinueDrag = (_, args) =>
        {
            if (args.EscapePressed)
            {
                dragWasCanceled = true;
            }
        };

        dragSource.QueryContinueDrag += queryContinueDrag;
        try
        {
            var data = new DataObject(
                InternalProjectFolderMemberDragFormat,
                member.ProjectBoxId);
            DragDrop.DoDragDrop(dragSource, data, DragDropEffects.Move);
            if (dragWasCanceled
                || _projectFolderMemberDraggedOutCallback is null
                || !TryGetCursorScreenPosition(out var releasePoint))
            {
                return;
            }

            var outcome = ProjectFolderInteraction.GetDragOutcome(
                ProjectFolderDragOrigin.MemberCard,
                GetVisibleBounds(),
                releasePoint);
            if (outcome == ProjectFolderDragOutcome.KeepMembership)
            {
                return;
            }

            await _projectFolderMemberDraggedOutCallback(member.ProjectBoxId, outcome);
        }
        finally
        {
            dragSource.QueryContinueDrag -= queryContinueDrag;
            QueueSendToBottom();
        }

        e.Handled = true;
    }

    private void OnProjectFolderMemberDragOver(object sender, DragEventArgs e)
    {
        e.Effects = sender is Button { DataContext: ProjectFolderMemberViewModel target }
                    && e.Data.GetDataPresent(InternalProjectFolderMemberDragFormat)
                    && e.Data.GetData(InternalProjectFolderMemberDragFormat) is Guid sourceProjectId
                    && sourceProjectId != target.ProjectBoxId
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnProjectFolderMemberDrop(object sender, DragEventArgs e)
    {
        if (sender is not Button { DataContext: ProjectFolderMemberViewModel target }
            || !e.Data.GetDataPresent(InternalProjectFolderMemberDragFormat)
            || e.Data.GetData(InternalProjectFolderMemberDragFormat) is not Guid sourceProjectId
            || sourceProjectId == target.ProjectBoxId
            || _projectFolderMemberReorderedCallback is null
            || !TryGetCursorScreenPosition(out var releasePoint))
        {
            e.Handled = true;
            return;
        }

        var outcome = ProjectFolderInteraction.GetDragOutcome(
            ProjectFolderDragOrigin.MemberCard,
            GetVisibleBounds(),
            releasePoint,
            isOverAnotherMember: true);
        if (outcome == ProjectFolderDragOutcome.ReorderMember)
        {
            await _projectFolderMemberReorderedCallback(sourceProjectId, target.ProjectBoxId);
        }

        e.Handled = true;
    }

    private void ClearPendingProjectFolderMemberDrag()
    {
        _projectFolderMemberDragStartPoint = null;
        _projectFolderMemberDragSource = null;
    }

    private void OnExpandDrawerClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SyncDrawerSecondaryFromItems();
        PrepareDrawerSecondaryPopupForOpen();
        if (sender is UIElement centerTarget)
        {
            ConfigureDrawerSecondaryPopupPlacement(centerTarget);
        }

        DrawerSecondaryPopup.IsOpen = true;
        ClearItemSelection();
        e.Handled = true;
    }

    private void PrepareDrawerSecondaryPopupForOpen()
    {
        DrawerSecondaryPopupRoot.BeginAnimation(OpacityProperty, null);
        DrawerSecondaryPopupRoot.Opacity = 0;
        PrepareDrawerPopupScaleForPlacement(DrawerSecondaryPopupScale);
    }

    internal static void PrepareDrawerPopupScaleForPlacement(ScaleTransform scale)
    {
        ArgumentNullException.ThrowIfNull(scale);

        // Popup creates its HWND using the child's current transformed bounds. A
        // reduced scale here shifts the first HWND up/left; when the animation
        // later reaches 1, the full-size content is left at that stale position.
        // Position with a neutral transform and apply the visual scale only after
        // the Popup has opened.
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        scale.ScaleX = 1;
        scale.ScaleY = 1;
    }

    private void ConfigureDrawerSecondaryPopupPlacement(UIElement centerTarget)
    {
        var popupSize = new Size(
            ViewModel.DrawerSecondaryPanelWidth,
            ViewModel.DrawerSecondaryPanelHeight);
        var anchor = GetVisibleBounds();
        var occupiedBounds = Application.Current.Windows
            .OfType<DesktopBoxWindow>()
            .Where(window => window != this && window.IsVisible)
            .Select(window => window.GetVisibleBounds())
            .ToArray();
        var placement = DrawerPopupPlacementSelector.Select(
            anchor,
            popupSize,
            occupiedBounds,
            DrawerPopupGap,
            DrawerPopupCollisionPadding,
            SystemParameters.WorkArea);

        DrawerSecondaryPopup.HorizontalOffset = 0;
        DrawerSecondaryPopup.VerticalOffset = 0;
        if (placement == DrawerPopupPlacement.Center)
        {
            DrawerSecondaryPopup.PlacementTarget = centerTarget;
            DrawerSecondaryPopup.Placement = PlacementMode.Center;
            return;
        }

        // Keep the collision-aware side selected above. Relative Popup placement
        // can be flipped by WPF near a screen edge, potentially putting it back on
        // top of a neighboring box.
        var target = DrawerPopupPlacementSelector.GetCandidateBounds(
            placement,
            anchor,
            popupSize,
            DrawerPopupGap);
        DrawerSecondaryPopup.PlacementTarget = null;
        DrawerSecondaryPopup.Placement = PlacementMode.Absolute;
        DrawerSecondaryPopup.HorizontalOffset = target.Left;
        DrawerSecondaryPopup.VerticalOffset = target.Top;
    }

    /// <summary>
    /// 初始布局稳定后强制重测。SizeToContent 窗口的首次测量以初始 HWND 尺寸为约束，
    /// 若内容之后不再变化（如折叠抽屉盒的封面），窗口会一直停留在错误的初始宽度上
    /// （封面两侧突出）。一次 InvalidateMeasure 即可让窗口贴合真实内容。
    /// </summary>
    internal void ResyncSizeToContent()
    {
        if (SizeToContent != SizeToContent.Manual)
        {
            InvalidateMeasure();
        }
    }

    private Thickness VisibleBoundsMargin =>
        ViewModel.IsProjectBox ? new Thickness(6) : WindowBorder.Margin;

    internal Rect GetVisibleBounds() =>
        ComputeVisibleBounds(Left, Top, ActualWidth, ActualHeight, VisibleBoundsMargin);

    internal bool TryGetCursorScreenPosition(out Point position)
    {
        if (GetCursorPos(out var cursor))
        {
            var localPosition = PointFromScreen(new Point(cursor.X, cursor.Y));
            position = new Point(Left + localPosition.X, Top + localPosition.Y);
            return true;
        }

        position = default;
        return false;
    }

    /// <summary>
    /// <see cref="GetVisibleBounds"/> 的逆运算：把可视区域原点换算回窗口 Left/Top。
    /// 重叠消解在可视区域坐标系里计算，写回窗口位置时必须减去阴影留白 Margin，
    /// 否则每执行一次消解窗口就会按 Margin 平移一次（位置漂移）。
    /// </summary>
    internal void MoveToVisibleOrigin(double visibleLeft, double visibleTop)
    {
        var (left, top) = ComputeWindowOrigin(visibleLeft, visibleTop, VisibleBoundsMargin);
        Left = left;
        Top = top;
    }

    /// <summary>
    /// SizeToContent 窗口以左上角为锚点随内容向右下生长。内容尺寸变化（切换图标预设、
    /// 固定格数、增删项目）可能把右/下边缘推出工作区——表现为盒子边缘被屏幕"吞掉"。
    /// 尺寸变化后把可视区域钳回工作区；只做显示性校正，不写回已保存位置。
    /// </summary>
    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsVisible || e.PreviousSize == e.NewSize)
        {
            return;
        }

        var bounds = GetVisibleBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var workArea = GetWorkAreaDip();
        var visibleLeft = bounds.Left;
        var visibleTop = bounds.Top;
        if (bounds.Right > workArea.Right)
        {
            visibleLeft = workArea.Right - bounds.Width;
        }

        if (bounds.Bottom > workArea.Bottom)
        {
            visibleTop = workArea.Bottom - bounds.Height;
        }

        // 盒子比工作区还大时，左/上钳制优先，保证标题栏可见。
        visibleLeft = Math.Max(workArea.Left, visibleLeft);
        visibleTop = Math.Max(workArea.Top, visibleTop);
        if (Math.Abs(visibleLeft - bounds.Left) > 0.5
            || Math.Abs(visibleTop - bounds.Top) > 0.5)
        {
            MoveToVisibleOrigin(visibleLeft, visibleTop);
        }
    }

    internal static Rect ComputeVisibleBounds(
        double windowLeft,
        double windowTop,
        double windowWidth,
        double windowHeight,
        Thickness margin) =>
        new(
            windowLeft + margin.Left,
            windowTop + margin.Top,
            Math.Max(0, windowWidth - margin.Left - margin.Right),
            Math.Max(0, windowHeight - margin.Top - margin.Bottom));

    internal static (double Left, double Top) ComputeWindowOrigin(
        double visibleLeft,
        double visibleTop,
        Thickness margin) =>
        (visibleLeft - margin.Left, visibleTop - margin.Top);

    private void OnDrawerSecondaryPopupOpened(object? sender, EventArgs e)
    {
        // 弹窗 HWND 属主是盒子窗口，盒子窗口属主是桌面壳。弹窗打开时 Windows 会把
        // 整条属主链提前：所有同属桌面壳的盒子都会被带到应用窗口之上（"全部上浮"）。
        // 第一时间断开弹窗属主，再把所有盒子压回桌面层，弹窗稍后置顶（不被沉底拖下）。
        DetachDrawerPopupOwner();
        QueueSendToBottomAll();
        // 沉底在 ApplicationIdle 还会补一次，而压主窗口沉底会把它的属子弹窗一起拖下去；
        // 置顶必须排在所有沉底调用之后，所以用 SystemIdle 优先级。
        Dispatcher.BeginInvoke(DispatcherPriority.SystemIdle, BringDrawerPopupToFront);

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                var initialScaleX = Math.Clamp(
                    ViewModel.LayoutSettings.DrawerPrimaryIconFrameSize
                        / Math.Max(1, DrawerSecondaryPopupRoot.ActualWidth),
                    0.08,
                    0.24);
                var initialScaleY = Math.Clamp(
                    ViewModel.LayoutSettings.DrawerPrimaryIconFrameSize
                        / Math.Max(1, DrawerSecondaryPopupRoot.ActualHeight),
                    0.08,
                    0.32);
                var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
                var duration = TimeSpan.FromMilliseconds(190);
                DrawerSecondaryPopupRoot.CacheMode = new BitmapCache
                {
                    EnableClearType = true
                };
                DrawerSecondaryPopupScale.BeginAnimation(
                    ScaleTransform.ScaleXProperty,
                    new DoubleAnimation(initialScaleX, 1, duration) { EasingFunction = easing });
                DrawerSecondaryPopupScale.BeginAnimation(
                    ScaleTransform.ScaleYProperty,
                    new DoubleAnimation(initialScaleY, 1, duration) { EasingFunction = easing });
                var opacityAnimation = new DoubleAnimation(
                    0,
                    1,
                    TimeSpan.FromMilliseconds(145))
                {
                    EasingFunction = easing
                };
                opacityAnimation.Completed += (_, _) =>
                    DrawerSecondaryPopupRoot.CacheMode = null;
                DrawerSecondaryPopupRoot.BeginAnimation(OpacityProperty, opacityAnimation);
            });
    }

    private void OnCollapseDrawerClick(object sender, RoutedEventArgs e)
    {
        ViewModel.IsDrawerExpanded = false;
        ClearItemSelection();
        e.Handled = true;
    }

    private void OnDrawerResizeStarted(object sender, DragStartedEventArgs e)
    {
        _drawerResizeStartWidth = ViewModel.DrawerCoverWidth;
        _drawerResizeStartHeight = ViewModel.DrawerCoverHeight;
        GetCursorPos(out _drawerResizeStartCursor);
        e.Handled = true;
    }

    private void OnDrawerResizeDelta(object sender, DragDeltaEventArgs e)
    {
        if (!GetCursorPos(out var currentCursor))
        {
            return;
        }

        var horizontalDelta = currentCursor.X - _drawerResizeStartCursor.X;
        var verticalDelta = currentCursor.Y - _drawerResizeStartCursor.Y;
        var dpi = VisualTreeHelper.GetDpi(this);
        ViewModel.ResizeDrawerCover(
            _drawerResizeStartWidth + (horizontalDelta / Math.Max(0.1, dpi.DpiScaleX)),
            _drawerResizeStartHeight + (verticalDelta / Math.Max(0.1, dpi.DpiScaleY)));
        e.Handled = true;
    }

    private async void OnDrawerResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        if (e.Canceled)
        {
            // 拖拽被取消（如捕获丢失/Alt+Tab 切走）：回滚到拖拽前的尺寸，不保存。
            ViewModel.ResizeDrawerCover(_drawerResizeStartWidth, _drawerResizeStartHeight);
            e.Handled = true;
            return;
        }

        try
        {
            await ViewModel.SaveDrawerCoverSizeAsync();
        }
        catch (Exception exception)
        {
            ViewModel.ResizeDrawerCover(
                _drawerResizeStartWidth,
                _drawerResizeStartHeight);
            _ = exception;
        }

        e.Handled = true;
    }

    private void OnDrawerSurfacePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isPositionLocked
            || e.LeftButton != MouseButtonState.Pressed
            || e.OriginalSource is not DependencyObject source
            || FindVisualAncestor<Button>(source) is not null
            || FindVisualAncestor<Thumb>(source) is not null)
        {
            return;
        }

        e.Handled = true;
        try
        {
            var isExplicitProjectUnlinkRequested = IsExplicitProjectUnlinkModifier(
                Keyboard.Modifiers);
            DragMove();
            var dropPoint = CaptureDragReleasePoint();
            isExplicitProjectUnlinkRequested |= IsExplicitProjectUnlinkModifier(
                Keyboard.Modifiers);
            QueueSendToBottom();
            if (_positionChangedCallback is not null)
            {
                _ = _positionChangedCallback(
                    ViewModel.BoxId,
                    dropPoint,
                    isExplicitProjectUnlinkRequested);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async void OnDrawerIconPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: DrawerCoverTileViewModel { Item: not null } tile })
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            // 双击才打开：单击只选中（与图标网格一致），避免误触直接启动。
            ClearPendingIconDrag();
            await ViewModel.OpenItemCommand.ExecuteAsync(tile.Item);
            e.Handled = true;
            return;
        }

        SelectCoverTile(tile);
        _suppressDrawerItemClick = false;
        _dragStartPoint = e.GetPosition(this);
        _dragStartItem = tile.Item;
    }

    private void SelectCoverTile(DrawerCoverTileViewModel selectedTile)
    {
        foreach (var coverTile in ViewModel.DrawerCoverTiles)
        {
            coverTile.IsSelected = ReferenceEquals(coverTile, selectedTile);
        }

        // 与网格选中互斥：任何时刻全局只有一个选中项。
        IconList.SelectedItem = null;
        FileList.SelectedItem = null;
        _keyboardDeleteTarget = null;
    }

    private async void OnDrawerIconMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStartPoint is null || _dragStartItem is null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ClearPendingIconDrag();
            return;
        }

        IInputElement coordinateSpace = ReferenceEquals(sender, DrawerSecondaryPopupRoot)
            ? DrawerSecondaryPopupRoot
            : this;
        var current = e.GetPosition(coordinateSpace);
        if (Math.Abs(current.X - _dragStartPoint.Value.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - _dragStartPoint.Value.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var drawerItem = _dragStartItem;
        ClearPendingIconDrag();
        if (!_itemDragGate.TryEnter())
        {
            return;
        }

        // 只有弹窗磁贴的拖拽要吞掉随后的 Click；封面磁贴已不挂 Click（双击才打开）。
        _suppressDrawerItemClick = ReferenceEquals(sender, DrawerSecondaryPopupRoot);
        try
        {
            await RunItemDragAsync(drawerItem, sender as UIElement ?? IconList);
        }
        finally
        {
            _itemDragGate.Exit();
        }
    }

    private void OnDrawerSecondaryIconPreviewMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: DrawerItemViewModel item })
        {
            return;
        }

        _suppressDrawerItemClick = false;
        _dragStartPoint = e.GetPosition(DrawerSecondaryPopupRoot);
        _dragStartItem = item;
    }

    private async void OnDrawerSecondaryItemClick(object sender, RoutedEventArgs e)
    {
        if (_suppressDrawerItemClick)
        {
            _suppressDrawerItemClick = false;
            e.Handled = true;
            return;
        }

        if (sender is Button { DataContext: DrawerItemViewModel item })
        {
            await ViewModel.OpenItemCommand.ExecuteAsync(item);
            e.Handled = true;
        }
    }

    private static T? FindVisualAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T typed)
            {
                return typed;
            }
        }

        return null;
    }

    private static T? FindVisualDescendant<T>(DependencyObject source)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(source); index++)
        {
            var child = VisualTreeHelper.GetChild(source, index);
            if (child is T typed)
            {
                return typed;
            }

            if (FindVisualDescendant<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    private void OnFileItemsPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Delta < 0 && FileSearchBar.Visibility == Visibility.Visible)
        {
            FileSearchBar.Visibility = Visibility.Collapsed;
            FileSearchBox.Clear();
            return;
        }

        if (e.Delta <= 0
            || FileSearchBar.Visibility == Visibility.Visible
            || ViewModel.Type is BoxType.Todo or BoxType.Note or BoxType.Project or BoxType.ProjectFolder)
        {
            return;
        }

        var scrollViewer = FindVisualDescendant<ScrollViewer>(ActiveItemsList);
        if (scrollViewer is not null && scrollViewer.VerticalOffset > 0)
        {
            return;
        }

        FileSearchBar.Visibility = Visibility.Visible;
        e.Handled = true;
    }

    private void OnFileSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _fileItemsView.Refresh();
    }

    private bool MatchesFileSearch(object item)
    {
        var query = FileSearchBox.Text.Trim();
        return query.Length == 0
            || item is DrawerItemViewModel drawerItem
            && drawerItem.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            ResetDragVisualState();
            ClearPendingIconDrag();
            Hide();
            ViewModel.ReleaseHiddenWindowItems();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        SourceInitialized -= OnSourceInitialized;
        Loaded -= OnLoaded;
        DpiChanged -= OnDpiChanged;
        AppThemeManager.ThemeChanged -= OnThemeChanged;
        AppThemeManager.CrystalBoxTransparencyChanged -= OnCrystalBoxTransparencyChanged;
        Activated -= OnWindowActivated;
        Deactivated -= OnWindowDeactivated;
        StateChanged -= OnWindowStateChanged;
        _source?.RemoveHook(WindowMessageHook);
        _source = null;
        _nativeWindow = null;
        if (Application.Current is not null)
        {
            Application.Current.Deactivated -= OnApplicationDeactivated;
        }

        base.OnClosed(e);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _nativeWindow = new DesktopToolWindow(handle);
        _nativeWindow.Configure();
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WindowMessageHook);
        QueueSendToBottom();
    }

    private nint WindowMessageHook(
        nint windowHandle,
        int message,
        nint wordParameter,
        nint longParameter,
        ref bool handled)
    {
        if (DesktopToolWindow.IsMinimizeSystemCommand(message, wordParameter))
        {
            // Win+D / Show Desktop normally minimizes top-level windows. A desktop
            // box is desktop furniture, so consume the minimize command.
            handled = true;
        }

        return nint.Zero;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_forceClose
            || WindowState != WindowState.Minimized
            || _restoreAfterMinimizeQueued)
        {
            return;
        }

        // Some shell versions minimize via ShowWindow instead of WM_SYSCOMMAND.
        // Restore after the shell's burst of Z-order changes has settled.
        _restoreAfterMinimizeQueued = true;
        _ = RestoreAfterShellMinimizeAsync();
    }

    private async Task RestoreAfterShellMinimizeAsync()
    {
        await Task.Delay(120).ConfigureAwait(false);
        if (Dispatcher.HasShutdownStarted)
        {
            return;
        }

        await Dispatcher.InvokeAsync(() =>
        {
            _restoreAfterMinimizeQueued = false;
            if (!_forceClose && WindowState == WindowState.Minimized)
            {
                _nativeWindow?.RestoreWithoutActivation();
                // RestoreWithoutActivation no longer changes Z order. Apply exactly
                // one layer operation based on the stabilized desktop state.
                SendToBottom();
            }
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateIconDisplayMetrics(VisualTreeHelper.GetDpi(this));
        ResetDragVisualState();
        ClearPendingIconDrag();
        ApplyThemeAppearance();
        WindowMotion.PopIn(this, 0.97, 140);
        if (ViewModel.IsTodoBox || ViewModel.IsProjectBox || ViewModel.IsProjectFolder || ViewModel.IsNoteBox)
        {
            if (ViewModel.IsTodoBox)
            {
                TodoTitleTextBox.Focus();
            }
            else if (ViewModel.IsNoteBox && !ViewModel.IsNoteCollapsed)
            {
                NoteContentTextBox.Focus();
            }
            else if (ViewModel.IsProjectBox && ViewModel.AreProjectModulesExpanded)
            {
                ProjectIssueTitleTextBox.Focus();
            }
        }
        else
        {
            ActiveItemsList.Focus();
        }
        QueueSendToBottom();
    }

    private void OnDpiChanged(object sender, DpiChangedEventArgs e)
    {
        UpdateIconDisplayMetrics(e.NewDpi);
    }

    private void UpdateIconDisplayMetrics(DpiScale dpi)
    {
        ViewModel.UpdateIconDisplayMetrics(dpi.DpiScaleX, dpi.DpiScaleY);
    }

    private void OnThemeChanged(object? sender, AppTheme theme)
    {
        ApplyThemeAppearance();
    }

    private void OnCrystalBoxTransparencyChanged(object? sender, bool enabled)
    {
        ApplyThemeAppearance();
    }

    private void ApplyThemeAppearance()
    {
        AppThemeManager.ApplyDesktopBoxResources(Resources);
        AppThemeManager.ApplyToWindow(this);
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        QueueSendToBottom();
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        ClearItemSelection();
        ResetDragVisualState();
        QueueSendToBottom();
    }

    private void OnApplicationDeactivated(object? sender, EventArgs e)
    {
        ClearItemSelection();
        ResetDragVisualState();
    }

    /// <summary>
    /// 全局鼠标钩子发现点击落在本盒子之外（桌面/其他程序/其他盒子）时调用。
    /// 盒子带 WS_EX_NOACTIVATE，外部点击不会产生任何 Deactivated 事件，
    /// 选中框只能靠这个显式信号清除。
    /// </summary>
    internal void ClearSelectionFromOutside()
    {
        ClearItemSelection();
    }

    private void ClearItemSelection()
    {
        IconList.SelectedItem = null;
        FileList.SelectedItem = null;
        _keyboardDeleteTarget = null;
        foreach (var coverTile in ViewModel.DrawerCoverTiles)
        {
            coverTile.IsSelected = false;
        }
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ProjectDesktopActionsPopup.IsOpen
            && !IsSourceWithin(e.OriginalSource, ProjectDesktopActionsButton)
            && (ProjectDesktopActionsPopup.Child is not DependencyObject popupContent
                || !IsSourceWithin(e.OriginalSource, popupContent)))
        {
            ProjectDesktopActionsPopup.IsOpen = false;
        }

        // A cancelled external OLE drag can occasionally omit the final DragLeave.
        // A subsequent real click proves that no drag is active, so remove any stale
        // target chrome before routing the click to the item or title bar.
        if (!_itemDragGate.IsEntered)
        {
            ResetAllDragVisualStates();
        }
    }

    private async void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsNoteBox)
        {
            await ViewModel.FlushNoteAsync();
        }

        Close();
    }

    private void OnOpenProjectDesktopActionsMenu(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.SupportsDesktopActions)
        {
            return;
        }

        ProjectDesktopRenamePopup.IsOpen = false;
        ProjectDesktopActionsPopup.IsOpen = !ProjectDesktopActionsPopup.IsOpen;
        e.Handled = true;
    }

    private void OnOpenProjectDesktopRename(object sender, RoutedEventArgs e)
    {
        ProjectDesktopActionsPopup.IsOpen = false;
        ProjectDesktopRenameTextBox.Text = ViewModel.Name;
        ProjectDesktopRenamePopup.IsOpen = true;
        Dispatcher.BeginInvoke(() =>
        {
            Activate();
            ProjectDesktopRenameTextBox.Focus();
            Keyboard.Focus(ProjectDesktopRenameTextBox);
            ProjectDesktopRenameTextBox.SelectAll();
        });
        e.Handled = true;
    }

    private void OnProjectHeaderTitleMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || !ViewModel.SupportsDesktopActions)
        {
            return;
        }

        OnOpenProjectDesktopRename(sender, e);
        e.Handled = true;
    }

    private async void OnConfirmProjectDesktopRename(object sender, RoutedEventArgs e)
    {
        var newName = ProjectDesktopRenameTextBox.Text.Trim();
        ProjectDesktopRenamePopup.IsOpen = false;
        if (string.IsNullOrWhiteSpace(newName))
        {
            MessageBox.Show(this, "名称不能为空。", "收纳盒", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RunProjectDesktopActionAsync(
            callback => callback(ViewModel.BoxId, newName),
            _renameBoxCallback,
            "重命名收纳盒失败。");
        e.Handled = true;
    }

    private void OnCancelProjectDesktopRename(object sender, RoutedEventArgs e)
    {
        ProjectDesktopRenamePopup.IsOpen = false;
        e.Handled = true;
    }

    private void OnProjectDesktopRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnConfirmProjectDesktopRename(sender, e);
        }
        else if (e.Key == Key.Escape)
        {
            OnCancelProjectDesktopRename(sender, e);
        }
    }

    private static bool IsSourceWithin(object? source, DependencyObject ancestor)
    {
        if (source is not DependencyObject dependency)
        {
            return false;
        }

        for (DependencyObject? current = dependency;
             current is not null;
             current = GetInteractiveSurfaceParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private async void OnToggleProjectDesktopPositionLock(object sender, RoutedEventArgs e)
    {
        ProjectDesktopActionsPopup.IsOpen = false;
        await RunProjectDesktopActionAsync(
            callback => callback(ViewModel.BoxId),
            _togglePositionLockCallback,
            "更新收纳盒锁定状态失败。");
        e.Handled = true;
    }

    private async void OnToggleProjectDesktopTitleVisibility(object sender, RoutedEventArgs e)
    {
        ProjectDesktopActionsPopup.IsOpen = false;
        await RunProjectDesktopActionAsync(
            callback => callback(ViewModel.BoxId),
            _toggleTitleVisibilityCallback,
            "更新收纳盒名称显示失败。");
        e.Handled = true;
    }

    private async void OnDeleteProjectDesktopBox(object sender, RoutedEventArgs e)
    {
        ProjectDesktopActionsPopup.IsOpen = false;
        var isFolder = ViewModel.IsProjectFolder;
        var isProject = ViewModel.IsProjectBox;
        var message = isFolder
            ? "解散文件夹后，成员项目会恢复为独立项目，不会删除任何项目内容。"
            : isProject
                ? "删除后，项目阶段、模块和关联记录都会移除。"
                : ViewModel.Type == BoxType.Bound
                    ? "删除收纳盒只会移除桌面入口，目标文件夹中的内容会保留。"
                    : ViewModel.Type == BoxType.Mapping
                        ? "删除收纳盒只会移除映射记录，不会删除原文件。"
                        : "删除收纳盒后，其中的文件会按原位置或桌面进行恢复。";
        var title = isFolder
            ? "解散项目文件夹？"
            : isProject
                ? "删除项目收纳盒？"
                : "删除收纳盒？";
        if (MessageBox.Show(
                this,
                message,
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunProjectDesktopActionAsync(
            callback => callback(ViewModel.BoxId),
            _deleteBoxCallback,
            isFolder ? "解散项目文件夹失败。" : "删除收纳盒失败。");
        e.Handled = true;
    }

    private async Task RunProjectDesktopActionAsync<TCallback>(
        Func<TCallback, Task> invoke,
        TCallback? callback,
        string failureMessage)
        where TCallback : class
    {
        if (!ViewModel.SupportsDesktopActions || callback is null)
        {
            return;
        }

        try
        {
            await invoke(callback);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                failureMessage,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnNoteEditorPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Activate();
        NoteContentTextBox.Focus();
        Keyboard.Focus(NoteContentTextBox);
    }

    private void OnTodoTitlePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 待办输入需要键盘焦点：盒子是 NOACTIVATE，点击本身不激活窗口，
        // 这里显式激活（用户明确要开始输入，盒子短暂到前面、点别处即收回）。
        Activate();
        TodoTitleTextBox.Focus();
        Keyboard.Focus(TodoTitleTextBox);
    }

    private async void OnTodoTitleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !ViewModel.AddTodoCommand.CanExecute(null))
        {
            return;
        }

        e.Handled = true;
        await ViewModel.AddTodoCommand.ExecuteAsync(null);
        TodoTitleTextBox.Focus();
    }

    private void OnProjectIssueTitlePreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Activate();
        ProjectIssueTitleTextBox.Focus();
        Keyboard.Focus(ProjectIssueTitleTextBox);
    }

    private async void OnProjectIssueTitleKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox textBox)
        {
            return;
        }

        textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (!ViewModel.AddProjectIssueCommand.CanExecute(null))
        {
            return;
        }

        e.Handled = true;
        await ViewModel.AddProjectIssueCommand.ExecuteAsync(null);
        textBox.Focus();
        Keyboard.Focus(textBox);
    }

    private async void OnProjectIssueAddClicked(object sender, RoutedEventArgs e)
    {
        Activate();
        if (ViewModel.AddProjectIssueCommand.CanExecute(null))
        {
            await ViewModel.AddProjectIssueCommand.ExecuteAsync(null);
        }

        ProjectIssueTitleTextBox.Focus();
        Keyboard.Focus(ProjectIssueTitleTextBox);
        e.Handled = true;
    }

    private async void OnProjectStageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0
            || e.RemovedItems.Count == 0
            || !ViewModel.SaveProjectStageCommand.CanExecute(null))
        {
            return;
        }

        await ViewModel.SaveProjectStageCommand.ExecuteAsync(null);
    }

    private async void OnProjectModuleStateSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0
            || e.RemovedItems.Count == 0
            || sender is not FrameworkElement { DataContext: ProjectIssueViewModel module }
            || !ViewModel.UpdateProjectModuleStateCommand.CanExecute(module))
        {
            return;
        }

        await ViewModel.UpdateProjectModuleStateCommand.ExecuteAsync(module);
    }

    private async void OnUseMappingGridModeClick(object sender, RoutedEventArgs e)
    {
        await SwitchMappingViewModeAsync(useListMode: false);
    }

    private async void OnUseMappingListModeClick(object sender, RoutedEventArgs e)
    {
        await SwitchMappingViewModeAsync(useListMode: true);
    }

    private async Task SwitchMappingViewModeAsync(bool useListMode)
    {
        if (_isMappingViewTransitioning
            || !ViewModel.IsMappingBox
            || ViewModel.IsMappingListMode == useListMode)
        {
            return;
        }

        _isMappingViewTransitioning = true;
        var incomingList = useListMode ? FileList : IconList;

        try
        {
            var startWidth = Math.Max(MinWidth, ActualWidth);
            var startHeight = Math.Max(MinHeight, ActualHeight);

            // SizeToContent would otherwise apply the target view's desired size in one frame.
            // Freeze the current size first, then animate to the newly measured target size.
            SizeToContent = SizeToContent.Manual;
            Width = startWidth;
            Height = startHeight;
            incomingList.BeginAnimation(OpacityProperty, null);
            incomingList.Opacity = 0;

            var modeChangeTask = useListMode
                ? ViewModel.UseMappingListModeCommand.ExecuteAsync(null)
                : ViewModel.UseMappingGridModeCommand.ExecuteAsync(null);

            await Dispatcher.InvokeAsync(
                () => { },
                DispatcherPriority.DataBind);

            WindowBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var targetWidth = Math.Max(MinWidth, WindowBorder.DesiredSize.Width);
            var targetHeight = Math.Max(MinHeight, WindowBorder.DesiredSize.Height);

            incomingList.Opacity = 1;
            incomingList.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
                {
                    BeginTime = TimeSpan.FromMilliseconds(45),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                });

            await Task.WhenAll(
                modeChangeTask,
                AnimateWindowSizeAsync(startWidth, startHeight, targetWidth, targetHeight));
        }
        finally
        {
            incomingList.BeginAnimation(OpacityProperty, null);
            incomingList.Opacity = 1;
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            SizeToContent = SizeToContent.WidthAndHeight;
            ClearValue(WidthProperty);
            ClearValue(HeightProperty);
            _isMappingViewTransitioning = false;
            QueueSendToBottom();
        }
    }

    private Task AnimateWindowSizeAsync(
        double startWidth,
        double startHeight,
        double targetWidth,
        double targetHeight)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var duration = TimeSpan.FromMilliseconds(220);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        Width = targetWidth;
        Height = targetHeight;

        var widthAnimation = new DoubleAnimation(startWidth, targetWidth, duration)
        {
            EasingFunction = easing
        };
        var heightAnimation = new DoubleAnimation(startHeight, targetHeight, duration)
        {
            EasingFunction = easing
        };
        heightAnimation.Completed += (_, _) => completion.TrySetResult();

        BeginAnimation(WidthProperty, widthAnimation, HandoffBehavior.SnapshotAndReplace);
        BeginAnimation(HeightProperty, heightAnimation, HandoffBehavior.SnapshotAndReplace);

        return completion.Task;
    }

    private void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        // 紧跟 DragLeave 的 DragOver 说明只是 resize churn：取消待执行的复位。
        CancelPendingDragLeaveReset();

        if (ViewModel.IsTodoBox || ViewModel.IsProjectBox || ViewModel.IsProjectFolder)
        {
            ViewModel.IsDragOver = false;
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var acceptsDrop = false;
        var showPreview = false;
        if (e.Data.GetDataPresent(InternalDrawerItemDragFormat))
        {
            acceptsDrop = TryGetInternalDragPayload(e.Data, out var payload);
            // 固定模式（硬约束）：盒已满时拒绝拖入。
        if (acceptsDrop && !ViewModel.HasFreeSlotForDrop(
                payload.SourceBoxId == ViewModel.BoxId ? payload.ItemId : (Guid?)null))
        {
            acceptsDrop = false;
        }

        // 排序模式的落点由排序键决定（盒内拖动为空操作），槽位预览会误导：
        // 只保留盒子高亮，不显示落点框。
        showPreview = acceptsDrop && ViewModel.IsFreeSort;
            e.Effects = acceptsDrop ? DragDropEffects.Move : DragDropEffects.None;
            if (showPreview)
            {
                ShowDropPreview(e, payload);
            }
        }
        else
        {
            var dropEffect = ChooseFileDropEffect(e.AllowedEffects);
            acceptsDrop = e.Data.GetDataPresent(DataFormats.FileDrop) && dropEffect != DragDropEffects.None;
            // 固定模式（硬约束）：盒已满时拒绝拖入文件。
            if (acceptsDrop && !ViewModel.HasFreeSlotForDrop())
            {
                acceptsDrop = false;
            }

            showPreview = acceptsDrop && ViewModel.IsFreeSort;
            e.Effects = acceptsDrop ? dropEffect : DragDropEffects.None;
            if (showPreview)
            {
                ShowDropPreview(e, null);
            }
        }

        if (!showPreview)
        {
            ViewModel.HideDragPreview();
        }

        ViewModel.IsDragOver = acceptsDrop;

        e.Handled = true;
    }

    private void OnPreviewDragLeave(object sender, DragEventArgs e)
    {
        // SizeToContent 窗口随拖拽预览在指针下方生长时，OLE 会补发 DragLeave/DragEnter 对
        // （churn）。若在此同步复位，就会出现"复位→下一帧 DragOver 再显示→再复位"的疯狂频闪。
        // 改为延迟复位：churn 场景紧跟的 DragOver 会取消它；真正离开/取消时没有后续
        // DragOver，复位在极短延迟后生效（肉眼不可辨）。
        var cts = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _dragLeaveResetCts, cts);
        previous?.Cancel();
        previous?.Dispose();
        _ = ResetDragVisualStateAfterSettlingAsync(cts);
    }

    private CancellationTokenSource? _dragLeaveResetCts;

    private async Task ResetDragVisualStateAfterSettlingAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(90, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!cts.IsCancellationRequested)
        {
            ResetDragVisualState();
        }
    }

    private void CancelPendingDragLeaveReset()
    {
        var cts = Interlocked.Exchange(ref _dragLeaveResetCts, null);
        cts?.Cancel();
        cts?.Dispose();
    }

    private async void OnFilesDropped(object sender, DragEventArgs e)
    {
        if (ViewModel.IsTodoBox || ViewModel.IsProjectBox || ViewModel.IsProjectFolder)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            ResetDragVisualState();
            return;
        }

        if (!e.Data.GetDataPresent(InternalDrawerItemDragFormat)
            && !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        e.Handled = true;
        try
        {
            if (e.Data.GetDataPresent(InternalDrawerItemDragFormat))
            {
                if (TryGetInternalDragPayload(e.Data, out var payload))
                {
                    var slot = GetDropSlot(e, payload);
                    if (slot is null)
                    {
                        // 固定模式盒已满：拒绝落放，不标记为内部移动，项目保留在原盒。
                        e.Effects = DragDropEffects.None;
                        return;
                    }

                    e.Effects = DragDropEffects.Move;
                    // Mark synchronously (same object instance, in-process) so the source
                    // box sees it immediately after DoDragDrop returns and treats this as
                    // an internal move/rearrange rather than a move-out to the desktop.
                    payload.WasDroppedInsideWitchDrawer = true;
                    _ = CompleteInternalDropAsync(payload, slot.Value);
                }

                return;
            }

            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                var slot = GetDropSlot(e);
                if (slot is null)
                {
                    // 固定模式盒已满：拒绝导入，文件保持原样。
                    e.Effects = DragDropEffects.None;
                    return;
                }

                e.Effects = paths.Length > 0 ? ChooseFileDropEffect(e.AllowedEffects) : DragDropEffects.None;
                // ImportPathsAsync already reloads the box internally; no extra LoadAsync here.
                var importedIds = await ViewModel.ImportPathsAsync(paths, slot.Value.Column, slot.Value.Row);
                e.Effects = importedIds.Count > 0 ? ChooseFileDropEffect(e.AllowedEffects) : DragDropEffects.None;
                var lastImportedId = importedIds.LastOrDefault();
                var importedItem = lastImportedId != Guid.Empty
                    ? ViewModel.Items.FirstOrDefault(candidate => candidate.Id == lastImportedId)
                    : null;
                if (importedItem is not null)
                {
                    importedItem.ReloadIconIfNeeded();
                    // Only keep keyboard selection while this box actually has focus.
                    // External Explorer drops often leave the window non-activated; a sticky
                    // SelectedItem then cannot be cleared by clicking the desktop.
                    if (IsActive)
                    {
                        ActiveItemsList.SelectedItem = importedItem;
                        _keyboardDeleteTarget = importedItem;
                        ActiveItemsList.Focus();
                    }
                    else
                    {
                        ClearItemSelection();
                    }
                }
            }
        }
        finally
        {
            ResetDragVisualState();
            ResetDragCursor();
        }
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox)
        {
            return;
        }

        var itemList = ActiveItemsList;
        var item = itemList.SelectedItem as DrawerItemViewModel ?? _keyboardDeleteTarget;
        if (ViewModel.SupportsFileManagement
            && e.Key == Key.C
            && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            && item is not null)
        {
            e.Handled = await TryCopyItemToClipboardAsync(item);
            return;
        }

        if (ViewModel.SupportsFileManagement
            && e.Key == Key.V
            && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            await PasteClipboardFilesAsync();
            return;
        }

        if (ViewModel.SupportsFileManagement && e.Key == Key.F2 && item is not null)
        {
            e.Handled = true;
            OpenFileItemRename(item);
            return;
        }

        if (e.Key != Key.Delete)
        {
            return;
        }

        if (item is null || !ViewModel.Items.Contains(item))
        {
            return;
        }

        e.Handled = true;
        await ViewModel.DeleteItemCommand.ExecuteAsync(item);
        _keyboardDeleteTarget = null;
        itemList.Focus();
    }

    private void OnItemsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            _keyboardDeleteTarget = listBox.SelectedItem as DrawerItemViewModel;
        }
    }

    private void OnFileManagementPreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (!ViewModel.SupportsFileManagement)
        {
            return;
        }

        if (TryGetDrawerItem(e.OriginalSource, out var item))
        {
            IconList.SelectedItem = item;
            _keyboardDeleteTarget = item;
        }
        else
        {
            ClearItemSelection();
        }
    }

    private void OnFileManagementContextMenuOpened(object sender, RoutedEventArgs e)
    {
        var selectedItem = IconList.SelectedItem as DrawerItemViewModel;
        RenameFileMenuItem.IsEnabled = selectedItem is not null;
        CopyFileMenuItem.IsEnabled = selectedItem?.Model.EffectivePath is not null;
        PasteFileMenuItem.IsEnabled = ClipboardContainsFileDropList();
        DeleteFileMenuItem.IsEnabled = selectedItem is not null;
    }

    private async void OnCreateFolderClick(object sender, RoutedEventArgs e)
    {
        var itemId = await ViewModel.CreateFileSystemItemAsync(ItemKind.Directory);
        if (itemId != Guid.Empty)
        {
            SelectItem(itemId);
        }
    }

    private async void OnCreateTextFileClick(object sender, RoutedEventArgs e)
    {
        var itemId = await ViewModel.CreateFileSystemItemAsync(ItemKind.File);
        if (itemId != Guid.Empty)
        {
            SelectItem(itemId);
        }
    }

    private void OnRenameFileClick(object sender, RoutedEventArgs e)
    {
        if (IconList.SelectedItem is DrawerItemViewModel item)
        {
            OpenFileItemRename(item);
        }
    }

    private void OpenFileItemRename(DrawerItemViewModel item)
    {
        _fileItemRenameTarget = item;
        _fileItemRenameExtension = item.Model.ItemKind == ItemKind.File
            ? Path.GetExtension(item.Model.DisplayName)
            : string.Empty;
        if (_fileItemRenameExtension.Length == item.Model.DisplayName.Length)
        {
            _fileItemRenameExtension = string.Empty;
        }

        FileItemRenameTextBox.Text = string.IsNullOrEmpty(_fileItemRenameExtension)
            ? item.Model.DisplayName
            : item.Model.DisplayName[..^_fileItemRenameExtension.Length];
        FileItemRenameExtensionText.Text = _fileItemRenameExtension;
        FileItemRenameExtensionContainer.Visibility = string.IsNullOrEmpty(_fileItemRenameExtension)
            ? Visibility.Collapsed
            : Visibility.Visible;
        FileItemRenamePopup.IsOpen = true;
        Dispatcher.BeginInvoke(() =>
        {
            Activate();
            FileItemRenameTextBox.Focus();
            Keyboard.Focus(FileItemRenameTextBox);
            FileItemRenameTextBox.SelectAll();
        });
    }

    private async void OnConfirmFileItemRename(object sender, RoutedEventArgs e)
    {
        var item = _fileItemRenameTarget;
        var newBaseName = FileItemRenameTextBox.Text.Trim();
        if (item is null || string.IsNullOrWhiteSpace(newBaseName))
        {
            return;
        }

        var newName = $"{newBaseName}{_fileItemRenameExtension}";

        FileItemRenamePopup.IsOpen = false;
        _fileItemRenameTarget = null;
        if (await ViewModel.RenameFileSystemItemAsync(item, newName))
        {
            SelectItem(item.Id);
        }
    }

    private void OnCancelFileItemRename(object sender, RoutedEventArgs e)
    {
        FileItemRenamePopup.IsOpen = false;
        _fileItemRenameTarget = null;
    }

    private void OnFileItemRenameKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnConfirmFileItemRename(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            OnCancelFileItemRename(sender, e);
            e.Handled = true;
        }
    }

    private async void OnCopyFileClick(object sender, RoutedEventArgs e)
    {
        if (IconList.SelectedItem is DrawerItemViewModel item)
        {
            await TryCopyItemToClipboardAsync(item);
        }
    }

    private static async Task<bool> TryCopyItemToClipboardAsync(DrawerItemViewModel item)
    {
        var path = item.Model.EffectivePath;
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return false;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var data = new DataObject();
                data.SetData(DataFormats.FileDrop, new[] { path });
                Clipboard.SetDataObject(data, copy: true);
                return true;
            }
            catch (Exception) when (OperatingSystem.IsWindows())
            {
                if (attempt == 2)
                {
                    return false;
                }

                await Task.Delay(25);
            }
        }

        return false;
    }

    private async void OnPasteFileClick(object sender, RoutedEventArgs e)
    {
        await PasteClipboardFilesAsync();
    }

    private async void OnDeleteFileClick(object sender, RoutedEventArgs e)
    {
        if (IconList.SelectedItem is not DrawerItemViewModel item)
        {
            return;
        }

        await ViewModel.DeleteItemCommand.ExecuteAsync(item);
        _keyboardDeleteTarget = null;
        IconList.Focus();
    }

    private async Task PasteClipboardFilesAsync()
    {
        string[] paths;
        try
        {
            if (!Clipboard.ContainsFileDropList())
            {
                return;
            }

            paths = Clipboard.GetFileDropList().Cast<string>().ToArray();
        }
        catch (Exception) when (OperatingSystem.IsWindows())
        {
            return;
        }

        var itemIds = await ViewModel.CopyPathsIntoBoxAsync(paths);
        var lastItemId = itemIds.LastOrDefault();
        if (lastItemId != Guid.Empty)
        {
            SelectItem(lastItemId);
        }
    }

    private static bool ClipboardContainsFileDropList()
    {
        try
        {
            return Clipboard.ContainsFileDropList();
        }
        catch (Exception) when (OperatingSystem.IsWindows())
        {
            return false;
        }
    }

    private void OnSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!CanStartWholeBoxDrag(
                TryGetDrawerItem(e.OriginalSource, out _),
                e.OriginalSource))
        {
            return;
        }

        ClearItemSelection();

        if (_isPositionLocked)
        {
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                var isExplicitProjectUnlinkRequested = IsExplicitProjectUnlinkModifier(
                    Keyboard.Modifiers);
                DragMove();
                var dropPoint = CaptureDragReleasePoint();
                isExplicitProjectUnlinkRequested |= IsExplicitProjectUnlinkModifier(
                    Keyboard.Modifiers);
                QueueSendToBottom();
                if (_positionChangedCallback is not null)
                {
                    _ = _positionChangedCallback(
                        ViewModel.BoxId,
                        dropPoint,
                        isExplicitProjectUnlinkRequested);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    internal static bool CanStartWholeBoxDrag(bool sourceIsDrawerItem, object? source) =>
        !sourceIsDrawerItem && !IsInteractiveSurface(source);

    internal static bool IsExplicitProjectUnlinkModifier(ModifierKeys modifiers) =>
        (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

    private Point? CaptureDragReleasePoint()
    {
        try
        {
            return TryGetCursorScreenPosition(out var point) ? point : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool IsInteractiveSurface(object? source)
    {
        if (source is not DependencyObject dependency)
        {
            return false;
        }

        for (DependencyObject? current = dependency; current is not null; current = GetInteractiveSurfaceParent(current))
        {
            if (current is TextBox
                or Button
                or ComboBox
                or CheckBox
                or ListBoxItem
                or ScrollBar)
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? GetInteractiveSurfaceParent(DependencyObject current) =>
        current switch
        {
            Visual => VisualTreeHelper.GetParent(current),
            FrameworkContentElement contentElement => contentElement.Parent,
            _ => LogicalTreeHelper.GetParent(current)
        };

    private void OnIconPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_itemDragGate.IsEntered)
        {
            e.Handled = true;
            return;
        }

        BeginIconDrag(e, sender as ListBox ?? ActiveItemsList);
    }

    private async void OnIconMouseMove(object sender, MouseEventArgs e)
    {
        var itemList = sender as ListBox ?? ActiveItemsList;
        if (_dragStartPoint is null || _dragStartItem is null)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            ClearPendingIconDrag();
            return;
        }

        var current = e.GetPosition(itemList);
        var distanceX = Math.Abs(current.X - _dragStartPoint.Value.X);
        var distanceY = Math.Abs(current.Y - _dragStartPoint.Value.Y);
        if (distanceX < SystemParameters.MinimumHorizontalDragDistance
            && distanceY < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var drawerItem = _dragStartItem;
        // DoDragDrop runs a nested OLE message loop. Clear the pending gesture and close
        // the gate before entering it so re-entrant MouseMove events cannot start a
        // second nested drag operation.
        ClearPendingIconDrag();
        if (!_itemDragGate.TryEnter())
        {
            return;
        }

        try
        {
            // 拖拽不需要窗口激活：OLE 模态循环自行处理 Esc 取消与光标反馈，
            // 激活只会把盒子抬起来闪一帧。
            await RunItemDragAsync(drawerItem, itemList);
        }
        finally
        {
            _itemDragGate.Exit();
        }
    }

    private (int Column, int Row)? GetDropSlot(DragEventArgs e, DesktopBoxDragPayload? payload = null)
    {
        var movingItemId = payload?.SourceBoxId == ViewModel.BoxId ? payload.ItemId : (Guid?)null;
        if (ViewModel.IsMappingListMode)
        {
            return ViewModel.GetListDropSlot(movingItemId);
        }

        if (ViewModel.IsDrawerCollapsed)
        {
            // The collapsed drawer cover is not the item grid (the IconList is hidden and
            // has zero size), so pointer coordinates cannot select a grid cell. Append
            // after the last item, the same fallback the mapping list view uses.
            return ViewModel.GetListDropSlot(movingItemId);
        }

        var itemList = ActiveItemsList;
        var point = e.GetPosition(itemList);
        var padding = itemList.Padding;
        var rawSlot = ViewModel.GetGridSlot(
            point.X - padding.Left,
            point.Y - padding.Top,
            Math.Max(0, itemList.ActualWidth - padding.Left - padding.Right),
            Math.Max(0, itemList.ActualHeight - padding.Top - padding.Bottom));

        // 固定模式（硬约束）：盒内找不到空位时返回 null，调用方据此拒绝拖放。
        return ViewModel.TryGetAvailableDropSlot(rawSlot.Column, rawSlot.Row, movingItemId, out var slot)
            ? slot
            : null;
    }

    private void ShowDropPreview(DragEventArgs e, DesktopBoxDragPayload? payload)
    {
        if (ViewModel.IsDrawerCollapsed)
        {
            var coverMovingItemId = payload?.SourceBoxId == ViewModel.BoxId ? payload.ItemId : (Guid?)null;
            ShowDrawerCoverDropPreview(coverMovingItemId);
            return;
        }

        var slot = GetDropSlot(e, payload);
        if (slot is null)
        {
            // 固定模式盒已满：不显示落点预览，DragOver 已给出禁止光标。
            ViewModel.HideDragPreview();
            return;
        }

        ViewModel.ShowDragPreview(slot.Value.Column, slot.Value.Row);
    }

    private void ShowDrawerCoverDropPreview(Guid? movingItemId)
    {
        // Dropped items append after the last item (see GetDropSlot), so the preview
        // frame marks the exact cover cell the item will occupy -- the same
        // "frame == landing spot" contract the normal grid boxes have.
        var insertIndex = ViewModel.Items.Count(item => movingItemId is null || item.Id != movingItemId.Value);
        if (insertIndex >= ViewModel.DrawerCoverCapacity
            || DrawerCoverItems.ActualWidth <= 0
            || DrawerCoverItems.ActualHeight <= 0)
        {
            // The item lands in the overflow popup (or the cover is not measured yet):
            // there is no cover cell to point at, keep just the box highlight.
            ViewModel.HideDragPreview();
            return;
        }

        var cellRect = CalculateCoverCellRect(
            insertIndex,
            ViewModel.DrawerCoverColumns,
            ViewModel.DrawerCoverRows,
            DrawerCoverItems.ActualWidth,
            DrawerCoverItems.ActualHeight,
            ViewModel.LayoutSettings.ItemSpacing);
        var origin = DrawerCoverItems.TranslatePoint(
            new Point(cellRect.Left, cellRect.Top),
            DragPreviewCanvas);
        ViewModel.ShowDragPreviewAt(origin.X, origin.Y, cellRect.Width, cellRect.Height);
    }

    internal static Rect CalculateCoverCellRect(
        int cellIndex,
        int columns,
        int rows,
        double surfaceWidth,
        double surfaceHeight,
        double inset)
    {
        var safeColumns = Math.Max(1, columns);
        var safeRows = Math.Max(1, rows);
        var cellWidth = surfaceWidth / safeColumns;
        var cellHeight = surfaceHeight / safeRows;
        var safeIndex = Math.Max(0, cellIndex);
        var cellColumn = safeIndex % safeColumns;
        var cellRow = safeIndex / safeColumns;
        return new Rect(
            (cellColumn * cellWidth) + inset,
            (cellRow * cellHeight) + inset,
            Math.Max(1, cellWidth - (inset * 2)),
            Math.Max(1, cellHeight - (inset * 2)));
    }

    private void SelectItem(Guid itemId)
    {
        var item = ViewModel.Items.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null)
        {
            return;
        }

        ActiveItemsList.SelectedItem = item;
        _keyboardDeleteTarget = item;
        ActiveItemsList.Focus();
    }

    private async Task CompleteInternalDropAsync(DesktopBoxDragPayload payload, (int Column, int Row) slot)
    {
        var moved = false;
        try
        {
            moved = await ViewModel.DropDrawerItemAsync(payload.ItemId, slot.Column, slot.Row);
            if (moved)
            {
                MarkDroppedInsideWitchDrawer(payload);
                SelectItem(payload.ItemId);
            }
        }
        finally
        {
            payload.CompleteDrop(moved);
        }
    }

    private void BeginIconDrag(MouseButtonEventArgs e, ListBox itemList)
    {
        // 不在按下时激活：盒子窗口带 WS_EX_NOACTIVATE，刻意让点选不抬升（防闪帧）。
        // 键盘激活推迟到拖拽真正开始时（OnIconMouseMove 超过阈值后）。
        _dragStartPoint = e.GetPosition(itemList);
        _dragStartItem = null;

        if (TryGetDrawerItem(e.OriginalSource, out var drawerItem))
        {
            itemList.SelectedItem = drawerItem;
            _keyboardDeleteTarget = drawerItem;
            _dragStartItem = drawerItem;
        }
        else
        {
            itemList.SelectedItem = null;
            _keyboardDeleteTarget = null;
        }
    }

    private void ClearPendingIconDrag()
    {
        _dragStartPoint = null;
        _dragStartItem = null;
    }

    // A single left-button drag handles every case based on where it is released:
    //   - dropped on the same box  -> rearrange
    //   - dropped on another box   -> move into that box
    //   - dropped outside the app  -> move out to the desktop
    private async Task RunItemDragAsync(DrawerItemViewModel drawerItem, UIElement dragSource)
    {
        var payload = DesktopBoxDragPayload.Create(drawerItem.Id, ViewModel.BoxId);
        var data = new DataObject();
        data.SetData(InternalDrawerItemDragFormat, payload, autoConvert: false);
        var canExportPath = PathExists(drawerItem.PathLabel);

        var dragWasCanceled = false;
        QueryContinueDragEventHandler queryContinueDrag = (_, args) =>
        {
            if (args.EscapePressed)
            {
                dragWasCanceled = true;
            }
        };

        // The drag carries no OS file data, so the desktop/Explorer reports "no drop" and the
        // shell shows a forbidden (🚫) cursor — misleading, because releasing there still moves
        // the item to the desktop. Override the feedback: keep the normal move cursor over valid
        // in-app targets, and show a neutral hand instead of 🚫 everywhere else.
        GiveFeedbackEventHandler giveFeedback = (_, args) =>
        {
            args.Handled = true;
            if (args.Effects == DragDropEffects.None)
            {
                args.UseDefaultCursors = false;
                Mouse.SetCursor(Cursors.Hand);
            }
            else
            {
                args.UseDefaultCursors = true;
                Mouse.SetCursor(null);
            }
        };

        drawerItem.IsDragSource = true;
        dragSource.QueryContinueDrag += queryContinueDrag;
        dragSource.GiveFeedback += giveFeedback;

        // The secondary drawer popup is StaysOpen="False", so the OLE drag's mouse capture
        // would close it mid-drag and detach the drag source (killing GiveFeedback /
        // QueryContinueDrag and the cursor override). Keep it open for the drag's duration.
        var keepDrawerPopupOpen = DrawerSecondaryPopup.IsOpen
            && dragSource is Visual dragVisual
            && IsSameOrVisualDescendant(DrawerSecondaryPopupRoot, dragVisual);
        if (keepDrawerPopupOpen)
        {
            DrawerSecondaryPopup.StaysOpen = true;
        }

        try
        {
            DragDrop.DoDragDrop(dragSource, data, DragDropEffects.Move);
            var internalDropSucceeded = payload.WasDroppedInsideWitchDrawer
                || ConsumeDroppedInsideWitchDrawer(payload);
            var cursorOverWindow = IsCursorOverWitchDrawerWindow();
            var cursorOverPopup = IsCursorOverOpenDrawerPopup();
            var cursorOverApp = cursorOverWindow || cursorOverPopup;

            if (internalDropSucceeded)
            {
                // Dropped onto a WitchDrawer box (same box = rearrange, other box = move).
                // The destination performs the move asynchronously; wait for it to commit
                // before refreshing the source box.
                await WaitForInternalDropAsync(payload);
                await ViewModel.LoadAsync();
                if (!ViewModel.Items.Any(item => item.Id == drawerItem.Id))
                {
                    _keyboardDeleteTarget = null;
                }
            }
            else if (ShouldExportItemAfterDrag(
                         dragWasCanceled,
                         canExportPath,
                         cursorOverApp,
                         internalDropSucceeded))
            {
                // Released outside every WitchDrawer window → move the file to the desktop.
                var exported = await ViewModel.ExportItemToDesktopAsync(drawerItem);
                if (exported)
                {
                    _keyboardDeleteTarget = null;
                }
            }
            // else: released over the same box without moving, or cancelled with Esc → no action.
        }
        finally
        {
            if (keepDrawerPopupOpen)
            {
                DrawerSecondaryPopup.StaysOpen = false;
            }
            dragSource.QueryContinueDrag -= queryContinueDrag;
            dragSource.GiveFeedback -= giveFeedback;
            drawerItem.IsDragSource = false;
            ResetAllDragVisualStates();
            ResetDragCursor();
            if (Mouse.Captured is not null)
            {
                Mouse.Capture(null);
            }
            dragSource.Focus();
            QueueSendToBottom();
        }
    }

    internal static bool ShouldExportItemAfterDrag(
        bool dragWasCanceled,
        bool canExportPath,
        bool cursorOverApp,
        bool internalDropSucceeded)
    {
        return !dragWasCanceled
            && canExportPath
            && !cursorOverApp
            && !internalDropSucceeded;
    }

    private void ResetDragVisualState()
    {
        // 立即复位（落放/拖拽结束/全局清理）：任何延迟复位都取消。
        CancelPendingDragLeaveReset();
        ViewModel.HideDragPreview();
        ViewModel.IsDragOver = false;
    }

    private static void ResetAllDragVisualStates()
    {
        if (Application.Current is null)
        {
            return;
        }

        foreach (var window in Application.Current.Windows.OfType<DesktopBoxWindow>())
        {
            window.ResetDragVisualState();
        }
    }

    private static void ResetDragCursor()
    {
        // GiveFeedback may leave a custom Hand cursor after DoDragDrop returns.
        Mouse.OverrideCursor = null;
        Mouse.SetCursor(null);
    }

    private static async Task<bool> WaitForInternalDropAsync(DesktopBoxDragPayload payload)
    {
        var completedTask = await Task.WhenAny(payload.DropCompletion, Task.Delay(750));
        return completedTask == payload.DropCompletion && await payload.DropCompletion;
    }

    private static bool TryGetInternalDragPayload(IDataObject data, out DesktopBoxDragPayload payload)
    {
        payload = null!;
        var rawPayload = data.GetData(InternalDrawerItemDragFormat);
        if (rawPayload is DesktopBoxDragPayload typedPayload)
        {
            payload = typedPayload;
            return true;
        }

        if (rawPayload is Guid itemId)
        {
            payload = DesktopBoxDragPayload.Create(itemId, Guid.Empty);
            return true;
        }

        return false;
    }

    private static DragDropEffects ChooseFileDropEffect(DragDropEffects allowedEffects)
    {
        if ((allowedEffects & DragDropEffects.Move) == DragDropEffects.Move)
        {
            return DragDropEffects.Move;
        }

        return (allowedEffects & DragDropEffects.Copy) == DragDropEffects.Copy
            ? DragDropEffects.Copy
            : (allowedEffects & DragDropEffects.Link) == DragDropEffects.Link
                ? DragDropEffects.Link
                : DragDropEffects.None;
    }

    internal static void MarkDroppedInsideWitchDrawer(DesktopBoxDragPayload payload)
    {
        // 目标盒的 Drop 处理器在 DoDragDrop 返回前就会同步置位 WasDroppedInsideWitchDrawer，
        // 源端靠该标志位即可识别内部落放；静态集合只是"同步标记缺失"时的兜底通道。
        // 已有同步标记时再写入集合，条目永远不会被消费（源端 || 短路），残留 ItemId 会把
        // 该项目之后的"拖出到桌面"误判成内部落放，导致首次拖出静默失效。
        if (!payload.WasDroppedInsideWitchDrawer)
        {
            CompletedInternalDragIds.Add(payload.DragId);
            CompletedInternalItemIds.Add(payload.ItemId);
        }

        payload.WasDroppedInsideWitchDrawer = true;
    }

    internal static bool ConsumeDroppedInsideWitchDrawer(DesktopBoxDragPayload payload)
    {
        var matchedByDrag = CompletedInternalDragIds.Remove(payload.DragId);
        var matchedByItem = CompletedInternalItemIds.Remove(payload.ItemId);
        var matched = matchedByDrag || matchedByItem;
        if (!matched)
        {
            return false;
        }

        payload.WasDroppedInsideWitchDrawer = true;
        return true;
    }

    private static bool PathExists(string? path)
    {
        return !string.IsNullOrWhiteSpace(path)
            && (File.Exists(path) || Directory.Exists(path));
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    private static readonly nint WindowPositionTopmost = -1;
    private const int WindowOwnerIndex = -8;
    private const uint SetWindowPosNoSize = 0x0001;
    private const uint SetWindowPosNoMove = 0x0002;
    private const uint SetWindowPosNoActivate = 0x0010;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint hWnd,
        nint hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint SetWindowLongPtr(nint hWnd, int index, nint newValue);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);

    private const uint MonitorDefaultToNearest = 2;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.StructLayout(
        System.Runtime.InteropServices.LayoutKind.Sequential,
        CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct NativeMonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
        [System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref NativeMonitorInfo lpmi);

    /// <summary>
    /// 窗口当前所在显示器的工作区（DIP）。<see cref="SystemParameters.WorkArea"/> 只覆盖主屏，
    /// 多显示器下必须按窗口所在屏取工作区，否则副屏上的盒子会被钳制逻辑误判越界搬回主屏。
    /// 句柄尚未创建或查询失败时回退到主屏工作区。
    /// </summary>
    internal Rect GetWorkAreaDip()
    {
        var fallback = SystemParameters.WorkArea;
        var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (handle == nint.Zero)
        {
            return fallback;
        }

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            return fallback;
        }

        var info = new NativeMonitorInfo
        {
            Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMonitorInfo>()
        };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return fallback;
        }

        // GetMonitorInfo 返回物理像素，按窗口当前 DPI 换算成 DIP。
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Rect(
            info.WorkArea.Left / dpi.DpiScaleX,
            info.WorkArea.Top / dpi.DpiScaleY,
            (info.WorkArea.Right - info.WorkArea.Left) / dpi.DpiScaleX,
            (info.WorkArea.Bottom - info.WorkArea.Top) / dpi.DpiScaleY);
    }

    private bool IsCursorOverOpenDrawerPopup()
    {
        // Popups are not part of Application.Current.Windows, so the window hit-test above
        // misses releases over the secondary drawer popup. Treat those as inside the app;
        // otherwise a short drag ending on the popup would wrongly move the item to the desktop.
        if (!DrawerSecondaryPopup.IsOpen
            || !DrawerSecondaryPopupRoot.IsVisible
            || !GetCursorPos(out var cursor))
        {
            return false;
        }

        try
        {
            var topLeft = DrawerSecondaryPopupRoot.PointToScreen(new Point(0, 0));
            var bottomRight = DrawerSecondaryPopupRoot.PointToScreen(
                new Point(DrawerSecondaryPopupRoot.ActualWidth, DrawerSecondaryPopupRoot.ActualHeight));
            return IsScreenPointInside(cursor.X, cursor.Y, topLeft, bottomRight);
        }
        catch (InvalidOperationException)
        {
            // Popup content has no presentation source yet; skip it.
            return false;
        }
    }

    internal static bool IsScreenPointInside(int x, int y, Point topLeft, Point bottomRight)
    {
        return x >= topLeft.X
            && x <= bottomRight.X
            && y >= topLeft.Y
            && y <= bottomRight.Y;
    }

    internal static bool IsSameOrVisualDescendant(Visual root, Visual candidate)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(candidate);
        return ReferenceEquals(root, candidate) || root.IsAncestorOf(candidate);
    }

    private static bool IsCursorOverWitchDrawerWindow()
    {
        // Mouse.GetPosition is stale right after DoDragDrop; use the real cursor screen
        // position and compare against each window's on-screen rectangle.
        if (!GetCursorPos(out var cursor))
        {
            return false;
        }

        foreach (Window window in Application.Current.Windows)
        {
            if (!window.IsVisible || window.ActualWidth <= 0 || window.ActualHeight <= 0)
            {
                continue;
            }

            try
            {
                var topLeft = window.PointToScreen(new Point(0, 0));
                var bottomRight = window.PointToScreen(new Point(window.ActualWidth, window.ActualHeight));
                if (IsScreenPointInside(cursor.X, cursor.Y, topLeft, bottomRight))
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                // Window has no presentation source yet; skip it.
            }
        }

        return false;
    }

    private async void OnItemsMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TryGetDrawerItem(e.OriginalSource, out var drawerItem))
        {
            await ViewModel.OpenItemCommand.ExecuteAsync(drawerItem);
        }
    }

    private bool TryGetDrawerItem(object? source, out DrawerItemViewModel drawerItem)
    {
        drawerItem = null!;
        if (source is not DependencyObject dependencyObject)
        {
            return false;
        }

        var container = ItemsControl.ContainerFromElement(IconList, dependencyObject) as FrameworkElement
            ?? ItemsControl.ContainerFromElement(FileList, dependencyObject) as FrameworkElement;
        if (container?.DataContext is not DrawerItemViewModel item)
        {
            return false;
        }

        drawerItem = item;
        return true;
    }
}
