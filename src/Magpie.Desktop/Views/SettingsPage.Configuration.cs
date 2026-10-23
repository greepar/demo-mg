using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;

public partial class SettingsPage
{
    private ConfigurationLocation? _configuration;
    private bool _refreshingConfiguration;
    private Func<string,Task<bool>>? _previewRevealFile;
    private void RefreshConfiguration()
    {
        _refreshingConfiguration=true;
        try
        {
            if(_configuration is null && RuntimeSettings.PersistenceEnabled)_configuration=RuntimeSettings.Location;
            PortableModeToggle.IsEnabled=_configuration is not null;LocateConfigurationButton.IsEnabled=_configuration is not null;
            PortableModeToggle.IsChecked=_configuration?.Portable==true;
            ToolTip.SetTip(LocateConfigurationButton,_configuration?.Path);
        }
        finally { _refreshingConfiguration=false; }
    }
    private void ConfigurationError(Exception error)
    {
        ConfigurationStatus.Text=(Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"无法更新或打开配置位置：":"Could not update or open the configuration location: ")+error.Message;
        ConfigurationStatus.IsVisible=true;
        Dispatcher.UIThread.Post(()=>ConfigurationStatus.BringIntoView());
    }
    private void PortableMode_Changed(object? sender,RoutedEventArgs args)
    {
        if(!_initialized||_refreshingConfiguration||_configuration is null)return;
        try
        {
            _configuration.SetPortable(RuntimeSettings.Current,PortableModeToggle.IsChecked==true);
            ConfigurationStatus.IsVisible=false;RefreshConfiguration();
        }
        catch(Exception failure)
        {
            PortableModeToggle.IsEnabled=false;
            Dispatcher.UIThread.Post(()=> { RefreshConfiguration();ConfigurationError(failure); });
        }
    }
    private async void LocateConfiguration_Click(object? sender,RoutedEventArgs args)
    {
        if(_configuration is null)return;
        LocateConfigurationButton.IsEnabled=false;
        try
        {
            // A fresh installation may not have saved any preferences yet.
            if(!File.Exists(_configuration.Path))RuntimeSettings.Current.WriteFile(_configuration.Path);
            bool opened;
            if(_previewRevealFile is {} preview)
            {
                if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Configuration fixtures require preview mode.");
                opened=await preview(_configuration.Path);
            }
            else
            {
                if(!RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Opening the file manager is disabled in preview mode.");
                var owner=TopLevel.GetTopLevel(this) ?? throw new InvalidOperationException("The application window is unavailable.");
                opened=await ConfigurationReveal.ShowAsync(owner,_configuration.Path);
            }
            if(!opened)throw new IOException(Localization.CurrentLanguage.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"系统无法打开配置目录。":"The system could not open the configuration directory.");
            ConfigurationStatus.IsVisible=false;
        }
        catch(Exception error) { ConfigurationError(error); }
        finally { LocateConfigurationButton.IsEnabled=true; }
    }
}
