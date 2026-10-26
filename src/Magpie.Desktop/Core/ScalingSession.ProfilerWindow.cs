using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    private Canvas? _profilerLayer;
    private Border? _profilerWindow,_profilerHeader;
    private Point? _profilerDragStart;
    private Point _profilerDragOrigin;
    private IPointer? _profilerPointer;
    private bool _positioningProfiler;
    private TextBlock? _profilerInfo;
    private EngineHandle? _profilerInfoEngine;
    private ProfilerSessionInfo? _profilerSessionInfo;
    private PerformanceSnapshot? _profilerMetrics;
    private DuplicateFrameMode _profilerDetection;
    private bool _profilerDynamicStatistics;

    private Canvas CreateProfilerWindow()
    {
        var close=new Button { Name="CloseProfilerButton",Content="×",Width=28,Height=28,MinWidth=0,Padding=new Thickness(0) };
        Avalonia.Automation.AutomationProperties.SetName(close,Localization.Get("Overlay_Toolbar_Close"));
        close.Click+=(_,_)=> { if(_gpuProfilingToggle is {} toggle)toggle.IsChecked=false; };
        var title=new TextBlock { Text=Localization.Get("Overlay_Profiler"),Foreground=Brushes.White,VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeight.SemiBold };
        var headerGrid=new Grid { ColumnDefinitions=new ColumnDefinitions("*,Auto"),Children={title,close} };Grid.SetColumn(close,1);
        _profilerHeader=new Border { Name="ProfilerDragHeader",Background=Brushes.Transparent,Child=headerGrid };
        _profilerHeader.PointerPressed+=ProfilerPressed;
        _profilerHeader.PointerMoved+=ProfilerMoved;
        _profilerHeader.PointerReleased+=ProfilerReleased;
        _profilerHeader.PointerCaptureLost+=(_,_)=>EndProfilerDrag(false);
        _profilerInfo=new TextBlock { Name="ProfilerSessionInfo",Foreground=Brushes.White,FontSize=12,TextWrapping=TextWrapping.Wrap };
        _profilerWindow=new Border { Name="FloatingProfiler",Background=Brush.Parse("#FF202020"),BorderBrush=Brush.Parse("#FF555555"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Padding=new Thickness(10),Child=new StackPanel { Spacing=6,Children={_profilerHeader,_profilerInfo,_gpuPanel!} },IsVisible=RuntimeSettings.Current.GpuProfilingEnabled };
        _profilerLayer=new Canvas { ClipToBounds=true,Children={_profilerWindow} };
        _profilerLayer.LayoutUpdated+=(_,_)=>PositionProfiler();
        return _profilerLayer;
    }

    private void RefreshProfilerInfo()
    {
        if(_profilerInfo is not null&&_profilerSessionInfo is {} info)
            _profilerInfo.Text=info.Describe(_profilerMetrics,_profilerDetection,_profilerDynamicStatistics);
    }

    private void PositionProfiler()
    {
        if(_positioningProfiler||_profilerLayer is not {} layer||_profilerWindow is not {} panel)return;
        _positioningProfiler=true;
        try
        {
            panel.IsVisible=RuntimeSettings.Current.GpuProfilingEnabled;
            if(!panel.IsVisible&&_profilerDragStart is not null)EndProfilerDrag(false);
            panel.Width=Math.Min(310,Math.Max(1,layer.Bounds.Width));
            panel.MaxHeight=Math.Max(1,Math.Min(500,layer.Bounds.Height));
            _profilerInfo?.Measure(new Size(Math.Max(1,panel.Width-22),double.PositiveInfinity));
            if(_gpuPanel?.Children.OfType<ScrollViewer>().SingleOrDefault() is {} scroll)
                scroll.MaxHeight=Math.Max(1,panel.MaxHeight-116-(_profilerInfo?.DesiredSize.Height??0));
            if(_profilerDragStart is null&&panel.Bounds.Width>0&&panel.Bounds.Height>0)
            {
                var point=RuntimeSettings.Current.ProfilerPlacement.Resolve(layer.Bounds.Size,panel.Bounds.Size);
                Canvas.SetLeft(panel,point.X);Canvas.SetTop(panel,point.Y);
            }
        }
        finally { _positioningProfiler=false; }
    }

    private void ProfilerPressed(object? sender,PointerPressedEventArgs args)
    {
        if(args.Handled||args.Properties.PointerUpdateKind!=PointerUpdateKind.LeftButtonPressed||args.Source is not Visual source||_profilerHeader is null||_profilerLayer is null||_profilerWindow is null)return;
        for(Visual? item=source;item is not null&&!ReferenceEquals(item,_profilerHeader);item=item.GetVisualParent())if(item is Button)return;
        _profilerDragStart=args.GetPosition(_profilerLayer);_profilerDragOrigin=new(Canvas.GetLeft(_profilerWindow),Canvas.GetTop(_profilerWindow));
        _profilerPointer=args.Pointer;args.Pointer.Capture(_profilerHeader);args.Handled=true;ConfigureMacInput();
    }
    private void ProfilerMoved(object? sender,PointerEventArgs args)
    {
        if(_profilerDragStart is not {} start||_profilerLayer is null||_profilerWindow is null||!ReferenceEquals(args.Pointer,_profilerPointer))return;
        var delta=args.GetPosition(_profilerLayer)-start;
        Canvas.SetLeft(_profilerWindow,Math.Clamp(_profilerDragOrigin.X+delta.X,0,Math.Max(0,_profilerLayer.Bounds.Width-_profilerWindow.Bounds.Width)));
        Canvas.SetTop(_profilerWindow,Math.Clamp(_profilerDragOrigin.Y+delta.Y,0,Math.Max(0,_profilerLayer.Bounds.Height-_profilerWindow.Bounds.Height)));
        args.Handled=true;ConfigureMacInput();
    }
    private void ProfilerReleased(object? sender,PointerReleasedEventArgs args)
    {
        if(_profilerDragStart is null||args.InitialPressMouseButton!=MouseButton.Left||!ReferenceEquals(args.Pointer,_profilerPointer))return;
        EndProfilerDrag(true);args.Handled=true;
    }
    private void EndProfilerDrag(bool save)
    {
        if(_profilerDragStart is null)return;
        _profilerDragStart=null;var pointer=_profilerPointer;_profilerPointer=null;pointer?.Capture(null);
        if(save&&_profilerLayer is {} layer&&_profilerWindow is {} panel)
        {
            var settings=RuntimeSettings.Current;var previous=settings.ProfilerPlacement;
            try
            {
                settings.ProfilerPlacement=OverlayPlacement.FromPosition(new(Canvas.GetLeft(panel),Canvas.GetTop(panel)),layer.Bounds.Size,panel.Bounds.Size);
                settings.Save();
            }
            catch(Exception error)
            {
                settings.ProfilerPlacement=previous;
                if(_message is not null) { _message.Text=error.Message;_message.IsVisible=true; }
            }
        }
        PositionProfiler();ConfigureMacInput();
    }
}
