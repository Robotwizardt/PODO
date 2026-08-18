using System.Windows;
using Microsoft.Win32;

namespace PaperTodo;

public interface INoteFileSaveDialog
{
    string? Show(string suggestedFileName);
}

internal sealed class NoteFileSaveDialog(Window owner) : INoteFileSaveDialog
{
    public string? Show(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = Strings.Get("NoteFileSaveDialogTitle"),
            Filter = Strings.Get("NoteFileSaveDialogFilter"),
            FilterIndex = 1,
            DefaultExt = ".md",
            AddExtension = true,
            CheckPathExists = true,
            OverwritePrompt = true,
            FileName = suggestedFileName
        };

        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }
}
