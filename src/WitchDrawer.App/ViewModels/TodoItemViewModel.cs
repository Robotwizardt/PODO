using WitchDrawer.Core.Models;

namespace WitchDrawer.App.ViewModels;

public sealed class TodoItemViewModel
{
    public TodoItemViewModel(TodoItem model)
    {
        Model = model;
    }

    public TodoItem Model { get; }

    public Guid Id => Model.Id;

    public string Title => Model.Title;

    public bool IsCompleted => Model.IsCompleted;

    public string TimeLabel
    {
        get
        {
            var time = (Model.CompletedAt ?? Model.CreatedAt).ToLocalTime();
            var prefix = Model.IsCompleted ? "完成于" : "创建于";
            return $"{prefix} {time:MM-dd HH:mm}";
        }
    }
}
