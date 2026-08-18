using System.IO;
using System.Text;

namespace PaperTodo;

public static class NoteFileExporter
{
    public static string CreateSafeFileName(string? title, string fallback)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var characters = (title ?? string.Empty)
            .Trim()
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .ToArray();
        var fileName = new string(characters).TrimEnd(' ', '.');
        return string.IsNullOrWhiteSpace(fileName) ? fallback : fileName;
    }

    public static void Save(string path, string? content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(
            path,
            content ?? string.Empty,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
