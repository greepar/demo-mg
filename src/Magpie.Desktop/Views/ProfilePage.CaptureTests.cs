using Avalonia.Controls;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;

public partial class ProfilePage
{
    internal async Task VerifyTitleBarInteractionAsync()
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Title bar fixtures require isolated preview mode.");
        Settings.CaptureTitleBar=true;RefreshCaptureMethod();
        bool supported=CaptureBackendPolicy.CanCaptureTitleBar(Settings.CaptureBackend);
        if(CaptureTitleBarCard.IsEnabled!=supported||CaptureTitleBarToggle.IsChecked!=true)throw new InvalidOperationException("Saved title bar preference/capability not reflected.");
        CaptureTitleBarToggle.IsChecked=false;
        if(Settings.CaptureTitleBar==supported)throw new InvalidOperationException("Title bar handler ignored availability or failed to save.");
        if(supported) {CaptureTitleBarToggle.IsChecked=true;if(!Settings.CaptureTitleBar)throw new InvalidOperationException("Enabled title bar handler failed to restore preference.");}
        RefreshCaptureMethod();
        var reopened=new ProfilePage();if(_profile is not null)reopened.ConfigureProfile(_profile);
        if(!reopened.Settings.CaptureTitleBar||reopened.CaptureTitleBarToggle.IsChecked!=true||reopened.CaptureTitleBarCard.IsEnabled!=supported)
            throw new InvalidOperationException("Reopening profile lost the title bar preference or changed capability.");
        CaptureTitleBarCard.BringIntoView();await Task.Delay(80);
        Console.WriteLine("TITLE BAR UI PASS: actual toggle handlers respect backend capability, unsupported backends preserve saved preferences, reopened profile restores value; no capture or system picker.");
    }
    internal async Task VerifyCaptureInteractionAsync(Window owner,bool leaveWarning)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Capture fixtures require isolated preview mode.");
        var previous=Settings.CaptureBackend;
        bool previousTitle=Settings.CaptureTitleBar;
        try
        {
            Settings.CaptureTitleBar=true;RefreshCaptureMethod();
            if(CaptureTitleBarToggle.IsChecked!=true||CaptureTitleBarCard.IsEnabled!=CaptureBackendPolicy.CanCaptureTitleBar(Settings.CaptureBackend))throw new InvalidOperationException("Saved title bar preference or native capability was not reflected.");
            var available=CaptureBackendPolicy.Available;
            // Saved preferences from a different platform remain intact until
            // the user explicitly chooses an available backend.
            foreach(var foreign in Enum.GetValues<CaptureBackend>().Where(b=>!CaptureBackendPolicy.Supported(b)))
            {
                Settings.CaptureBackend=foreign;RefreshCaptureMethod();
                if(CaptureMethod.SelectedItem is not ComboBoxItem { Tag:CaptureBackend selected,IsEnabled:false }||selected!=foreign||!CaptureWarning.IsOpen||CaptureWarning.Severity!="Warning")throw new InvalidOperationException("Unavailable saved capture backend was hidden or overwritten.");
                var reopened=new ProfilePage();if(_profile is not null)reopened.ConfigureProfile(_profile);
                if(reopened.Settings.CaptureBackend!=foreign||Settings.CaptureBackend!=foreign)throw new InvalidOperationException("Opening profile replaced unavailable backend.");
                int supported=Enum.GetValues<CaptureBackend>().Count(b=>b!=CaptureBackend.Native&&CaptureBackendPolicy.Supported(b));
                if(CaptureMethod.Items.Cast<ComboBoxItem>().Count(i=>i.IsEnabled)!=supported||CaptureTitleBarCard.IsEnabled)throw new InvalidOperationException("Capture controls advertise an unimplemented capability.");
                await ScalingSession.VerifyUnsupportedCaptureAsync(owner,Settings);
                CaptureMethod.SelectedIndex=0;
                if(Settings.CaptureBackend!=CaptureBackend.Native||CaptureWarning.Severity!="Informational")throw new InvalidOperationException($"Selecting native capture did not recover: previous={foreign}, actual={Settings.CaptureBackend}, severity={CaptureWarning.Severity}, index={CaptureMethod.SelectedIndex}, initialized={_initialized}.");
            }
            if(leaveWarning) { Settings.CaptureBackend=CaptureBackend.DesktopDuplication;RefreshCaptureMethod(); }
            CaptureMethod.BringIntoView();await Task.Delay(80);
            Console.WriteLine("CAPTURE BACKEND UI PASS: actual profile selection handlers, unavailable foreign preferences preserved, truthful capabilities, explicit recovery and session rejection before capture/engine/picker.");
        }
        catch { Settings.CaptureBackend=previous;RefreshCaptureMethod();throw; }
        finally {Settings.CaptureTitleBar=previousTitle;CaptureTitleBarToggle.IsChecked=previousTitle;}
    }
}
