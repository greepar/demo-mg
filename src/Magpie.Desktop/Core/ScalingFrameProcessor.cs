using System.Diagnostics;
using System.Runtime.InteropServices;
namespace Magpie.Desktop.Core;

// Shared by real capture sessions and deterministic GPU replay tests. Only processed output is cached.
internal sealed class ScalingFrameProcessor
{
    private readonly DuplicateFrameDetector _detector=new();
    private NativeEffectStep[] _lastPlan=[];
    private uint _lastWidth,_lastHeight;
    private long? _lastRender;
    private bool? _profilingRequested;
    private bool _sampleRequested;
    private SourceRegion? _lastRegion;
    private uint _captureWidth,_captureHeight;
    private byte[] _cropped=[];
    private string? _profilingError;
    public GpuProfileSnapshot GpuProfile { get; private set; }=GpuProfileSnapshot.Off;
    public byte[] Pixels { get; private set; } = [];
    public FramePerformance Performance { get; }
    public ScalingFrameProcessor(long start)=>Performance=new(start);
    public unsafe bool Process(EngineHandle engine,nint source,uint width,uint height,bool newFrame,NativeEffectStep[] plan,DuplicateFrameMode mode,bool dynamicStatistics,double minimumFrameRate,double maximumFrameRate,long now,bool gpuProfiling=false,SourceRegion? region=null)
    {
        if(source==0 || width==0 || height==0 || width>16384 || height>16384 || (ulong)width*height>16777216)throw new ArgumentException("Invalid capture frame.");
        var rectangle=region ?? new SourceRegion(0,0,width,height);rectangle.Validate(width,height);
        bool changed=_lastRegion!=rectangle || _captureWidth!=width || _captureHeight!=height;
        if(changed)_lastWidth=0;
        if(rectangle==new SourceRegion(0,0,width,height))
        {
            bool rendered=ProcessPixels(engine,source,width,height,newFrame,plan,mode,dynamicStatistics,minimumFrameRate,maximumFrameRate,now,gpuProfiling);
            _lastRegion=rectangle;_captureWidth=width;_captureHeight=height;return rendered;
        }
        int bytes=checked((int)((ulong)rectangle.Width*rectangle.Height*4));
        if(_cropped.Length!=bytes)_cropped=new byte[bytes];
        if(newFrame || changed)rectangle.Copy(source,width,height,_cropped);
        fixed(byte* pixels=_cropped)
        {
            bool rendered=ProcessPixels(engine,(nint)pixels,rectangle.Width,rectangle.Height,newFrame,plan,mode,dynamicStatistics,minimumFrameRate,maximumFrameRate,now,gpuProfiling);
            _lastRegion=rectangle;_captureWidth=width;_captureHeight=height;return rendered;
        }
    }
    private unsafe bool ProcessPixels(EngineHandle engine,nint source,uint width,uint height,bool newFrame,NativeEffectStep[] plan,DuplicateFrameMode mode,bool dynamicStatistics,double minimumFrameRate,double maximumFrameRate,long now,bool gpuProfiling)
    {
        if(!Enum.IsDefined(mode) || source==0 || width==0 || height==0 || (ulong)width*height>16777216 || plan.Length is <1 or >17)throw new ArgumentException("Invalid scaling source or plan.");
        if(!double.IsFinite(minimumFrameRate) || minimumFrameRate is <0 or >1000 || !double.IsFinite(maximumFrameRate) || maximumFrameRate<0 || maximumFrameRate>1000)throw new ArgumentException("Invalid frame rate.");
        if(_profilingRequested!=gpuProfiling)
        {
            _profilingRequested=gpuProfiling;_profilingError=null;
            try
            {
                if(NativeCore.SetGpuProfiling(engine,gpuProfiling?1:0)==0)throw NativeCore.Error();
                GpuProfile=GpuProfileSnapshot.Read(engine);_sampleRequested=gpuProfiling && GpuProfile.Supported;
            }
            catch(Exception failure)
            {
                _profilingError=failure.Message;_sampleRequested=false;
                NativeCore.SetGpuProfiling(engine,0);GpuProfile=new(false,gpuProfiling,3,0,0,[],failure.Message);
            }
        }
        bool changed=width!=_lastWidth || height!=_lastHeight || !MemoryMarshal.AsBytes(plan.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(_lastPlan.AsSpan()));
        FrameDetection detection=default;
        if(newFrame)
        {
            Performance.Receive();long begin=Stopwatch.GetTimestamp();
            detection=_detector.Observe(new ReadOnlySpan<byte>((void*)source,checked((int)((ulong)width*height*4))),width,height,mode,dynamicStatistics);
            Performance.Detect(detection,Stopwatch.GetElapsedTime(begin).TotalMilliseconds);
        }
        double minimum=maximumFrameRate>0 ? Math.Min(minimumFrameRate,maximumFrameRate) : minimumFrameRate;
        bool refresh=_lastRender is {} previous && minimum>0 && now-previous>=Math.Ceiling(Stopwatch.Frequency/minimum);
        if(!changed && (!newFrame || detection.Duplicate) && !refresh && !_sampleRequested)
        {
            if(newFrame && detection.Duplicate)Performance.Skip();return false;
        }
        var output=plan[^1];
        if(output.Width==0 || output.Height==0 || (ulong)output.Width*output.Height>16777216)throw new ArgumentException("Invalid output size.");
        int bytes=checked((int)((ulong)output.Width*output.Height*4));
        if(Pixels.Length!=bytes)Pixels=new byte[bytes];
        long scaleBegin=Stopwatch.GetTimestamp();
        fixed(byte* destination=Pixels)fixed(NativeEffectStep* steps=plan)
            if(NativeCore.ScaleChain(engine,source,width,height,destination,(ulong)bytes,steps,(uint)plan.Length)==0)throw NativeCore.Error();
        Performance.Scale(refresh && !changed && (!newFrame || detection.Duplicate),Stopwatch.GetElapsedTime(scaleBegin).TotalMilliseconds);
        if(gpuProfiling && _profilingError is null)
        {
            try { GpuProfile=GpuProfileSnapshot.Read(engine); }
            catch(Exception failure) { GpuProfile=new(false,true,3,0,0,[],failure.Message); }
            Performance.RecordGpu(GpuProfile);
        }
        _sampleRequested=false;
        _lastWidth=width;_lastHeight=height;_lastPlan=(NativeEffectStep[])plan.Clone();_lastRender=now;
        return true;
    }
    public int IdleDelayMilliseconds(double minimumFrameRate,double maximumFrameRate,long now)
    {
        double minimum=maximumFrameRate>0 ? Math.Min(minimumFrameRate,maximumFrameRate) : minimumFrameRate;
        if(_lastRender is not {} last || minimum<=0)return 10;
        double remaining=(last+Math.Ceiling(Stopwatch.Frequency/minimum)-now)*1000/Stopwatch.Frequency;
        return (int)Math.Clamp(Math.Ceiling(remaining),1,10);
    }
}
