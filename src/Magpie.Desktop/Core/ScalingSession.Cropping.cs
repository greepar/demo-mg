using Avalonia.Controls;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    private void OpenCapturedOutput(Window owner,bool fullscreen,uint width,uint height)
    {
        _captureFrameWidth=width;_captureFrameHeight=height;
        var crop=SourceRegion.Resolve(_settings,width,height);
        OpenOutput(owner,fullscreen,crop.Width,crop.Height);
    }
    private SourceRegion PrepareCapturedSource(uint width,uint height)
    {
        _captureFrameWidth=width;_captureFrameHeight=height;
        var crop=SourceRegion.Resolve(_settings,width,height);
        SetSourceGeometry(crop.Width,crop.Height);return crop;
    }
}
