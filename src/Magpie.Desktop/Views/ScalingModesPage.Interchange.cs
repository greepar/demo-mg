using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;

public partial class ScalingModesPage
{
    private bool _interchangeBusy;
    internal ModeImportResult ImportFromJson(string text)
    {
        var result=ScalingModeInterchange.Import(RuntimeSettings.Current,text);
        RefreshCustomModes();NewModeForm.RefreshChoices();
        ShowInterchangeStatus($"Imported {result.Added} modes; renamed {result.Renamed} name conflicts. Preserved unavailable effects: {result.DeferredEffects}.");
        return result;
    }
    internal ModeImportResult? TryImportFromJson(string text)
    {
        try { return ImportFromJson(text); }
        catch(Exception failure) { ShowInterchangeStatus(Localization.Get("Message_ImportScalingModesFailed")+" "+failure.Message);return null; }
    }
    internal string ExportToJson()=>ScalingModeInterchange.Export(ScalingModeInterchange.Builtins(RuntimeSettings.Current).Concat(RuntimeSettings.Current.Modes));
    private void ShowInterchangeStatus(string text) { InterchangeStatus.Text=text;InterchangeStatus.IsVisible=true;InterchangeStatus.BringIntoView(); }
    private void SetInterchangeBusy(bool busy) { _interchangeBusy=busy;ImportModesAction.IsEnabled=ExportModesAction.IsEnabled=!busy; }
    private async void ImportModes_Click(object? sender,RoutedEventArgs args)
    {
        if(_interchangeBusy || TopLevel.GetTopLevel(this) is not {} owner)return;
        if(!RuntimeSettings.PersistenceEnabled) { ShowInterchangeStatus("File selection is disabled in preview mode.");return; }
        SetInterchangeBusy(true);
        try
        {
            if(!owner.StorageProvider.CanOpen)throw new InvalidOperationException("File selection is unavailable.");
            var files=await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title=Localization.Get("Dialog_Import_Title"),AllowMultiple=false,FileTypeFilter=[new FilePickerFileType("JSON") { Patterns=["*.json"],MimeTypes=["application/json"] }] });
            if(files.Count==0)return;
            using var file=files[0];await using var input=await file.OpenReadAsync();
            TryImportFromJson(await ScalingModeInterchange.ReadAsync(input));
        }
        catch(Exception failure) { ShowInterchangeStatus(Localization.Get("Message_ImportScalingModesFailed")+" "+failure.Message); }
        finally { SetInterchangeBusy(false); }
    }
    private async void ExportModes_Click(object? sender,RoutedEventArgs args)
    {
        if(_interchangeBusy || TopLevel.GetTopLevel(this) is not {} owner)return;
        if(!RuntimeSettings.PersistenceEnabled) { ShowInterchangeStatus("File selection is disabled in preview mode.");return; }
        SetInterchangeBusy(true);
        try
        {
            string text=ExportToJson();
            if(!owner.StorageProvider.CanSave)throw new InvalidOperationException("File saving is unavailable.");
            using var file=await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title=Localization.Get("Dialog_Export_Title"),SuggestedFileName="ScalingModes.json",DefaultExtension="json",ShowOverwritePrompt=true,FileTypeChoices=[new FilePickerFileType("JSON") { Patterns=["*.json"],MimeTypes=["application/json"] }] });
            if(file is null)return;
            if(file.TryGetLocalPath() is {} localPath)await ScalingModeInterchange.WriteLocalAsync(localPath,text);
            else { await using var output=await file.OpenWriteAsync();await ScalingModeInterchange.WriteAsync(output,text); }
            ShowInterchangeStatus("Scaling modes exported.");
        }
        catch(Exception failure) { ShowInterchangeStatus(Localization.Get("Message_ExportScalingModesFailed")+" "+failure.Message); }
        finally { SetInterchangeBusy(false); }
    }
}
