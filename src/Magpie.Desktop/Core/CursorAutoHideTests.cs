namespace Magpie.Desktop.Core;
internal static class CursorAutoHideTests
{
    internal static void Verify()
    {
        static void Check(bool value) {if(!value)throw new InvalidOperationException("Original cursor auto-hide behavior differs.");}
        var state=new CursorAutoHide();
        bool Draw(double time,int x=1,nint shape=1,bool captured=true,bool moving=false,bool enabled=true)=>
            state.Visible(TimeSpan.FromSeconds(time),x,2,shape,captured,moving,enabled,1);
        Check(Draw(0)&&Draw(1)&&!Draw(1.01)); // Original strict > boundary.
        Check(!Draw(2,shape:0)&&!Draw(3)); // arrow -> hidden -> same arrow stays idle.
        Check(Draw(4,shape:2)&&Draw(5,shape:2)&&!Draw(5.01,shape:2));
        Check(!Draw(6,x:3,shape:0)&&Draw(7,x:3,shape:2)); // Movement while source-hidden resets.
        Check(Draw(20,moving:true)&&Draw(21)&&!Draw(21.01));
        Check(Draw(30,captured:false)&&Draw(31)&&!Draw(31.01));
        Check(Draw(40,enabled:false)&&Draw(50)&&!Draw(51.01));
        state.Reset();Check(Draw(60));
        string folder=Path.Combine(Path.GetTempPath(),"magpie-cursor-idle-"+Guid.NewGuid().ToString("N"));
        string path=Path.Combine(folder,"settings.json");
        try
        {
            var settings=new RuntimeSettings {AutoHideCursorEnabled=true,AutoHideCursorDelay=.1};
            var copy=settings.CopyForProfile();copy.AutoHideCursorEnabled=false;copy.AutoHideCursorDelay=5;
            settings.Profiles.Add(new ApplicationProfile {Name="Pointer",ExecutablePath=Path.GetFullPath("cursor-test"),Settings=copy});
            settings.WriteFile(path);var read=RuntimeSettings.ReadFile(path);
            Check(read.AutoHideCursorEnabled&&read.AutoHideCursorDelay==.1&&!read.Profiles[0].Settings.AutoHideCursorEnabled&&read.Profiles[0].Settings.AutoHideCursorDelay==5);
            foreach(double invalid in new[]{0,.09,5.01,double.NaN,double.PositiveInfinity})
            {
                settings.AutoHideCursorDelay=invalid;
                try {settings.WriteFile(path);throw new Exception("Invalid idle interval saved.");}catch(InvalidDataException) { }
            }
        }
        finally {if(Directory.Exists(folder))Directory.Delete(folder,true);}
        Console.WriteLine("CURSOR AUTO-HIDE PASS: original shape/position/hidden-source/moving/capture rules, strict deadline, independent profiles and AOT persistence.");
    }
}
