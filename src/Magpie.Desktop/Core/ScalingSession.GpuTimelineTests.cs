using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    internal async Task VerifyGpuTimelineAsync(MainWindow owner,bool compact)
    {
        if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("GPU timeline fixtures require preview mode.");
        var global=RuntimeSettings.Current;bool previousGpu=global.GpuProfilingEnabled,previousDeveloper=global.DeveloperMode;
        using var engine=NativeCore.Create();if(engine.IsInvalid)throw NativeCore.Error();
        try
        {
            global.GpuProfilingEnabled=true;global.DeveloperMode=false;
            _settings.Effect=11;_settings.SelectedModeId=null;
            OpenOutput(owner,false);_output!.Width=compact?360:640;_output.Height=compact?480:600;
            SetToolbarState(ToolbarState.AlwaysShow);await Task.Delay(120);
            byte[] input=Enumerable.Range(0,24).Select(i=>(byte)(i%4==3?255:i*7)).ToArray();
            NativeEffectStep[] plan=[new(){Effect=11,Width=6,Height=4},new(){Effect=0,Width=9,Height=6}];
            var processor=new ScalingFrameProcessor(0);
            if(!ReplayFrame(processor,engine,input,plan,true,0,gpu:true)||!processor.GpuProfile.Valid||!PresentProcessedFrame(processor,engine,plan,CaptureSource.Unknown,1))throw new InvalidOperationException("Timeline fixture needs an actual valid Vulkan timestamp sample.");
            await Task.Delay(100);
            var view=_gpuTimingView!;var toggle=_gpuSwitchView!;
            if(toggle.IsVisible||view.Presentation.ShowPasses||view.Presentation.Rows.Length!=2)throw new InvalidOperationException("Normal mode exposed developer pass rows.");
            CheckWidths();
            var effectKey=new GpuTimingKey(0);Enter(view.TimingRows[effectKey]);
            if(view.Highlighted!=effectKey||view.SegmentButton(effectKey).BorderThickness.Left!=1)throw new InvalidOperationException("Timing row hover did not highlight its timeline segment.");
            Exit(view.TimingRows[effectKey]);if(view.Highlighted is not null)throw new InvalidOperationException("Row exit retained a selection.");
            global.DeveloperMode=true;UpdateGpuProfile(processor.GpuProfile);
            if(!toggle.IsVisible)throw new InvalidOperationException("Multi-pass developer view has no switch.");
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
            if(!_gpuPassView||!view.Presentation.ShowPasses||view.Presentation.Rows.Length!=8)throw new InvalidOperationException("Pass switch did not create all original pass rows.");
            CheckWidths();
            var pass=view.Presentation.Segments.First(item=>item.Key.Pass>=0);var segment=view.SegmentButton(pass.Key);
            Enter(segment);
            if(view.Highlighted!=pass.Key||view.TimingRows[pass.Key].Background is null)throw new InvalidOperationException("Timeline hover did not highlight its timing row.");
            string expected=view.Presentation.Tooltip(pass);
            if((ToolTip.GetTip(segment) as TextBlock)?.Text!=expected||!expected.Contains(PassExportCatalog.All[11][pass.Key.Pass].Name))throw new InvalidOperationException("Actual timeline tooltip lacks original pass name or measured values.");
            Exit(segment);
            _output.FocusManager!.Focus(null);segment.Focus(NavigationMethod.Tab);
            if(view.Highlighted!=pass.Key||!_toolbar!.IsKeyboardFocusWithin)throw new InvalidOperationException("Keyboard focus did not expose timeline selection.");
            _toolbarPointer=null;UpdateToolbarVisibility();if(_toolbar.Opacity!=1)throw new InvalidOperationException("Keyboard timing interaction hid the toolbar.");
            RefreshGpuProfile();await Task.Delay(60);
            segment=view.SegmentButton(pass.Key);
            if(!segment.IsFocused||view.Highlighted!=pass.Key)throw new InvalidOperationException("Timing refresh lost keyboard interaction identity.");
            _output.FocusManager.Focus(null);
            if(IsToolbarCaption(segment,_toolbar)||IsToolbarCaption(view.TimingRows[pass.Key],_toolbar))throw new InvalidOperationException("Timing content can accidentally initiate native move.");
            // Developer mode can change while the session and sample remain.
            global.DeveloperMode=false;UpdateGpuProfile(processor.GpuProfile);
            if(toggle.IsVisible||_gpuPassView||view.Presentation.ShowPasses||view.Highlighted is not null)throw new InvalidOperationException("Disabling developer mode retained pass controls or stale hover.");
            global.DeveloperMode=true;UpdateGpuProfile(processor.GpuProfile);toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
            var inspect=view.Presentation.Segments.First(item=>item.Key.Pass>=0);Enter(view.TimingRows[inspect.Key]);
            view.Timeline.BringIntoView();await Task.Delay(120);CheckWidths();
            if(!_toolbar!.IsVisible||_toolbar.Opacity!=1||view.Bounds.Width>_output.ClientSize.Width||_toolbar.Bounds.Height>_output.ClientSize.Height+1)throw new InvalidOperationException("Timing panel exceeded the output client bounds.");
            var args=Environment.GetCommandLineArgs();int capture=Array.IndexOf(args,"--capture");
            if(capture>=0&&capture+1<args.Length)
            {
                var selected=view.SegmentButton(inspect.Key);
                selected.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(100);
                var tip=(TextBlock)ToolTip.GetTip(selected)!;
                if(TopLevel.GetTopLevel(tip) is not Avalonia.Controls.Primitives.PopupRoot)throw new InvalidOperationException("Timeline click did not open a real native tooltip.");
                await UiPreview.WaitForPopupFramesAsync([tip]);
                await Task.Delay(250); // finish the tooltip's normal fade-in before visual review
                UiPreview.Save((Control)_output.Content!,_output,[tip],Path.ChangeExtension(args[capture+1],"output.png"));
                ToolTip.SetIsOpen(selected,false);await Task.Delay(80);
            }
            var profiling=_output.GetVisualDescendants().OfType<CheckBox>().Single(c=>c.Name=="GpuProfilingToggle");
            global.GpuProfilingEnabled=false;UpdateGpuProfile(GpuProfileSnapshot.Off);
            if(profiling.IsChecked!=false||_gpuPanel!.IsVisible||view.IsVisible)throw new InvalidOperationException("External profiler preference did not synchronize the running output toggle.");
            global.GpuProfilingEnabled=true;UpdateGpuProfile(processor.GpuProfile);
            if(profiling.IsChecked!=true||!_gpuPanel.IsVisible||!view.IsVisible)throw new InvalidOperationException("External profiler enable did not restore the running toggle/sample.");
            profiling.IsChecked=false;
            if(view.IsVisible||_gpuPanel!.IsVisible||view.Presentation.Rows.Length!=0)throw new InvalidOperationException("Disabling profiler retained timing content.");
            Console.WriteLine("GPU TIMELINE UI PASS: actual CuNNy/nearest Vulkan timestamps, proportional native layout, actual effect/pass switch and mode guards, original tooltip text, row/segment routed hover, keyboard selection/refresh continuity, caption exclusion, compact scrolling and disable cleanup. Source pixels and pointer events are injected; no real capture or OS input delivery.");

            void CheckWidths()
            {
                var model=view.Presentation;double weights=model.Segments.Sum(s=>s.Milliseconds);
                if(view.Timeline.Bounds.Width<=0||view.Timeline.Children.Count!=model.Segments.Length)throw new InvalidOperationException("Timeline was not measured.");
                foreach(var item in model.Segments)
                {
                    var button=view.SegmentButton(item.Key);double expectedWidth=view.Timeline.Bounds.Width*item.Milliseconds/weights;
                    if(Math.Abs(button.Bounds.Width-expectedWidth)>1.1||button.Bounds.Right>view.Timeline.Bounds.Width+1)throw new InvalidOperationException($"Rendered timeline width differs: key={item.Key}, bounds={button.Bounds}, expected={expectedWidth}, bar={view.Timeline.Bounds}, weight={item.Milliseconds}/{weights}.");
                }
            }
            static void Enter(Control control)=>Route(control,InputElement.PointerEnteredEvent);
            static void Exit(Control control)=>Route(control,InputElement.PointerExitedEvent);
            static void Route(Control control,RoutedEvent<PointerEventArgs> routed)
            {
                var root=control.GetVisualAncestors().Last();var point=control.TranslatePoint(new Point(1,1),root)??throw new InvalidOperationException("Timeline control has no visual root.");
                control.RaiseEvent(new PointerEventArgs(routed,control,new Pointer(45,PointerType.Mouse,true),root,point,0,new PointerPointProperties(RawInputModifiers.None,PointerUpdateKind.Other),KeyModifiers.None));
            }
        }
        finally
        {
            global.GpuProfilingEnabled=previousGpu;global.DeveloperMode=previousDeveloper;
            _finished=true;_output?.Close();_output=null;_bitmap?.Dispose();_bitmap=null;_stageEngine=null;_stop.Dispose();
        }
    }
}
