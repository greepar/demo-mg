using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;

public partial class SettingsPage
{
    private OriginalConfigurationResult? _importDraft;
    private Func<Task<string?>>? _previewImportSource;
    private Action? _previewImportRestart;
    private string? _previewImportDestination;
    private bool _importBusy;
    private static bool ImportChinese=>Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase);
    public string ImportConfigurationTitle=>ImportChinese?"导入原版配置":"Import original configuration";
    public string ImportConfigurationDescription=>ImportChinese
        ?"选择原版 config.json 并查看迁移报告。导入将替换当前配置并重启，旧配置会自动备份。尚未实现的设置会保留，但不会生效。"
        :"Choose the original config.json and review migration. Import replaces current preferences and restarts, keeping a backup. Unimplemented settings are preserved without taking effect.";
    public string ChooseConfigurationLabel=>ImportChinese?"选择文件":"Choose file";
    public string ApplyConfigurationLabel=>ImportChinese?"备份、导入并重启":"Back up, import and restart";
    public string CancelConfigurationLabel=>ImportChinese?"取消":"Cancel";
    private void ImportError(Exception failure)
    {
        ImportConfigurationError.Text=(ImportChinese?"无法导入配置：":"Could not import configuration: ")+failure.Message;
        ImportConfigurationError.IsVisible=true;ImportConfigurationError.BringIntoView();
    }
    private void ClearImport()
    { _importDraft=null;ImportConfigurationReview.IsVisible=false;ImportConfigurationError.IsVisible=false; }
    private async void ChooseConfiguration_Click(object? sender,RoutedEventArgs args)
    {
        if(_importBusy)return;
        ClearImport();_importBusy=true;ChooseConfigurationButton.IsEnabled=false;
        try
        {
            string? text;
            if(_previewImportSource is {} fixture)
            {
                if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Import fixtures require isolated preview mode.");
                text=await fixture();
            }
            else
            {
                if(!RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("File picking is disabled in preview mode.");
                var owner=TopLevel.GetTopLevel(this)??throw new InvalidOperationException("The application window is unavailable.");
                var files=await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title=ImportConfigurationTitle,AllowMultiple=false,
                    FileTypeFilter=[new FilePickerFileType("Magpie config.json") { Patterns=["*.json"],MimeTypes=["application/json"] }]
                });
                if(files.Count==0)return;
                using var file=files[0];await using var stream=await file.OpenReadAsync();
                text=await ScalingModeInterchange.ReadAsync(stream);
            }
            if(text is null)return;
            _importDraft=OriginalConfiguration.Parse(text);
            ImportConfigurationSummary.Text=(ImportChinese
                ?$"将替换为 {_importDraft.Settings.Profiles.Count} 个应用配置、{_importDraft.Settings.Modes.Count} 个模式。请核对以下 {_importDraft.Warnings.Count} 条迁移说明。"
                :$"Replace with {_importDraft.Settings.Profiles.Count} application profiles and {_importDraft.Settings.Modes.Count} modes. Review {_importDraft.Warnings.Count} migration notes below.");
            ImportConfigurationWarnings.Text=_importDraft.Warnings.Count==0
                ?(ImportChinese?"没有额外迁移说明。全局快捷键需要重新向系统申请绑定。":"No additional migration notes. Global shortcuts require a new system binding request.")
                :string.Join("\n\n",_importDraft.Warnings.Prepend(ImportChinese?"全局快捷键需要重新向系统申请绑定。":"Global shortcuts require a new system binding request."));
            ImportConfigurationReview.IsVisible=true;ImportConfigurationSummary.BringIntoView();
        }
        catch(Exception failure) { _importDraft=null;ImportError(failure); }
        finally { _importBusy=false;ChooseConfigurationButton.IsEnabled=true; }
    }
    private void CancelConfiguration_Click(object? sender,RoutedEventArgs args)
    { if(!_importBusy)ClearImport(); }
    private void ApplyConfiguration_Click(object? sender,RoutedEventArgs args)
    {
        if(_importBusy||_importDraft is not {} draft)return;
        _importBusy=true;ApplyConfigurationButton.IsEnabled=false;
        try
        {
            if(TopLevel.GetTopLevel(this) is not MainWindow main)throw new InvalidOperationException("The application window is unavailable.");
            if(!main.CanImportConfiguration)throw new InvalidOperationException(ImportChinese?"请先停止缩放并等待快捷键设置结束，再导入。":"Stop scaling and finish shortcut setup before importing.");
            if(_previewImportRestart is {} restart)
            {
                if(RuntimeSettings.PersistenceEnabled||_previewImportDestination is null)throw new InvalidOperationException("Import fixtures require an isolated configuration destination.");
                ConfigurationImport.Install(draft.Settings,_previewImportDestination,restart);
                ClearImport();return;
            }
            if(!RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Configuration installation is disabled in preview mode.");
            ConfigurationImport.Install(draft.Settings,RuntimeSettings.FilePath,ApplicationRestart.Restart);
            // No event-loop yield between replacement startup and shutdown: old pages cannot save over the import.
            _importDraft=null;if(main.DesktopIntegration is {} integration)integration.Exit();else main.Close();
        }
        catch(Exception failure) { ImportError(failure); }
        finally { _importBusy=false;ApplyConfigurationButton.IsEnabled=true; }
    }
}
