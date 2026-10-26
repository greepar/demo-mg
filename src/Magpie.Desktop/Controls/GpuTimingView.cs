using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Media;
using Magpie.Desktop.Core;

namespace Magpie.Desktop.Controls;

internal sealed class GpuTimingView : StackPanel
{
    // Original OverlayHelper::TIMELINE_COLORS. Keep identities stable between
    // effect/pass views; colors are cosmetic and independent of GPU samples.
    private static readonly IBrush[] Colors=[Brush.Parse("#E53935"),Brush.Parse("#9C27B0"),Brush.Parse("#3F51B5"),Brush.Parse("#1E88E5"),Brush.Parse("#00897B"),Brush.Parse("#795548"),Brush.Parse("#757575")];
    private readonly Dictionary<GpuTimingKey,Border> _rows=[];
    private readonly Dictionary<GpuTimingKey,(Button Button,TimelineLabel Label,GpuTimingItem Item)> _segments=[];
    internal GpuTimingPresentation Presentation { get; private set; }=new([],[],0,false,false);
    internal GpuTimingKey? Highlighted { get; private set; }
    internal Grid Timeline { get; }=new() { Height=26,ClipToBounds=true,Name="GpuTimingTimeline" };
    internal IReadOnlyDictionary<GpuTimingKey,Border> TimingRows=>_rows;
    internal Button SegmentButton(GpuTimingKey key)=>_segments[key].Button;

    internal GpuTimingView() { Name="GpuTimingViewContent";Spacing=4; }

