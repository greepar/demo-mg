using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Views;

public partial class SettingsPage
{
    internal async Task VerifyImportUiAsync()
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Import tests require isolated preview mode.");
        static void Invoke(Button button)
            =>ControlAutomationPeer.CreatePeerForElement(button)!.GetProvider<IInvokeProvider>()!.Invoke();
        static void Require(bool value,string message)
        { if(!value)throw new InvalidOperationException("Configuration import UI: "+message); }
        async Task Idle()
        {
            for(int i=0;i<100&&_importBusy;i++)await Task.Delay(10);
            Require(!_importBusy,"Import did not complete.");await Task.Delay(30);
        }
        var original=RuntimeSettings.Current;
        string root=Path.Combine(Path.GetTempPath(),"magpie-import-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            _previewImportDestination=Path.Combine(root,"settings.json");File.WriteAllText(_previewImportDestination,"exact original bytes, not JSON\n");
            _previewImportSource=()=>Task.FromResult<string?>(OriginalConfigurationTests.Example);
            int restarts=0;_previewImportRestart=()=> { restarts++;throw new IOException("simulated restart refusal"); };
            ChooseConfigurationButton.BringIntoView();await Task.Delay(30);
            Invoke(ChooseConfigurationButton);await Idle();
            Require(_importDraft?.Settings.Profiles.Count==2&&ImportConfigurationReview.IsVisible&&ImportConfigurationWarnings.Text!.Contains("graphicsCardId")&&File.ReadAllText(_previewImportDestination)=="exact original bytes, not JSON\n","Review changed disk or hid migration notes.");
            Invoke(CancelConfigurationButton);await Idle();
            Require(_importDraft is null&&!ImportConfigurationReview.IsVisible&&restarts==0,"Cancel imported or restarted.");
            _previewImportSource=()=>Task.FromResult<string?>(null);
            Invoke(ChooseConfigurationButton);await Idle();
            Require(_importDraft is null&&!ImportConfigurationReview.IsVisible,"Cancelled picker reused an old draft.");
            _previewImportSource=()=>Task.FromResult<string?>("{\"scalingModes\":[]}");
            Invoke(ChooseConfigurationButton);await Idle();
            Require(_importDraft is null&&ImportConfigurationError.IsVisible&&ChooseConfigurationButton.IsEnabled,"Malformed input was accepted or blocked retry.");
            _previewImportSource=()=>Task.FromResult<string?>(OriginalConfigurationTests.Example);
            Invoke(ChooseConfigurationButton);await Idle();
            Invoke(ApplyConfigurationButton);await Idle();
            Require(restarts==1&&ImportConfigurationError.IsVisible&&ImportConfigurationReview.IsVisible&&ApplyConfigurationButton.IsEnabled&&File.ReadAllText(_previewImportDestination)=="exact original bytes, not JSON\n","Restart failure did not restore bytes and retain retry.");
            _previewImportRestart=()=>restarts++;
            Invoke(ApplyConfigurationButton);await Idle();
            var imported=RuntimeSettings.ReadFile(_previewImportDestination);
            Require(restarts==2&&!ImportConfigurationReview.IsVisible&&imported.Profiles.Count==2&&imported.Theme==2&&ReferenceEquals(original,RuntimeSettings.Current),"Successful import changed old process state or lost settings.");
            Require(Directory.GetFiles(root,"*.before-import-*.json").All(file=>File.ReadAllText(file)=="exact original bytes, not JSON\n"),"Backups lost exact prior contents.");
            // Render the actual imported app profile: no fake host executable or launch/location action.
            var profile=new ProfilePage();profile.ConfigureProfile(imported.Profiles[0]);
            Require(!profile.FindControl<Button>("LaunchProfileButton")!.IsEnabled&&!profile.FindControl<MenuItem>("ProgramLocationMenu")!.IsEnabled&&profile.FindControl<TextBlock>("ImportedProfileNotice")!.Text!.Contains("C:\\Games\\example.exe"),"Foreign identity did not disable launch/location or display its original rule.");
            _previewImportSource=()=>Task.FromResult<string?>(OriginalConfigurationTests.Example);
            Invoke(ChooseConfigurationButton);await Idle();ApplyConfigurationButton.BringIntoView();await Task.Delay(50);
            Console.WriteLine("CONFIG IMPORT UI PASS: actual automation Invoke, review, cancel, picker cancellation, malformed input, restart refusal rollback/retry, exact backups and foreign profile guards; picker/restart are isolated fixtures.");
        }
        finally
        {
            _previewImportSource=null;_previewImportRestart=null;_previewImportDestination=null;
            if(Directory.Exists(root))Directory.Delete(root,true);
        }
    }
}
