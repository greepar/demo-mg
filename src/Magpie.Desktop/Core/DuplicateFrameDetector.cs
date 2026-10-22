namespace Magpie.Desktop.Core;

internal enum DuplicateFrameMode { Always,Dynamic,Never }
internal readonly record struct FrameDetection(bool Duplicate,bool Compared,bool Bypassed,bool PredictionMiss);
// Same 16/1/16 adaptive schedule as FrameSourceBase.cpp. Current capture delivers CPU RGBA8.
internal sealed class DuplicateFrameDetector
{
    private byte[] _previous=[];
    private uint _width,_height;
    private DuplicateFrameMode? _mode;
    private bool _statistics,_checking=true;
    private int _left=16,_nextSkip=1;
    public FrameDetection Observe(ReadOnlySpan<byte> source,uint width,uint height,DuplicateFrameMode mode,bool statistics)
    {
        if(!Enum.IsDefined(mode) || width==0 || height==0 || (ulong)width*height*4!=(ulong)source.Length)throw new ArgumentException("Invalid duplicate detector frame or mode.");
        if(_mode!=mode || _statistics!=statistics || _width!=width || _height!=height)
        {
            _previous=[];_mode=mode;_statistics=statistics;_width=width;_height=height;_checking=true;_left=16;_nextSkip=1;
        }
        if(mode==DuplicateFrameMode.Never)return default;
        if(_previous.Length==0) { Save(source);return default; }
        if(mode==DuplicateFrameMode.Always)
        {
            bool same=source.SequenceEqual(_previous);if(!same)Save(source);
            return new(same,true,false,false);
        }
        if(_checking)
        {
            if(--_left==0) { _checking=false;_left=_nextSkip;if(_nextSkip<16)++_nextSkip; }
            bool same=source.SequenceEqual(_previous);
            if(same) { _checking=true;_left=16;_nextSkip=1;return new(true,true,false,false); }
            if(_checking || statistics)Save(source);
            return new(false,true,false,false);
        }
        if(--_left==0)
        {
            _checking=true;_left=(-4*_nextSkip+78)/7;
            if(!statistics)Save(source);
        }
        if(statistics)
        {
            bool same=source.SequenceEqual(_previous);if(!same)Save(source);
            return new(false,true,true,same);
        }
        return new(false,false,true,false);
    }
    private void Save(ReadOnlySpan<byte> source)
    {
        if(_previous.Length!=source.Length)_previous=new byte[source.Length];
        source.CopyTo(_previous);
    }
}
