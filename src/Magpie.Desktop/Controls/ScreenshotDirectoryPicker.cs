using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Magpie.Desktop.Core;

namespace Magpie.Desktop.Controls;

internal static class ScreenshotDirectoryPicker
{
    internal static void ApplyDirectory(string path,RuntimeSettings? target=null)
    {
        var settings=target ?? RuntimeSettings.Current;
        string previous=settings.ScreenshotDirectory;
        try { settings.ScreenshotDirectory=Path.GetFullPath(path);RuntimeSettings.Current.Save(); }
        catch { settings.ScreenshotDirectory=previous;throw; }
    }
    internal static async Task<bool> ChooseAsync(TopLevel owner,RuntimeSettings? target=null)
    {
        if(!owner.StorageProvider.CanPickFolder)throw new InvalidOperationException("Folder selection is unavailable.");
        var folders=await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title="Screenshot directory",AllowMultiple=false });
        if(folders.Count==0)return false;
        using var folder=folders[0];
        ApplyDirectory(folder.TryGetLocalPath() ?? throw new InvalidOperationException("Choose a local folder."),target);
        return true;
    }
}
