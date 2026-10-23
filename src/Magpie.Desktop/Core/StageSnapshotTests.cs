using System.Runtime.InteropServices;
namespace Magpie.Desktop.Core;

internal static class StageSnapshotTests
{
    internal static unsafe void Verify(EngineHandle engine)
    {
        static void Require(bool value,string message)
        { if(!value)throw new InvalidOperationException("Stage snapshot: "+message); }
        using var empty=NativeCore.Create();if(empty.IsInvalid)throw NativeCore.Error();
        Require(Marshal.SizeOf<StageSnapshot>()==32,"ABI layout");
        Require(NativeCore.StageSnapshotInfo(empty,0,out _,32)==0,"Unexecuted engine fabricated an output.");
        Require(NativeCore.SetGpuProfiling(engine,1)==1,"Enable profiling before readback test.");
        byte[] input=[1,3,5,255,11,17,23,255,31,41,47,255,53,61,71,255,79,89,97,255,101,107,113,255];
        NativeEffectStep[] steps=[new(){Effect=0,Width=6,Height=4},new(){Effect=3,Width=4,Height=3,Parameter1=0,Parameter2=.5f},new(){Effect=13,Width=8,Height=6}];
        byte[] final=new byte[8*6*4];
        fixed(byte* source=input,target=final)fixed(NativeEffectStep* plan=steps)
            Require(NativeCore.ScaleChain(engine,(nint)source,3,2,target,(ulong)final.Length,plan,3)==1,"GPU chain failed.");
        var snapshots=StageSnapshot.Read(engine);Require(snapshots.Length==3,"Stage count");
        byte[] first=new byte[6*4*4],second=new byte[4*3*4];
        fixed(byte* source=input,target=first)Require(NativeCore.ScaleExtended(empty,(nint)source,3,2,target,6,4,0,0,0)==1,"Independent nearest output failed.");
        fixed(byte* source=first,target=second)Require(NativeCore.ScaleExtended(empty,(nint)source,6,4,target,4,3,3,0,.5f)==1,"Independent bicubic output failed.");
        Require(snapshots[0].Copy(engine).SequenceEqual(first)&&snapshots[1].Copy(engine).SequenceEqual(second)&&snapshots[2].Copy(engine).SequenceEqual(final),"Stage pixels/dimensions did not match independent execution/final output.");
        Require(snapshots[2].Effect==13&&snapshots[2].Width==8&&snapshots[2].Height==6,"Multipass stage metadata");
        var profile=GpuProfileSnapshot.Read(engine);
        if(profile.Supported)Require(profile.Sequence>0,"No actual timed execution to compare.");
        _=snapshots[1].Copy(engine);Require(GpuProfileSnapshot.Read(engine).Sequence==profile.Sequence,"Readback submitted an extra GPU execution.");
        byte[] untouched=Enumerable.Repeat((byte)123,first.Length+1).ToArray();
        fixed(byte* target=untouched)
        {
            Require(NativeCore.CopyStageSnapshot(engine,snapshots[0].Serial,0,target,(ulong)untouched.Length)==0,"Wrong length accepted.");
            Require(NativeCore.CopyStageSnapshot(engine,snapshots[0].Serial,3,target,(ulong)first.Length)==0,"Out-of-range stage accepted.");
            Require(NativeCore.CopyStageSnapshot(engine,0,0,target,(ulong)first.Length)==0,"Zero serial accepted.");
        }
        Require(untouched.All(value=>value==123),"Failed read changed caller bytes.");
        Require(NativeCore.StageSnapshotInfo(engine,0,out _,31)==0&&NativeCore.StageSnapshotInfo(engine,3,out _,32)==0,"Invalid manifest query accepted.");
        NativeEffectStep one=new(){Effect=0,Width=3,Height=2};byte[] next=new byte[input.Length];
        fixed(byte* source=input,target=next)Require(NativeCore.ScaleChain(engine,(nint)source,3,2,target,(ulong)next.Length,&one,1)==1,"Replacement execution failed.");
        var latest=StageSnapshot.Read(engine);Require(latest.Length==1&&latest[0].Serial>snapshots[0].Serial,"Execution serial/stage list did not update.");
        fixed(byte* target=untouched)Require(NativeCore.CopyStageSnapshot(engine,snapshots[0].Serial,0,target,(ulong)first.Length)==0,"Stale execution copied a newer frame.");
        Require(untouched.All(value=>value==123),"Stale copy changed caller bytes.");
        Require(latest[0].Copy(engine).SequenceEqual(input),"Replacement stage not exact.");
        string name=ScreenshotNames.WithSuffix(ScreenshotNames.Format("%WT%",new string('中',300),"",DateTime.Now),"阶段😀");
        Require(System.Text.Encoding.UTF8.GetByteCount(name)<=224&&!name.Contains('\uFFFD'),"Suffixed filename overflow or split Unicode.");
        Require(NativeCore.SetGpuProfiling(engine,0)==1,"Restore profiler preference.");
        Console.WriteLine("STAGE SNAPSHOT PASS: actual nearest/bicubic/NNEDI3 chain outputs, independent pixels, 32-byte ABI, serial/count refresh, stale/bounds/length rejection without writes, no extra GPU work and Unicode filename budget.");
    }
}
