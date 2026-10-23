using Avalonia.Controls;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;

public partial class ProfilePage
{
    internal async Task VerifyCaptureInteractionAsync(Window owner,bool leaveWarning)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Capture fixtures require isolated preview mode.");
        var previous=Settings.CaptureBackend;
        try
        {
            var available=CaptureBackendPolicy.Available;
            // Saved preferences from a different platform remain intact until
            // the user explicitly chooses an available backend.
            foreach(var foreign in Enum.GetValues<CaptureBackend>().Where(b=>b!=CaptureBackend.Native&&b!=available))
            {
                Settings.CaptureBackend=foreign;RefreshCaptureMethod();
                if(CaptureMethod.SelectedItem is not ComboBoxItem { Tag:CaptureBackend selected,IsEnabled:false }||selected!=foreign||!CaptureWarning.IsOpen||CaptureWarning.Severity!="Warning")throw new InvalidOperationException("Unavailable saved capture backend was hidden or overwritten.");
                var reopened=new ProfilePage();if(_profile is not null)reopened.ConfigureProfile(_profile);
                if(reopened.Settings.CaptureBackend!=foreign||Settings.CaptureBackend!=foreign)throw new InvalidOperationException("Opening profile replaced unavailable backend.");
                if(CaptureMethod.Items.Cast<ComboBoxItem>().Count(i=>i.IsEnabled)!=1||CaptureTitleBarCard.IsEnabled)throw new InvalidOperationException("Capture controls advertise an unimplemented capability.");
                await ScalingSession.VerifyUnsupportedCaptureAsync(owner,Settings);
                CaptureMethod.SelectedIndex=0;
                if(Settings.CaptureBackend!=CaptureBackend.Native||CaptureWarning.Severity!="Informational")throw new InvalidOperationException($"Selecting native capture did not recover: previous={foreign}, actual={Settings.CaptureBackend}, severity={CaptureWarning.Severity}, index={CaptureMethod.SelectedIndex}, initialized={_initialized}.");
            }
            if(leaveWarning) { Settings.CaptureBackend=CaptureBackend.GraphicsCapture;RefreshCaptureMethod(); }
            CaptureMethod.BringIntoView();await Task.Delay(80);
            Console.WriteLine("CAPTURE BACKEND UI PASS: actual profile selection handlers, unavailable foreign preferences preserved, truthful capabilities, explicit recovery and session rejection before capture/engine/picker.");
        }
        catch { Settings.CaptureBackend=previous;RefreshCaptureMethod();throw; }
    }
}
