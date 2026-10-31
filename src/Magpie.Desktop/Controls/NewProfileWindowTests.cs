using Avalonia.Controls;
using Avalonia.Interactivity;
using Magpie.Desktop.Core;
namespace Magpie.Desktop.Controls;

public partial class NewProfileFlyoutContent
{
    internal async Task<string> VerifyWindowsPickerAsync(Func<Task> reopen)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Window picker fixture requires isolated settings.");
        var root=RuntimeSettings.Current;int original=root.Profiles.Count;
        var a=WindowsProfileCandidatesTests.Example(title:"Stale result");
        var b=WindowsProfileCandidatesTests.Example(20,"C:\\Games\\B.exe",title:"B window");
        var c=WindowsProfileCandidatesTests.Example(30,"C:\\Games\\C.exe",title:"C window");
        var d=WindowsProfileCandidatesTests.Example(40,"C:\\Games\\D.exe",title:"D window");
        var e=WindowsProfileCandidatesTests.Example(50,"C:\\Games\\E.exe",title:"E window with a long descriptive title for popup layout");
        void Require(bool value,string message) {if(!value)throw new InvalidOperationException("Window picker UI: "+message);}
        SetCandidateFixture(()=>Task.FromResult<IReadOnlyList<WindowsProfileCandidate>>([]));await RefreshSourcesAsync();
        Require(NoCandidates.IsVisible&&!ProfileFields.IsVisible&&!CreateButton.IsEnabled,"Empty window list exposed a fake source or create action.");
        SetCandidateFixture(()=>Task.FromException<IReadOnlyList<WindowsProfileCandidate>>(new IOException("Test source enumeration failure")));await RefreshSourcesAsync();
        Require(ErrorText.IsVisible&&!CreateButton.IsEnabled&&root.Profiles.Count==original,"Enumeration failure created a profile or lost its error.");
        var pending=new TaskCompletionSource<IReadOnlyList<WindowsProfileCandidate>>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetCandidateFixture(()=>pending.Task);var stale=RefreshSourcesAsync();
        SetCandidateFixture(()=>Task.FromResult<IReadOnlyList<WindowsProfileCandidate>>([b,c]));await RefreshSourcesAsync();
        pending.SetResult([a]);await stale;
        Require(CandidateWindows.Items.Count==2&&CandidateWindows.SelectedIndex==-1&&!CreateButton.IsEnabled,"Old async refresh replaced current candidates or auto-selected multiple windows.");
        CandidateWindows.SelectedIndex=0;
        Require(NameInput.Text==b.DefaultName&&CreateButton.IsEnabled,"Selecting a real snapshot did not fill the default name and enable creation.");
        NameInput.Text="Created from window";
        ApplicationProfile? created=null;void Observe(ApplicationProfile profile)=>created=profile;
        Created+=Observe;
        try {CreateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}finally {Created-=Observe;}
        Require(created?.WindowsRule?.PathOrAumid==b.Window.Source.ExecutablePath&&created.OriginalProfile is null&&root.Profiles.Count==original+1,
            "Actual create handler did not persist a typed native rule or retained a fake import archive.");
        created!.Settings.AutoScale=AutoScaleMode.Windowed;
        SetCandidateFixture(()=>Task.FromResult<IReadOnlyList<WindowsProfileCandidate>>([b,c]));await reopen();await Task.Delay(80);
        Require(CandidateWindows.Items.Count==1&&CandidateWindows.SelectedIndex==0&&NameInput.Text==c.DefaultName,"Existing rule was not filtered or the sole available window was not selected.");
        root.AddWindowsProfile("Concurrent profile",c.Window);
        int before=root.Profiles.Count;CreateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(ErrorText.IsVisible&&root.Profiles.Count==before,"Concurrent duplicate creation was not rejected without mutation.");
        SetCandidateFixture(()=>Task.FromResult<IReadOnlyList<WindowsProfileCandidate>>([b,c,d,e]));await RefreshSourcesAsync();
        Require(CandidateWindows.Items.Count==2&&!ErrorText.IsVisible,"Refresh did not remove duplicate identities and recover from error.");
        CandidateWindows.SelectedIndex=0;CopySource.SelectedIndex=1;
        Console.WriteLine("WINDOW PICKER UI PASS: actual refresh/select/create handlers, empty/failure states, stale async result rejection, default name, typed rule creation, reopen/filter/single selection, duplicate race and recovery; all window metadata synthetic, no desktop enumeration or capture.");
        return created.Id;
    }
}