    internal void Update(GpuProfileSnapshot sample,bool passes,bool developer)
    {
        var old=Highlighted;
        GpuTimingKey? focused=_segments.Where(pair=>pair.Value.Button.IsFocused).Select(pair=>(GpuTimingKey?)pair.Key).FirstOrDefault();
        Presentation=GpuTimingPresentation.Build(sample,passes,developer);
        foreach(var segment in _segments.Values)ToolTip.SetIsOpen(segment.Button,false);
        Children.Clear();_rows.Clear();_segments.Clear();Timeline.Children.Clear();Timeline.ColumnDefinitions.Clear();
        IsVisible=sample.Valid;
        if(!sample.Valid) { Highlighted=null;return; }
        Timeline.IsVisible=Presentation.ShowTimeline;
        if(Presentation.ShowTimeline)
        {
            if(Presentation.Segments.Length==0)Timeline.Children.Add(new Border { Background=Brush.Parse("#40757575") });
            for(int i=0;i<Presentation.Segments.Length;i++)
            {
                var item=Presentation.Segments[i];
                Timeline.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(item.Milliseconds,GridUnitType.Star)));
                var label=new TimelineLabel { Text=item.Name };
                var button=new Button { CornerRadius=new CornerRadius(0),Padding=new Thickness(0),Margin=new Thickness(0),MinWidth=0,MinHeight=0,HorizontalAlignment=Avalonia.Layout.HorizontalAlignment.Stretch,Height=26,Background=Colors[item.Color],Foreground=Brushes.White,BorderBrush=Brushes.White,BorderThickness=new Thickness(0),ClipToBounds=true,HorizontalContentAlignment=Avalonia.Layout.HorizontalAlignment.Stretch,VerticalContentAlignment=Avalonia.Layout.VerticalAlignment.Stretch,Tag=item.Key };
                // Keep original segment colors under hover/press as well;
                // the general Fluent button theme substitutes its own fill.
                button.Template=new FuncControlTemplate<Button>((control,_)=>
                {
                    var host=new Border { Child=label,Background=Colors[item.Color],BorderBrush=Brushes.White };
                    host.Bind(Border.BorderThicknessProperty,control.GetObservable(Button.BorderThicknessProperty));
                    return host;
                });
                Grid.SetColumn(button,i);ToolTip.SetTip(button,new TextBlock { Text=Presentation.Tooltip(item),TextWrapping=TextWrapping.Wrap,MaxWidth=260 });
                PopupAlignment.TrackToolTip(button);
                Avalonia.Automation.AutomationProperties.SetName(button,Presentation.Tooltip(item));
                button.PointerEntered+=(_,_)=>Highlight(item.Key);
                button.PointerExited+=(_,_)=>Highlight(null);
                button.GotFocus+=(_,e)=> { if(e.NavigationMethod!=NavigationMethod.Pointer)Highlight(item.Key); };
                button.LostFocus+=(_,_)=>Highlight(null);
                button.Click+=(_,_)=> { Highlight(item.Key);ToolTip.SetIsOpen(button,true); };
                _segments.Add(item.Key,(button,label,item));Timeline.Children.Add(button);
            }
            Children.Add(Timeline);
        }
        foreach(var item in Presentation.Rows)
        {
            var grid=new Grid { ColumnDefinitions=new ColumnDefinitions("Auto,*,Auto"),ColumnSpacing=5 };
            bool hasSegment=_segments.ContainsKey(item.Key);
            if(hasSegment)grid.Children.Add(new Border { Width=7,Height=7,Background=Colors[item.Color],VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center });
            var label=new TextBlock { Text=item.Name,Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,FontSize=12,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center };
            var duration=new TextBlock { Text=$"{item.Milliseconds:F3} ms",Foreground=Brushes.White,Opacity=Presentation.ShowPasses&&item.Key.Pass<0&&sample.Stages[item.Key.Stage].Passes.Length>1?.5:1,FontSize=12,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center };
            Grid.SetColumn(label,1);Grid.SetColumn(duration,2);grid.Children.Add(label);grid.Children.Add(duration);
            var row=new Border { Child=grid,Padding=new Thickness(item.Indented?16:0,3,2,3),Tag=item.Key };
            row.PointerEntered+=(_,_)=>Highlight(hasSegment?item.Key:null);row.PointerExited+=(_,_)=>Highlight(null);
            _rows.Add(item.Key,row);Children.Add(row);
        }
        if(sample.Stages.Length>1)
        {
            Children.Add(new Border { Height=1,Background=Brush.Parse("#60FFFFFF") });
            Children.Add(new TextBlock { Foreground=Brushes.White,FontSize=12,Text=$"{Localization.Get("Overlay_Profiler_Timings_Total")}: {Presentation.Total:F3} ms" });
        }
        Children.Add(new TextBlock { Foreground=Brushes.White,Opacity=.65,FontSize=11,TextWrapping=TextWrapping.Wrap,Text=$"Final barrier/query tail: {Math.Max(0,sample.TotalMilliseconds-Presentation.Total):F3} ms\nApproximate GPU intervals include transfers, barriers and query overhead." });
        Highlight(old is {} key&&_segments.ContainsKey(key)?key:null);
        if(focused is {} focusKey&&_segments.TryGetValue(focusKey,out var replacement))replacement.Button.Focus(NavigationMethod.Tab);
    }

    private void Highlight(GpuTimingKey? key)
    {
        Highlighted=key;
        foreach(var pair in _rows)pair.Value.Background=pair.Key==key?Brush.Parse("#30FFFFFF"):Brushes.Transparent;
        foreach(var pair in _segments)
        {
            bool selected=pair.Key==key;
            pair.Value.Button.BorderThickness=new Thickness(selected?1:0);
            pair.Value.Label.Text=selected?$"{Math.Round(Presentation.Percent(pair.Value.Item),MidpointRounding.AwayFromZero):F0}%":pair.Value.Item.Name;
        }
    }

    private sealed class TimelineLabel : Control
    {
        private string _text="";
        internal string Text { get=>_text;set { _text=value;InvalidateVisual(); } }
        public override void Render(DrawingContext context)
        {
            base.Render(context);
            var text=new FormattedText(Text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("$Default"),11,Brushes.White);
            // Match original: omit labels that do not fit, without assigning
            // minimum segment widths that distort the measured proportions.
            if(text.Width+4<Bounds.Width)context.DrawText(text,new Point((Bounds.Width-text.Width)/2,(Bounds.Height-text.Height)/2));
        }
    }
}
