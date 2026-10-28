using Avalonia;
namespace Magpie.Desktop.Core;

internal static class ToolbarTests
{
    internal static void Verify()
    {
        var rect=new Rect(20,0,200,80);
        foreach(var state in Enum.GetValues<ToolbarState>())
        foreach(var (point,expected) in new[]{(new Point(30,20),1d),(new Point(30,90),1d),(new Point(30,110),.5),(new Point(30,130),0d),(new Point(-10,80),.5)})
        {
            double actual=ToolbarVisibility.Alpha(state,rect,point,false),wanted=state==ToolbarState.Off?0:state==ToolbarState.AlwaysShow?1:expected;
            if(Math.Abs(actual-wanted)>1e-8)throw new InvalidOperationException("Toolbar fade differs from original distance formula.");
        }
        if(ToolbarVisibility.Alpha(ToolbarState.AutoHide,rect,null,false)!=0 || ToolbarVisibility.Alpha(ToolbarState.AutoHide,rect,null,true)!=1)throw new InvalidOperationException("Toolbar activity did not protect active interaction.");
        if(ToolbarVisibility.Alpha(ToolbarState.AutoHide,new Rect(100,200,320,80),new Point(110,170),false)!=.5||
            ToolbarVisibility.Alpha(ToolbarState.AutoHide,new Rect(100,200,320,80),new Point(110,149),false)!=0)
            throw new InvalidOperationException("Letterboxed toolbar fade did not use its actual image-relative top.");
        if(ToolbarVisibility.Next(ToolbarState.Off)!=ToolbarState.AlwaysShow||ToolbarVisibility.Next(ToolbarState.AlwaysShow)!=ToolbarState.AutoHide||ToolbarVisibility.Next(ToolbarState.AutoHide)!=ToolbarState.Off)throw new InvalidOperationException("Toolbar shortcut state order differs from the original.");
        string path=Path.Combine(Path.GetTempPath(),"magpie-toolbar-"+Guid.NewGuid().ToString("N"),"settings.json");
        try
        {
            var settings=new RuntimeSettings { FullscreenInitialToolbarState=ToolbarState.Off,WindowedInitialToolbarState=ToolbarState.AlwaysShow };
            var copy=settings.CopyForProfile();copy.FullscreenInitialToolbarState=ToolbarState.AutoHide;
            if(settings.FullscreenInitialToolbarState!=ToolbarState.Off)throw new InvalidOperationException("Toolbar preferences leaked across profiles.");
            settings.WriteFile(path);var restored=RuntimeSettings.ReadFile(path);
            if(ToolbarVisibility.Initial(restored,false)!=ToolbarState.Off||ToolbarVisibility.Initial(restored,true)!=ToolbarState.AlwaysShow)throw new InvalidOperationException("Toolbar preferences did not survive AOT disk roundtrip.");
            foreach(int invalid in new[]{-1,3})
            {
                settings.WindowedInitialToolbarState=(ToolbarState)invalid;
                try { settings.WriteFile(path);throw new Exception("Invalid toolbar state saved."); }catch(InvalidDataException) { }
            }
        }
        finally { if(Directory.Exists(Path.GetDirectoryName(path)))Directory.Delete(Path.GetDirectoryName(path)!,true); }
        Console.WriteLine("TOOLBAR PASS: original enum/cycle/distance fade, active protection, independent preferences, AOT persistence and invalid-state rejection.");
    }
}
