namespace Magpie.Desktop.Core;

internal static class PassSnapshotTests
{
    private static void Require(bool value,string message)
    { if(!value)throw new InvalidOperationException("Pass snapshot: "+message); }
    internal static unsafe void Verify(EngineHandle engine)
    {
        var formats=new HashSet<uint>();int count=0;
        foreach(uint effect in new uint[]{8,13,20,22})
        {
            uint factor=GraphCatalog.FixedScale(effect);if(factor==0)factor=1;
            uint width=4,height=4,outWidth=width*factor,outHeight=height*factor;
            byte[] input=Enumerable.Range(0,(int)(width*height*4)).Select(i=>(byte)(i%4==3?255:80)).ToArray();
            byte[] final=new byte[outWidth*outHeight*4];NativeEffectStep step=new(){Effect=effect,Width=outWidth,Height=outHeight};
            fixed(byte* source=input,target=final)Require(NativeCore.ScaleChain(engine,(nint)source,width,height,target,(ulong)final.Length,&step,1)==1,"Initial effect execution failed.");
            var stage=StageSnapshot.Read(engine)[0];var descriptions=PassExportCatalog.All[effect];
            ulong sequence=GpuProfileSnapshot.Read(engine).Sequence;
            byte[]? firstOutput=null,reusedOutput=null;PassSnapshot firstInfo=default;
            for(uint pass=0;pass<descriptions.Length;pass++)
            for(uint output=0;output<descriptions[pass].Outputs.Length;output++)
            {
                var info=PassSnapshot.Read(engine,0,pass,output);var expected=descriptions[pass].Outputs[output];
                Require(info.Serial==stage.Serial&&info.PassCount==descriptions.Length&&info.OutputCount==descriptions[pass].Outputs.Length&&info.Format==expected.Format&&info.Texture==(expected.Texture<0?uint.MaxValue:(uint)expected.Texture),"Original pass/texture mapping differs.");
                byte[] raw=info.Copy(engine);formats.Add(info.Format);count++;
                if(info.Texture==uint.MaxValue)Require(raw.SequenceEqual(final),"Final pass export changed the rendered output.");
                else
                {
                    var texture=GraphCatalog.Find(effect)!.Textures[info.Texture];uint[] dimensions=[width,height,outWidth,outHeight,1];
                    Require(info.Width==dimensions[texture.WidthAxis]*texture.WidthFactor&&info.Height==dimensions[texture.HeightAxis]*texture.HeightFactor,"Raw output geometry differs.");
                    using var dds=new MemoryStream();DdsExport.Write(dds,info,raw);
                    Require(dds.ToArray().AsSpan(148).SequenceEqual(raw),"DDS conversion changed signed/float/channel data.");
                    if(effect==13)
                    {
                        for(int y=0;y<info.Height;y+=2)for(int x=0;x<info.Width;x++)
                        {
                            float value=(float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(raw,(int)((y*info.Width+x)*2)));
                            Require(Math.Abs(value-80f/255)<.001f,"NNEDI3 even-row R16F sample differs from independent source luminance.");
                        }
                    }
                    if(effect==8&&pass==0&&output==0) { firstOutput=raw;firstInfo=info; }
                    if(effect==8&&pass==2&&output==0)reusedOutput=raw;
                }
                byte[] untouched=Enumerable.Repeat((byte)123,raw.Length+1).ToArray();
                fixed(byte* target=untouched)
                {
                    Require(NativeCore.CopyPassSnapshot(engine,info.Serial,0,pass,output,target,info.Bytes+1)==0,"Wrong output length accepted.");
                    Require(NativeCore.CopyPassSnapshot(engine,0,0,pass,output,target,info.Bytes)==0,"Zero serial accepted.");
                }
                Require(untouched.All(value=>value==123),"Rejected raw copy changed caller bytes.");
            }
            if(effect==8)
            {
                Require(firstOutput is not null&&reusedOutput is not null&&!firstOutput.SequenceEqual(reusedOutput),"Fixture did not exercise overwritten multi-output texture.");
                Require(firstInfo.Copy(engine).SequenceEqual(firstOutput!),"Replayed earlier pass returned later overwritten texels.");
            }
            Require(StageSnapshot.Read(engine)[0].Copy(engine).SequenceEqual(final),"Intermediate replay overwrote final display pixels.");
            Require(GpuProfileSnapshot.Read(engine).Sequence==sequence,"Developer replay changed the recorded displayed-frame profiler sample.");
            Require(NativeCore.PassSnapshotInfo(engine,0,0,0,out _,55)==0&&NativeCore.PassSnapshotInfo(engine,0,16,0,out _,56)==0&&NativeCore.PassSnapshotInfo(engine,0,0,32,out _,56)==0,"Invalid pass query accepted.");
            fixed(byte* source=input,target=final)Require(NativeCore.ScaleChain(engine,(nint)source,width,height,target,(ulong)final.Length,&step,1)==1,"Normal rendering did not recover after replay.");
            byte[] guard=new byte[final.Length];fixed(byte* target=guard)
                Require(NativeCore.CopyPassSnapshot(engine,stage.Serial,0,(uint)descriptions.Length-1,0,target,(ulong)guard.Length)==0,"Stale execution copied a new pass.");
        }
        Require(formats.SetEquals(new uint[]{0,1,2,3,5}),"GPU export did not cover all five writable corpus formats (R8 is SOURCE-only).");
        Console.WriteLine($"PASS SNAPSHOT GPU PASS: {count} original pass outputs, multi-output/reused RGBA16F replay, R16F independent luminance, RG16F/RG8/RGBA8, raw DDS bytes, final preservation, rejection and normal-render recovery. Developer replay submits GPU work without fabricating a new displayed frame.");
    }
}
