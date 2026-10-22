using Avalonia.Controls;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    private (int Selection,float Custom)? _initialSizingPreference;
    private RuntimeSettings InitialSizingSettings()
    {
        var preference=_initialSizingPreference ??= (_settings.InitialWindowedScaleFactor,_settings.CustomInitialWindowedScaleFactor);
        return new RuntimeSettings { InitialWindowedScaleFactor=preference.Selection,CustomInitialWindowedScaleFactor=preference.Custom };
    }
    private void SetSourceGeometry(uint width,uint height)
    {
        _sourceWidth=width;_sourceHeight=height;
        if(width>0 && height>0)_initialSizingPreference ??= (_settings.InitialWindowedScaleFactor,_settings.CustomInitialWindowedScaleFactor);
        PositionImage();
    }
    private void ApplyInitialWindowSize(Window owner,bool reconcile=false)
    {
        if(_output is null || _sourceWidth==0 || _sourceHeight==0 || _output.WindowState==WindowState.FullScreen || _initialSizingApplied && !reconcile)return;
        var screen=_output.Screens.ScreenFromWindow(_output.IsVisible?_output:owner) ?? _output.Screens.Primary ?? throw new InvalidOperationException("Display work area is unavailable.");
        double scale=_output.IsVisible?_output.RenderScaling:owner.RenderScaling;
        var client=_output.ClientSize;var frame=_output.FrameSize ?? client;
        uint decorationWidth=(uint)Math.Max(0,Math.Round((frame.Width-client.Width)*scale,MidpointRounding.AwayFromZero));
        uint decorationHeight=(uint)Math.Max(0,Math.Round((frame.Height-client.Height)*scale,MidpointRounding.AwayFromZero));
        var size=WindowSizing.Initial(InitialSizingSettings(),_sourceWidth,_sourceHeight,(uint)screen.WorkingArea.Width,(uint)screen.WorkingArea.Height,scale,decorationWidth,decorationHeight);
        _output.MinWidth=size.MinimumWidth/scale;_output.MinHeight=size.MinimumHeight/scale;
        _output.Width=size.Width/scale;_output.Height=size.Height/scale;
        _initialSizingApplied=true;UpdateSize();
    }
}
