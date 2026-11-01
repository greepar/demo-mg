using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Magpie.Desktop.Controls;
using Magpie.Desktop.Core;
using Magpie.Desktop.Views;
namespace Magpie.Desktop;

public partial class MainWindow
{
    internal async Task<string> VerifyProfileIconsAsync()
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Profile icon fixtures require isolated settings.");
        var root=RuntimeSettings.Current;
        var a=new ApplicationProfile {Name="Application icon",ExecutablePath=Path.Combine(Path.GetTempPath(),"alpha-app"),Settings=root.CopyForProfile()};
        var b=new ApplicationProfile {Name="Fallback icon",ExecutablePath=Path.Combine(Path.GetTempPath(),"beta-app"),Settings=root.CopyForProfile()};
        var first=new WindowsApplicationIcon(2,2,[128,0,0,128,0,255,0,255,0,0,255,255,255,255,255,255]);
        var second=new WindowsApplicationIcon(1,1,[11,22,33,255]);
        var oldA=new TaskCompletionSource<WindowsApplicationIcon?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldB=new TaskCompletionSource<WindowsApplicationIcon?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing=new TaskCompletionSource<WindowsApplicationIcon?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Button Navigation(ApplicationProfile profile)=>ProfilesNavItems.Children.OfType<Button>().Single(button=>button.Tag as string=="Profile:"+profile.Id);
        Control NavIcon(ApplicationProfile profile)=>((StackPanel)Navigation(profile).Content!).Children[0];
        ProfilePage Page(ApplicationProfile profile)=>(ProfilePage)_pages["Profile:"+profile.Id];
        Control? HeaderIcon(ApplicationProfile profile)=>Page(profile).FindControl<PageFrame>("ProfileFrame")!.Icon;
        void Require(bool condition,string message) {if(!condition)throw new InvalidOperationException("Profile icons: "+message);}
        try
        {
            root.Profiles.Clear();root.Profiles.AddRange([a,b]);
            uint requested=0;
            _profileIconFixture=(path,size)=> {requested=size;return path==a.ExecutablePath?oldA.Task:oldB.Task;};
            RefreshProfilesNavigation();var staleTasks=_profileIconLoads.ToArray();var staleNavigation=Navigation(a);
            Require(NavIcon(a) is SymbolIcon&&HeaderIcon(a) is SymbolIcon&&_profileIconBitmaps.Count==0,"Pending icon load blocked placeholders.");
            _profileIconFixture=(path,size)=>path==a.ExecutablePath?Task.FromResult<WindowsApplicationIcon?>(first):Task.FromException<WindowsApplicationIcon?>(new IOException("Test executable icon failure"));
            RefreshProfilesNavigation();await Task.WhenAll(_profileIconLoads.ToArray());
            Require(requested>=24&&NavIcon(a) is Image&&HeaderIcon(a) is Image&&NavIcon(b) is SymbolIcon&&HeaderIcon(b) is SymbolIcon&&_profileIconBitmaps.Count==1,
                "Executable icon did not reach both controls or failure removed its fallback.");
            var current=(Bitmap)((Image)NavIcon(a)).Source!;
            Require(ReferenceEquals(((Image)HeaderIcon(a)!).Source,current)&&!ReferenceEquals(NavIcon(a),HeaderIcon(a)),"Header/navigation reparented one control or lost shared immutable image data.");
            oldA.SetResult(second);oldB.SetResult(second);await Task.WhenAll(staleTasks);
            Require(ReferenceEquals(((Image)NavIcon(a)).Source,current)&&_profileIconBitmaps.Count==1&&((StackPanel)staleNavigation.Content!).Children[0] is SymbolIcon,
                "Stale loads allocated a new bitmap or overwrote refreshed controls.");
            root.Profiles.Remove(a);_profileIconFixture=(_,_)=>Task.FromResult<WindowsApplicationIcon?>(first);
            RefreshProfilesNavigation();await Task.WhenAll(_profileIconLoads.ToArray());
            Require(_profileIconBitmaps.Count==1&&!_pages.ContainsKey("Profile:"+a.Id)&&ProfilesNavItems.Children.Count==1&&NavIcon(b) is Image,"Delete/refresh retained old image/page ownership.");
            _profileIconFixture=(_,_)=>closing.Task;RefreshProfilesNavigation();var closeTasks=_profileIconLoads.ToArray();
            _profileIconsClosed=true;ClearProfileIcons();closing.SetResult(first);await Task.WhenAll(closeTasks);
            Require(_profileIconBitmaps.Count==0&&NavIcon(b) is SymbolIcon&&HeaderIcon(b) is SymbolIcon,"Late result after shutdown resurrected disposed image resources.");
            _profileIconsClosed=false;_profileIconFixture=(_,_)=>Task.FromResult<WindowsApplicationIcon?>(first);
            RefreshProfilesNavigation();await Task.WhenAll(_profileIconLoads.ToArray());Navigate("Profile:"+b.Id);
            await Task.Delay(80);
            Console.WriteLine("PROFILE ICON UI PASS: actual sidebar/header controls, independent Image instances/shared bitmap, placeholder/failure, stale refresh, deleted profile, close/late-result rejection and pixel size request; executable reader synthetic, no user desktop or Shell query.");
            return b.Id;
        }
        finally
        {
            oldA.TrySetResult(null);oldB.TrySetResult(null);closing.TrySetResult(null);
            // Keep the final isolated screenshot alive; the window's actual
            // Closed handler owns its last bitmap and performs final disposal.
            _profileIconFixture=null;
        }
    }
}
