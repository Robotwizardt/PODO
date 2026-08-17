using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public sealed class ArchivedTodoItemViewModel
{
    public ArchivedTodoItemViewModel(TodoItem model, string boxName)
    {
        Model = model;
        BoxName = boxName;
    }

    public TodoItem Model { get; }

    public Guid Id => Model.Id;

    public string Title => Model.Title;

    public string BoxName { get; }

    public string ArchivedTimeLabel
    {
        get
        {
            var time = (Model.ArchivedAt ?? Model.UpdatedAt).ToLocalTime();
            return $"归档于 {time:yyyy-MM-dd HH:mm}";
        }
    }
}
