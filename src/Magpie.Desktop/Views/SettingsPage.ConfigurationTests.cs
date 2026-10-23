using Avalonia.Interactivity;
using Avalonia.Controls;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;

public partial class SettingsPage
{
    internal async Task VerifyConfigurationUiAsync(bool failure)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Configuration fixtures require isolated preview mode.");
        string root=Path.Combine(Path.GetTempPath(),"magpie-config-ui-"+Guid.NewGuid().ToString("N"));
        try
        {
            string user=Path.Combine(root,"user",ConfigurationLocation.FileName),portable=Path.Combine(root,"application","config",ConfigurationLocation.FileName);
            _configuration=new ConfigurationLocation(user,portable);RefreshConfiguration();
            if(failure)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetDirectoryName(portable)!)!);
                File.WriteAllText(Path.GetDirectoryName(portable)!,"blocked directory");
                PortableModeToggle.IsChecked=true;await Task.Delay(100);
                if(PortableModeToggle.IsChecked==true||!PortableModeToggle.IsEnabled||!ConfigurationStatus.IsVisible||_configuration.Portable)throw new InvalidOperationException("Portable enable failure did not roll back and display an error.");
            }
            else
            {
                PortableModeToggle.IsChecked=true;await Task.Delay(30);
                if(!_configuration.Portable||!File.Exists(portable))throw new InvalidOperationException("Portable toggle did not migrate settings.");
                string? opened=null;
                _previewRevealFile=file=> { opened=file;return Task.FromResult(true); };
                LocateConfigurationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(50);
                if(opened!=portable)throw new InvalidOperationException("Locate configuration did not use the active portable file.");
                PortableModeToggle.IsChecked=false;await Task.Delay(30);
                if(_configuration.Portable||File.Exists(portable)||!File.Exists(user))throw new InvalidOperationException("Portable toggle did not return to user preferences.");
                LocateConfigurationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(50);
                if(opened!=user)throw new InvalidOperationException("Locate configuration did not follow the new file.");
                _previewRevealFile=_=>Task.FromResult(false);
                LocateConfigurationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(60);
                if(!ConfigurationStatus.IsVisible||!LocateConfigurationButton.IsEnabled)throw new InvalidOperationException("Rejected directory launch did not recover.");
            }
            PortableModeCard.BringIntoView();await Task.Delay(60);
            Console.WriteLine("CONFIGURATION UI PASS: "+(failure?"write failure rollback and visible error":"real toggle migration, current-directory launcher calls and launch rejection recovery")+"; all files temporary and no file manager opened.");
        }
        finally
        {
            _configuration=null;_previewRevealFile=null;
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
