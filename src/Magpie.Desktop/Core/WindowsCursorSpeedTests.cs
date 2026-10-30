namespace Magpie.Desktop.Core;
internal static class WindowsCursorSpeedTests
{
    private sealed class Api : IWindowsCursorSpeedApi
    {
        internal int Speed=10,Reads,Writes,FailReads,FailWrites;
        internal bool PartialWrite;
        internal bool? Acceleration=true;
        public int ReadSpeed() {Reads++;if(FailReads-->0)throw new InvalidOperationException("fixture speed read");return Speed;}
        public bool? ReadAcceleration()=>Acceleration;
        public void WriteSpeed(int speed)
        {
            Writes++;if(PartialWrite)Speed=speed;
            if(FailWrites-->0)throw new InvalidOperationException("fixture speed write");Speed=speed;
        }
    }
    internal static void Verify()
    {
        static void Check(bool ok) {if(!ok)throw new InvalidOperationException("Windows cursor speed compensation/ownership differs.");}
        Check(WindowsCursorSpeed.Compensate(10,2,true)==5&&WindowsCursorSpeed.Compensate(5,2,true)==3);
        Check(WindowsCursorSpeed.Compensate(10,2,false)==6&&WindowsCursorSpeed.Compensate(6,2,false)==4&&WindowsCursorSpeed.Compensate(20,2,false)==13);
        Check(WindowsCursorSpeed.Compensate(4,.8,false)==5); // exact tie chooses the upper sensitivity.
        Check(WindowsCursorSpeed.Compensate(20,.1,false)==20&&WindowsCursorSpeed.Compensate(1,100,false)==1);
        var api=new Api();var speed=new WindowsCursorSpeed(api);
        speed.Update(false,2,false);speed.Restore();Check(api.Reads==0&&api.Writes==0&&!speed.OwnsChange);
        speed.Update(true,2,false);speed.Update(true,2,false);Check(api.Speed==5&&api.Writes==1&&speed.OwnsChange);
        speed.Update(true,2,true);Check(api.Speed==10&&!speed.OwnsChange);
        speed.Update(true,2,false);Check(api.Speed==5);speed.Update(true,4,false);Check(api.Speed==3);
        speed.Update(false,4,false);Check(api.Speed==10&&!speed.OwnsChange);
        speed.Update(true,2,false);long generation=speed.PendingRestore;speed.Update(true,4,false);
        speed.Restore(generation);Check(api.Speed==3&&speed.OwnsChange);speed.Restore();Check(api.Speed==10);
        speed.Update(true,2,false);api.Speed=17;speed.Restore();Check(api.Speed==17&&!speed.OwnsChange);
        api.Speed=10;api.Acceleration=null;speed.Update(true,2,false);Check(api.Speed==5);
        api.FailReads=1;
        try {speed.Restore();throw new Exception("Failed read hidden.");}catch(InvalidOperationException error) when(error.Message=="fixture speed read") { }
        Check(speed.OwnsChange);speed.Restore();Check(api.Speed==10&&!speed.OwnsChange);
        api.PartialWrite=true;api.FailWrites=1;
        try {speed.Update(true,2,false);throw new Exception("Failed write hidden.");}catch(InvalidOperationException error) when(error.Message=="fixture speed write") { }
        Check(api.Speed==5&&speed.OwnsChange);speed.Restore();Check(api.Speed==10&&!speed.OwnsChange);
        speed.Update(true,2,false);api.FailWrites=1;
        try {speed.Restore();throw new Exception("Failed restoration hidden.");}catch(InvalidOperationException error) when(error.Message=="fixture speed write") { }
        Check(speed.OwnsChange);speed.Restore();Check(api.Speed==10&&!speed.OwnsChange);
        string folder=Path.Combine(Path.GetTempPath(),"magpie-cursor-speed-"+Guid.NewGuid().ToString("N"));
        try
        {
            var root=new RuntimeSettings();var copy=root.CopyForProfile();copy.AdjustCursorSpeed=false;
            root.Profiles.Add(new ApplicationProfile {Name="Speed",ExecutablePath=Path.GetFullPath("speed-test"),Settings=copy});
            string path=Path.Combine(folder,"settings.json");root.WriteFile(path);var read=RuntimeSettings.ReadFile(path);
            Check(read.AdjustCursorSpeed&&!read.Profiles[0].Settings.AdjustCursorSpeed);
        }
        finally {if(Directory.Exists(folder))Directory.Delete(folder,true);}
        Console.WriteLine("CURSOR SPEED MODEL PASS: original linear/nonlinear table and upper tie, bounded range, enter/move/exit, no redundant writes, external changes, partial failure ownership and AOT profile persistence; no system parameters touched.");
    }
}
