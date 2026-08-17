using CommunityToolkit.Mvvm.ComponentModel;
using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public sealed class ProjectLinkableBoxViewModel(Box box)
{
    public Guid Id => box.Id;

    public string Name => box.Name;

    public string TypeLabel => GetTypeLabel(box.Type);

    public string Badge => GetBadge(box.Type);

    internal static string GetTypeLabel(BoxType type) => type switch
    {
        BoxType.Mapping => "映射",
        BoxType.Pixel => "像素",
        BoxType.Drawer => "抽屉",
        BoxType.Bound => "目标",
        _ => "文件"
    };

    internal static string GetBadge(BoxType type) => type switch
    {
        BoxType.Mapping => "M",
        BoxType.Pixel => "P",
        BoxType.Drawer => "D",
        BoxType.Bound => "B",
        _ => "F"
    };
}

public sealed class ProjectLinkedBoxViewModel : ObservableObject
{
    private bool _isVisible;
    private ProjectAttachmentSide _attachmentSide;

    public ProjectLinkedBoxViewModel(ProjectBoxLink link)
    {
        Id = link.LinkedBoxId;
        Name = link.LinkedBoxName;
        TypeLabel = ProjectLinkableBoxViewModel.GetTypeLabel(link.LinkedBoxType);
        Badge = ProjectLinkableBoxViewModel.GetBadge(link.LinkedBoxType);
        _isVisible = link.IsVisible;
        _attachmentSide = link.AttachmentSide;
    }

    public Guid Id { get; }

    public string Name { get; }

    public string TypeLabel { get; }

    public string Badge { get; }

    public bool IsVisible
    {
        get => _isVisible;
        private set
        {
            if (!SetProperty(ref _isVisible, value))
            {
                return;
            }

            OnPropertyChanged(nameof(VisibilityActionLabel));
        }
    }

    public string VisibilityActionLabel => IsVisible ? "隐藏关联文件盒" : "显示关联文件盒";

    public ProjectAttachmentSide AttachmentSide => _attachmentSide;

    public string AttachmentSideLabel => ProjectAttachmentSideCatalog.GetLabel(AttachmentSide);

    internal void Apply(ProjectBoxLink link)
    {
        IsVisible = link.IsVisible;
        if (SetProperty(ref _attachmentSide, link.AttachmentSide))
        {
            OnPropertyChanged(nameof(AttachmentSideLabel));
        }
    }
}
