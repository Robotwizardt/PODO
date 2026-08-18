using System.Text.Json;
using WitchDrawer.Core.Services;

namespace WitchDrawer.App.FileDialogAccess;

public sealed class FileDialogAccessSettingsStore(DrawerService drawerService)
{
    internal const string SettingKey = "FileDialogAccessWindow";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public event EventHandler<FileDialogAccessSettings>? SettingsChanged;

    public async Task<FileDialogAccessSettings> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var saved = await drawerService.GetSettingAsync(SettingKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(saved))
        {
            return FileDialogAccessSettings.Default;
        }

        try
        {
            return Normalize(JsonSerializer.Deserialize<FileDialogAccessSettings>(saved, JsonOptions));
        }
        catch (JsonException)
        {
            return FileDialogAccessSettings.Default;
        }
    }

    public async Task SaveAsync(
        FileDialogAccessSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = Normalize(settings);
        await drawerService.SetSettingAsync(
            SettingKey,
            JsonSerializer.Serialize(normalized, JsonOptions),
            cancellationToken);
        SettingsChanged?.Invoke(this, normalized);
    }

    private static FileDialogAccessSettings Normalize(FileDialogAccessSettings? settings)
    {
        if (settings is null)
        {
            return FileDialogAccessSettings.Default;
        }

        var width = double.IsFinite(settings.Width)
            ? Math.Clamp(settings.Width, 240, 520)
            : FileDialogAccessSettings.Default.Width;
        return settings with
        {
            Width = width,
            RecentBoxIds = (settings.RecentBoxIds ?? [])
                .Where(id => id != Guid.Empty)
                .Distinct()
                .Take(3)
                .ToArray(),
            BlacklistedApplications = (settings.BlacklistedApplications ?? [])
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }
}
