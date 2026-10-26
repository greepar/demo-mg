using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Magpie.Desktop.Core;

// The capture and Vulkan work runs off the UI thread. Only one frame can await presentation.
internal sealed partial class ScalingSession
{
    private readonly RuntimeSettings _settings;
    public ScalingSession(RuntimeSettings? settings=null,Func<CancellationToken,Task<IScreenAwakeLease>>? powerFactory=null)
    {
        _settings=settings ?? RuntimeSettings.Current;_awake=new(powerFactory??ScreenAwake.AcquireAsync);_awake.Changed+=ReportAwake;
    }
    private readonly ScreenAwakeController _awake;
    private Task _powerUpdate=Task.CompletedTask;
    private CancellationTokenSource? _powerUpdateStop;
    private bool? _powerDesired;
    private TextBlock? _powerStatus;
    private Func<CancellationToken,Task>? _previewLoop;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _captureLock = new();
    private CaptureHandle? _capture;
    private Window? _output;
    private Flyout? _graphParameterFlyout;
    private WriteableBitmap? _bitmap;
    private long _target = ((long)1280 << 32) | 720;
    private int _windowed=1;
    private uint _sourceWidth,_sourceHeight;
    private bool _initialSizingApplied;
    private bool _started;
    private bool _finished;
    private TextBlock? _statistics;
    private Border? _toolbar;
    private WrapPanel? _toolbarActions;
    private TextBlock? _gpuDetails;
    private StackPanel? _gpuPanel;
    private Controls.GpuTimingView? _gpuTimingView;
    private Button? _gpuSwitchView;
    private CheckBox? _gpuProfilingToggle;
    private bool _syncingGpuProfiling;
    private GpuProfileSnapshot _gpuSample=GpuProfileSnapshot.Off;
    private readonly GpuTimingAverages _gpuAverages=new();
    private bool _gpuPassView,_gpuViewDeveloper;
    private Image? _image;
    private TextBlock? _message;
    private int _screenshotPending;
    internal void RequestScreenshot() { if(!_stop.IsCancellationRequested)Interlocked.Exchange(ref _screenshotPending,1); }
    public event Action<string?>? Finished;
    public event Action<string>? Status;

    internal Func<bool> GlobalScreenshotShortcutActive { get; set; }=()=>false;
    internal Func<bool> GlobalToolbarShortcutActive { get; set; }=()=>false;
    internal void ToggleToolbar()=>CycleToolbarState();

    public void Stop()
    {
        if (_finished) return;
        _stop.Cancel();
        lock (_captureLock)
            if (_capture is { IsClosed: false, IsInvalid: false }) NativeCore.CaptureCancel(_capture);
    }

    public Task RunAsync(Window owner, bool fullscreen, int delaySeconds)
    {
        if (_started) throw new InvalidOperationException("Scaling session is already running.");
        _started = true;
        return Task.Run(async () =>
        {
            string? failure = null;
            CaptureHandle? capture = null;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), _stop.Token);
                _settings.EnsureSelectedModeAvailable();
                _=CaptureBackendPolicy.Resolve(_settings.CaptureBackend,CaptureBackendPolicy.Available);
                if(_previewLoop is {} preview)
                {
                    if(RuntimeSettings.PersistenceEnabled)throw new InvalidOperationException("Synthetic capture requires isolated preview mode.");
                    await preview(_stop.Token);return;
                }
                using var engine=NativeCore.CreateForSettings(_settings);
                if(engine.IsInvalid)throw NativeCore.Error();
                string deviceName=Marshal.PtrToStringUTF8(NativeCore.DeviceName(engine)) ?? "Vulkan";
                capture = NativeCore.CaptureCreate();
                if (capture.IsInvalid) throw NativeCore.Error();
                lock (_captureLock) _capture = capture;
                _stop.Token.ThrowIfCancellationRequested();
                Status?.Invoke(OperatingSystem.IsWindows()
                    ? "Capturing the foreground window with GDI. Select the source during the start delay."
                    : "Select a window or screen in the system sharing dialog.");
                if (NativeCore.CaptureStart(capture) == 0) throw NativeCore.Error();
                _stop.Token.ThrowIfCancellationRequested();
                long start=Stopwatch.GetTimestamp();
                var processor=new ScalingFrameProcessor(start);
                nint lastSource=0;uint sourceWidth=0,sourceHeight=0;
                CaptureSource frameSource=CaptureSource.Unknown;ulong frameSequence=0;
                bool receivedFrame=false,havePlan=false;
                long previousRevision=-1,previousTarget=-1;int previousWindowed=-1;
                (NativeEffectStep[] Chain,string Name,DuplicateFrameMode Detection,bool DynamicStatistics,double Minimum,double Maximum,bool Gpu,SourceRegion Crop) plan=([],"",DuplicateFrameMode.Dynamic,false,0,0,false,default);
                var pacer=new FramePacer();
                while(!_stop.IsCancellationRequested)
                {
                    await pacer.WaitAsync(_settings.EffectiveMaximumFrameRate,_stop.Token);
                    int result=NativeCore.CaptureFrame(capture,out var source,out var width,out var height);
                    if(result<0)throw NativeCore.Error();
                    if(result==1)
                    {
                        frameSource=ReadCapturedSource(capture);frameSequence=NativeCore.CaptureFrameSequence(capture);
                        if(!receivedFrame)
                        {
                            _stop.Token.ThrowIfCancellationRequested();
                            await Dispatcher.UIThread.InvokeAsync(()=> { if(!_stop.IsCancellationRequested) { OpenCapturedOutput(owner,fullscreen,width,height); } });
                            _stop.Token.ThrowIfCancellationRequested();processor=new ScalingFrameProcessor(Stopwatch.GetTimestamp());
                        }
                        lastSource=source;sourceWidth=width;sourceHeight=height;receivedFrame=true;pacer.MarkFrame();
                    }
                    if(!receivedFrame)
                    {
                        if(Stopwatch.GetElapsedTime(start)>TimeSpan.FromSeconds(30))throw new InvalidOperationException("The capture stream did not provide a frame within 30 seconds.");
                        await Task.Delay(10,_stop.Token);continue;
                    }
                    UpdateKeepAwake(RuntimeSettings.Current.KeepScreenOn);
                    long target=Interlocked.Read(ref _target),revision=RuntimeSettings.ChangeSequence;int currentWindowed=Volatile.Read(ref _windowed);
                    if(!havePlan || result==1 || target!=previousTarget || revision!=previousRevision || currentWindowed!=previousWindowed)
                    {
                        uint viewportWidth=(uint)(target>>32),viewportHeight=(uint)target;
                        plan=await Dispatcher.UIThread.InvokeAsync(()=>
                        {
                            var crop=PrepareCapturedSource(sourceWidth,sourceHeight);
                            long currentTarget=Interlocked.Read(ref _target);viewportWidth=(uint)(currentTarget>>32);viewportHeight=(uint)currentTarget;
                            var global=RuntimeSettings.Current;
                            return (PresentationPlan.ForWindow(_settings,crop.Width,crop.Height,viewportWidth,viewportHeight,currentWindowed!=0),_settings.SelectedModeName,global.DuplicateFrameDetection,global.StatisticsForDynamicDetection,global.MinimumFrameRate,_settings.EffectiveMaximumFrameRate,global.GpuProfilingEnabled,crop);
                        });
                        havePlan=true;previousTarget=target;previousRevision=revision;previousWindowed=currentWindowed;
                    }
                    await _stageGate.WaitAsync(_stop.Token);
                    try
                    {
                    long now=Stopwatch.GetTimestamp();
                    var previousGpu=processor.GpuProfile;
                    bool scaled=processor.Process(engine,lastSource,sourceWidth,sourceHeight,result==1,plan.Chain,plan.Detection,plan.DynamicStatistics,plan.Minimum,plan.Maximum,now,plan.Gpu,plan.Crop);
                    // Disabling profiling may not require rendering a new
                    // image. Still clear the UI accumulator before re-enabling.
                    if(!scaled&&!ReferenceEquals(previousGpu,processor.GpuProfile))
                        await Dispatcher.UIThread.InvokeAsync(()=>UpdateGpuProfile(processor.GpuProfile));
                    if(scaled)
                    {
                        pacer.MarkFrame(now);
                        long submitBegin=Stopwatch.GetTimestamp();
                        if(await Dispatcher.UIThread.InvokeAsync(()=>PresentProcessedFrame(processor,engine,plan.Chain,frameSource,frameSequence)))
                            processor.Performance.Submit(Stopwatch.GetElapsedTime(submitBegin).TotalMilliseconds);
                    }
                    // Duplicate pixels still belong to the newly delivered
                    // source. The cached output is identical, so commit its
                    // metadata without an unnecessary GPU/bitmap submission.
                    else if(result==1)await Dispatcher.UIThread.InvokeAsync(()=>CommitUnchangedFrameSource(frameSource,frameSequence));
                    if(Volatile.Read(ref _screenshotPending)!=0)await Dispatcher.UIThread.InvokeAsync(DrainScreenshot);
                    if(processor.Performance.TrySnapshot(Stopwatch.GetTimestamp(),out var counters))
                    {
                        var output=plan.Chain[^1];
                        string title=$"Magpie · {plan.Name} · {plan.Crop.Width}×{plan.Crop.Height} → {output.Width}×{output.Height} · Scaled {counters.ScaledFps:F1} FPS · {deviceName} · Esc to stop";
                        string details=FramePerformance.Describe(counters,plan.Detection,plan.DynamicStatistics,plan.Gpu);
                        await Dispatcher.UIThread.InvokeAsync(()=> { if(_output is not null)_output.Title=title;if(_statistics is not null)_statistics.Text=details;UpdateGpuProfile(processor.GpuProfile); });
                    }
                    if(!scaled)await Task.Delay(processor.IdleDelayMilliseconds(plan.Minimum,plan.Maximum,Stopwatch.GetTimestamp()),_stop.Token);
                    else await Task.Delay(1,_stop.Token);
                    }
                    finally { _stageGate.Release(); }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!_stop.IsCancellationRequested) failure = ex.Message; }
            finally
            {
                _powerUpdateStop?.Cancel();
                try { await _powerUpdate; }catch(Exception cleanup) { failure??=cleanup.Message; }
                for(int attempt=0;attempt<2;attempt++)
                    try { await _awake.DisposeAsync();break; }
                    catch(Exception cleanup)
                    {
                        Console.Error.WriteLine("Keep screen on cleanup: "+cleanup.Message);
                        if(attempt==1) { failure??=cleanup.Message;ScreenAwakeCleanup.Schedule(_awake); }
                    }
                _powerUpdateStop?.Dispose();
                lock (_captureLock) _capture = null;
                capture?.Dispose();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _stageEngine=null;_displayedStages=[];
                    _finished = true;
                    _output?.Close(); _output = null;
                    _bitmap?.Dispose(); _bitmap = null;
                    Finished?.Invoke(failure);
                });
                _stop.Dispose();
            }
        });
    }
    private static unsafe void Scale(EngineHandle engine, nint source, uint width, uint height, byte[] pixels, NativeEffectStep[] chain)
    {
        fixed (byte* destination = pixels)
        fixed (NativeEffectStep* steps = chain)
            if (NativeCore.ScaleChain(engine, source, width, height, destination, (ulong)pixels.Length, steps, (uint)chain.Length) == 0) throw NativeCore.Error();
    }
    private void OpenOutput(Window owner, bool fullscreen,uint sourceWidth=0,uint sourceHeight=0)
    {
        var image = new Image { Stretch = Stretch.Fill, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        _image = image;
        _statistics = new TextBlock { Text = "Waiting for source frame…", Foreground = Brushes.White, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,FontSize=12 };
        _message = new TextBlock { Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var screenshot = new Button { Content = Localization.Text("Screenshot"), Margin = new Thickness(8,0) };
        screenshot.Name="ScreenshotOutputButton";
        screenshot.Click += (_, _) => RequestScreenshot();
        ConfigureStageExport(screenshot);
        var stop = new Button { Name="StopScalingButton",Content=new Controls.SymbolIcon { Kind="Close",Width=18,Height=18 },Width=36,Height=36,Padding=new Thickness(8) };
        stop.Click += (_, _) => Stop();
        ConfigureToolbarClose(stop,fullscreen);
        var pin=CreateToolbarPin();
        var effect = new ComboBox { Name="OutputEffectSelector",ItemsSource=Enumerable.Range(0,ScalingModeInterchange.EffectPaths.Length).Select(i=>EffectStageSettings.Name((uint)i)).ToArray(),SelectedIndex=(int)_settings.Effect,Margin=new Thickness(8,0) };
        effect.SelectionChanged += (_, _) => { if(effect.SelectedIndex<0)return;_settings.SelectedModeId = null; _settings.Effect = (uint)effect.SelectedIndex; RuntimeSettings.Current.Save(); };

        var modeChoices = _settings.Modes.ToArray();
        var modes = new ComboBox { ItemsSource = new[] { new ComboBoxItem { Content="Default effect" } }.Concat(modeChoices.Select(m => new ComboBoxItem { Content=m.IsAvailable?m.Name:m.Name+" (unavailable)",IsEnabled=m.IsAvailable })).ToArray(), SelectedIndex = Array.FindIndex(modeChoices, m => m.Id == _settings.SelectedModeId) + 1, Margin = new Thickness(8,0) };
        modes.SelectionChanged += (_, _) => { if (modes.SelectedIndex < 0) return;if(modes.SelectedIndex>0 && !modeChoices[modes.SelectedIndex-1].IsAvailable) { modes.SelectedIndex=Array.FindIndex(modeChoices,m=>m.Id==_settings.SelectedModeId)+1;return; } _settings.SelectedModeId = modes.SelectedIndex == 0 ? null : modeChoices[modes.SelectedIndex-1].Id; RuntimeSettings.Current.Save(); };
        var parameters = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        foreach (var name in new[] { "B", "C" })
        {
            var number = new NumericUpDown { Minimum = 0, Maximum = 1, Increment = .01m, Width = 100, Value = (decimal)(name == "B" ? _settings.BicubicB : _settings.BicubicC) };
            number.ValueChanged += (_, _) =>
            {
                if (name == "B") _settings.BicubicB = (float)(number.Value ?? .33m);
                else _settings.BicubicC = (float)(number.Value ?? .33m);
                RuntimeSettings.Current.Save();
            };
            parameters.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            parameters.Children.Add(number);
        }
        parameters.IsVisible = _settings.Effect == 3;
        effect.SelectionChanged += (_, _) => parameters.IsVisible = effect.SelectedIndex == 3;
        var sharpness = new NumericUpDown { Minimum = 0, Maximum = 1, Increment = .01m, Width = 100, Value = (decimal)_settings.FsrSharpness };
        sharpness.ValueChanged += (_, _) => { _settings.FsrSharpness = (float)(sharpness.Value ?? .87m); RuntimeSettings.Current.Save(); };
        var fsrParameters = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, IsVisible = _settings.Effect is 5 or 6, Children = { new TextBlock { Text = "Sharpness", Foreground = Brushes.White, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, sharpness } };
        effect.SelectionChanged += (_, _) => fsrParameters.IsVisible = effect.SelectedIndex is 5 or 6;
        var ssimSharpness=new NumericUpDown { Name="SsimOversharpInput",Minimum=1,Maximum=3,Increment=.1m,Width=130,Value=(decimal)_settings.SsimOversharp };
        ssimSharpness.ValueChanged+=(_,_)=> { _settings.SsimOversharp=(float)(ssimSharpness.Value ?? 1);RuntimeSettings.Current.Save(); };
        var ssimParameters=new StackPanel { Orientation=Avalonia.Layout.Orientation.Horizontal,Spacing=8,IsVisible=_settings.Effect==14,Children={ new TextBlock { Text="Oversharp",Foreground=Brushes.White,VerticalAlignment=Avalonia.Layout.VerticalAlignment.Center },ssimSharpness } };
        effect.SelectionChanged+=(_,_)=>ssimParameters.IsVisible=effect.SelectedIndex==14;
        var graphParameters=new Button { Name="OutputGraphParameters",Content=Localization.Get("Desktop.Parameters"),IsVisible=_settings.SelectedModeId is null && GraphEffects.HasParameters(_settings.Effect) };
        graphParameters.Click+=(_,_)=>
        {
            uint id=_settings.Effect;
            var editor=Controls.GraphParameterEditor.Create(id,i=>GraphEffects.Get(_settings,id,i),(i,v)=>_settings.GraphParameters[GraphEffects.Key(id,GraphEffects.Definitions(id)[i])]=v,()=>RuntimeSettings.Current.Save());
            _graphParameterFlyout?.Hide();var flyout=_graphParameterFlyout=new Flyout { Content=editor };
            Controls.PopupAlignment.Track(graphParameters,flyout);flyout.ShowAt(graphParameters);
        };
        effect.SelectionChanged+=(_,_)=>graphParameters.IsVisible=_settings.SelectedModeId is null && GraphEffects.HasParameters(_settings.Effect);
        modes.SelectionChanged+=(_,_)=>graphParameters.IsVisible=_settings.SelectedModeId is null && GraphEffects.HasParameters(_settings.Effect);
        effect.MaxWidth=240;modes.MaxWidth=240;
        var profiler=_gpuProfilingToggle=new CheckBox { Name="GpuProfilingToggle",Content=Localization.Get("Overlay_Profiler"),IsChecked=RuntimeSettings.Current.GpuProfilingEnabled,Foreground=Brushes.White };
        _gpuDetails=new TextBlock { Foreground=Brushes.White,TextWrapping=TextWrapping.Wrap,FontSize=12 };
        var switchView=new Button { Name="GpuTimingView",IsVisible=false,Content=Localization.Get("Overlay_Profiler_Timings_SwitchToPasses") };
        _gpuSwitchView=switchView;_gpuTimingView=new Controls.GpuTimingView { IsVisible=false };
        switchView.Click+=(_,_)=> { if(RuntimeSettings.Current.DeveloperMode&&_gpuSample.Stages.Any(s=>s.Passes.Length>1))_gpuPassView=!_gpuPassView;RefreshGpuProfile(); };
        _gpuPanel=new StackPanel { Name="GpuTimingPanel",Spacing=4,IsVisible=profiler.IsChecked==true,Children={ switchView,new ScrollViewer { MaxHeight=200,Content=new StackPanel { Spacing=4,Children={ _gpuTimingView,_gpuDetails } } } } };
        profiler.IsCheckedChanged+=(_,_)=>
        {
            if(_syncingGpuProfiling)return;
            bool previous=RuntimeSettings.Current.GpuProfilingEnabled;
            try { RuntimeSettings.Current.GpuProfilingEnabled=profiler.IsChecked==true;RuntimeSettings.Current.Save();_gpuPanel.IsVisible=profiler.IsChecked==true;_gpuAverages.Reset();_gpuSample=GpuProfileSnapshot.Off;RefreshGpuProfile(); }
            catch(Exception error)
            {
                RuntimeSettings.Current.GpuProfilingEnabled=previous;
                _syncingGpuProfiling=true;try { profiler.IsChecked=previous; }finally { _syncingGpuProfiling=false; }
                if(_message is not null) { _message.Text=error.Message;_message.IsVisible=true; }
            }
        };
        var row = new WrapPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { pin,modes, effect,graphParameters, screenshot, stop,profiler } };
        _toolbarActions=row;
        _powerStatus=new TextBlock { Name="OutputPowerStatus",IsVisible=false,Foreground=Brushes.Goldenrod,TextWrapping=TextWrapping.Wrap };
        _inputStatus=new TextBlock { Name="OutputInputStatus",IsVisible=false,Foreground=Brushes.Goldenrod,TextWrapping=TextWrapping.Wrap };
        _toolbar = new Border { Background = Brush.Parse("#D0202020"), Padding = new Thickness(12), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Child = new ScrollViewer { Content=new StackPanel { Spacing = 6, Children = { _statistics,row, parameters, fsrParameters,ssimParameters,_powerStatus,_inputStatus, _message } } } };
        var content = new Grid { ClipToBounds = true, Children = { image, _toolbar,CreateProfilerWindow() } };
        content.LayoutUpdated+=(_,_)=>ConfigureMacInput();
        InitialWindowSize? initial=null;double initialScale=owner.RenderScaling;
        if(!fullscreen && sourceWidth>0 && sourceHeight>0)
        {
            var screen=owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary ?? throw new InvalidOperationException("Display work area is unavailable.");
            initial=WindowSizing.Initial(InitialSizingSettings(),sourceWidth,sourceHeight,(uint)screen.WorkingArea.Width,(uint)screen.WorkingArea.Height,initialScale);
        }
        _output = new Window
        {
            WindowState=fullscreen?WindowState.FullScreen:WindowState.Normal,
            Title = $"Magpie · {_settings.SelectedModeName} · Esc to stop", Width = initial is {} size ? size.Width/initialScale : 1280, Height = initial is {} initialSize ? initialSize.Height/initialScale : 720,
            MinWidth = initial is {} minimum ? minimum.MinimumWidth/initialScale : 320, MinHeight = initial is {} minimumSize ? minimumSize.MinimumHeight/initialScale : 200, Background = Brushes.Black,
            Content = content, WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        _output.Deactivated+=(_,_)=>EndProfilerDrag(false);
        _output.Closed+=(_,_)=>EndProfilerDrag(false);
        _initialSizingApplied=initial.HasValue;
        _output.SizeChanged += (_, _) => UpdateSize();
        _output.ScalingChanged += (_,_)=>UpdateSize();
        _output.PropertyChanged+=(_,args)=>
        {
            if(args.Property!=Window.WindowStateProperty)return;
            UpdateSize();
            if(_output?.WindowState==WindowState.Normal && !_initialSizingApplied && _sourceWidth>0)
                Dispatcher.UIThread.Post(()=>
                {
                    if(_output is null || _output.WindowState!=WindowState.Normal || _initialSizingApplied)return;
                    try { ApplyInitialWindowSize(owner); }
                    catch(Exception error)
                    {
                        Status?.Invoke("Window sizing failed: "+error.Message);
                        if(_message is not null) { _message.Text=error.Message;_message.IsVisible=true; }
                    }
                });
        };
        _output.KeyDown += (_, args) => HandleOutputKey(args);
        _output.Closing += (_, _) => { _inputPoll?.Dispose();_inputPoll=null;Stop(); };
        InitializeToolbar(fullscreen);
        SetSourceGeometry(sourceWidth,sourceHeight);
        if(fullscreen)_output.WindowState=WindowState.FullScreen;
        else if(sourceWidth>0 && sourceHeight>0)ApplyInitialWindowSize(owner);
        // The output must survive the main window being hidden to the tray.
        _output.Show();
        StartMacInputPolling();
        if(!fullscreen && sourceWidth>0 && sourceHeight>0)ApplyInitialWindowSize(owner,true);
        UpdateSize();
    }
    private bool PresentProcessedFrame(ScalingFrameProcessor processor,EngineHandle engine,NativeEffectStep[] plan,CaptureSource source,ulong sequence)
    {
        var output=plan[^1];
        if(!PresentCapturedFrame(processor.Pixels,output.Width,output.Height,source,sequence))return false;
        SetDisplayedStages(engine,plan);
        if(processor.Performance.TotalScaled==1&&_statistics is not null)_statistics.Text="Measuring frame rates…";
        UpdateGpuProfile(processor.GpuProfile);
        return true;
    }
    private void UpdateGpuProfile(GpuProfileSnapshot sample)
    {
        var averaged=_gpuAverages.Push(sample,Stopwatch.GetTimestamp());
        if(!ReferenceEquals(_gpuSample,averaged)||_gpuViewDeveloper!=RuntimeSettings.Current.DeveloperMode) { _gpuSample=averaged;RefreshGpuProfile(); }
        bool enabled=RuntimeSettings.Current.GpuProfilingEnabled;
        if(_gpuPanel is not null)_gpuPanel.IsVisible=enabled;
        PositionProfiler();
        if(_gpuProfilingToggle is {} toggle&&toggle.IsChecked!=enabled)
        {
            _syncingGpuProfiling=true;try { toggle.IsChecked=enabled; }finally { _syncingGpuProfiling=false; }
        }
    }
    private void RefreshGpuProfile()
    {
        bool developer=RuntimeSettings.Current.DeveloperMode;_gpuViewDeveloper=developer;
        bool canShowPasses=developer&&_gpuSample.Stages.Any(s=>s.Passes.Length>1);
        if(!canShowPasses)_gpuPassView=false;
        if(_gpuSwitchView is {} button) { button.IsVisible=canShowPasses;button.Content=Localization.Get(_gpuPassView?"Overlay_Profiler_Timings_SwitchToEffects":"Overlay_Profiler_Timings_SwitchToPasses"); }
        _gpuTimingView?.Update(_gpuSample,_gpuPassView,developer);
        if(_gpuDetails is not null) { _gpuDetails.Text=_gpuSample.Describe(_gpuPassView);_gpuDetails.IsVisible=!_gpuSample.Valid; }
        PositionProfiler();
    }
    private void UpdateSize()
    {
        if (_output is null) return;
        if(_toolbar is not null)_toolbar.MaxHeight=Math.Max(1,_output.ClientSize.Height);
        uint width = (uint)Math.Clamp(Math.Round(_output.ClientSize.Width * _output.RenderScaling,MidpointRounding.AwayFromZero), 1, uint.MaxValue);
        uint height = (uint)Math.Clamp(Math.Round(_output.ClientSize.Height * _output.RenderScaling,MidpointRounding.AwayFromZero), 1, uint.MaxValue);
        Interlocked.Exchange(ref _target, ((long)width << 32) | height);
        Volatile.Write(ref _windowed,_output.WindowState==WindowState.FullScreen?0:1);
        PositionImage();
    }
    private void PositionImage()
    {
        if(_output is null || _image is null || _bitmap is null)return;
        long target=Interlocked.Read(ref _target);
        uint clientWidth=(uint)(target>>32),clientHeight=(uint)target;
        var viewport=(Width:clientWidth,Height:clientHeight);
        if(_sourceWidth>0 && _sourceHeight>0 && clientWidth<=16384 && clientHeight<=16384 && (ulong)clientWidth*clientHeight<=16777216)
            viewport=PresentationPlan.Viewport(_sourceWidth,_sourceHeight,clientWidth,clientHeight,Volatile.Read(ref _windowed)!=0);
        var origin=PresentationPlan.Center(clientWidth,clientHeight,viewport.Width,viewport.Height);
        var offset=PresentationPlan.Destination(viewport.Width,viewport.Height,(uint)_bitmap.PixelSize.Width,(uint)_bitmap.PixelSize.Height,_settings.OutputAlignment);
        _image.Margin=new Thickness((origin.X+offset.X)/_output.RenderScaling,(origin.Y+offset.Y)/_output.RenderScaling,0,0);
        _image.Width=_bitmap.PixelSize.Width/_output.RenderScaling;_image.Height=_bitmap.PixelSize.Height/_output.RenderScaling;
    }
    private unsafe bool Present(byte[] pixels, uint width, uint height)
    {
        if (_output is null || _image is not { } image || _stop.IsCancellationRequested) return false;
        var size = new PixelSize((int)width, (int)height);
        if (_bitmap?.PixelSize != size)
        {
            image.Source = null;
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(size, new Vector(96,96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
            image.Source = _bitmap;
        }
        using (var destination = _bitmap.Lock())
        fixed (byte* source = pixels)
            for (int y = 0; y < (int)height; y++)
                Buffer.MemoryCopy(source + y * (int)width * 4, (byte*)destination.Address + y * destination.RowBytes, destination.RowBytes, width * 4);
        PositionImage();
        image.InvalidateVisual();
        return true;
    }
    private void HandleOutputKey(KeyEventArgs args)
    {
        if(args.Key==Key.Escape) { Stop();args.Handled=true;return; }
        var screenshot=RuntimeSettings.Current.ScreenshotShortcut;
        bool screenshotBinding=args.Key==Enum.Parse<Key>(screenshot.Key)&&args.KeyModifiers==screenshot.AvaloniaModifiers;
        if(args.Key==Key.F12||screenshotBinding)
        {
            if(args.Key==Key.F12||!GlobalScreenshotShortcutActive())RequestScreenshot();
            args.Handled=true;return;
        }
        if(!GlobalToolbarShortcutActive()&&args.Key==Enum.Parse<Key>(RuntimeSettings.Current.ToolbarShortcut.Key)&&args.KeyModifiers==RuntimeSettings.Current.ToolbarShortcut.AvaloniaModifiers)
        { ToggleToolbar();args.Handled=true; }
    }
    private void DrainScreenshot()
    {
        if(Interlocked.Exchange(ref _screenshotPending,0)!=0&&!_stop.IsCancellationRequested)SaveScreenshot();
    }
    internal async Task VerifyOutputAsync(Window owner)
    {
        string directory = Path.Combine(Path.GetTempPath(), "magpie-output-test-" + Guid.NewGuid().ToString("N"));
        var settings=_settings;
        string previousDirectory=settings.ScreenshotDirectory,previousTemplate=settings.ScreenshotTemplate;
        try
        {
            settings.ScreenshotDirectory=directory;
            settings.ScreenshotTemplate="测试_\\%_%Y%";
            OpenOutput(owner, false);
            await Task.Delay(100);
            byte[] pixels = new byte[31*19*4];
            for(int i=0;i<pixels.Length;i+=4) { pixels[i]=(byte)(i%251); pixels[i+1]=45; pixels[i+2]=201; pixels[i+3]=255; }
            Present(pixels,31,19);
            string first = SaveScreenshot() ?? throw new InvalidOperationException("Output screenshot could not be saved.");
            string second = SaveScreenshot() ?? throw new InvalidOperationException("Second screenshot could not be saved.");
            if(first==second || Directory.GetFiles(directory,"*.png").Length != 2) throw new InvalidOperationException("Screenshot overwrite protection failed.");
            if(Path.GetDirectoryName(first)!=directory || Path.GetFileName(first)!=$"测试_%_{DateTime.Now:yyyy}.png")
                throw new InvalidOperationException("Configured screenshot directory/template was ignored.");
            VerifyPng(first,pixels,new PixelSize(31,19));
            // Exercise framebuffer recreation at a different size.
            byte[] resized = new byte[11*7*4];
            for(int i=0;i<resized.Length;i+=4) { resized[i]=42; resized[i+1]=93; resized[i+2]=157; resized[i+3]=255; }
            Present(resized,11,7);
            VerifyPng(SaveScreenshot() ?? throw new InvalidOperationException("Resized screenshot failed."),resized,new PixelSize(11,7));
            // A path occupied by a file must report failure without damaging that file.
            string blocked=Path.Combine(directory,"blocked");File.WriteAllText(blocked,"keep");
            settings.ScreenshotDirectory=blocked;
            if(SaveScreenshot() is not null || File.ReadAllText(blocked)!="keep")throw new InvalidOperationException("Screenshot directory failure was not handled.");
            settings.ScreenshotDirectory=directory;
            // The real Stop button must cancel the running token without throwing on repeats.
            var stop = _output!.GetVisualDescendants().OfType<Button>().First(b => b.Name == "StopScalingButton");
            stop.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Stop();
            if(!_stop.IsCancellationRequested) throw new InvalidOperationException("Output Stop did not cancel session.");
            Console.WriteLine("OUTPUT PASS: frame presentation/resize, lossless PNG roundtrip, configured directory/template, unique screenshots, failed directory, Stop/repeated Stop.");
        }
        finally
        {
            settings.ScreenshotDirectory=previousDirectory;settings.ScreenshotTemplate=previousTemplate;
            _graphParameterFlyout?.Hide();_graphParameterFlyout=null;
            _output?.Close(); _output=null; _bitmap?.Dispose(); _bitmap=null;
            _finished=true; _stop.Dispose();
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }
    private static unsafe void VerifyPng(string path, byte[] expected, PixelSize size)
    {
        using var decoded = new Bitmap(path);
        if(decoded.PixelSize != size) throw new InvalidOperationException("PNG dimensions differ from output frame.");
        using var rgba = new WriteableBitmap(size,new Vector(96,96),PixelFormat.Rgba8888,AlphaFormat.Opaque);
        using var buffer = rgba.Lock();
        decoded.CopyPixels(buffer);
        for(int y=0;y<size.Height;y++) for(int x=0;x<size.Width*4;x++)
            if(*((byte*)buffer.Address+y*buffer.RowBytes+x) != expected[y*size.Width*4+x]) throw new InvalidOperationException("Screenshot PNG pixel values differ from presented frame.");
    }
    private static string EffectName(uint effect) => EffectStageSettings.Name(effect);
    private string? SaveScreenshot()
    {
        if (_bitmap is null) return null;
        return SaveScreenshotBitmap(_bitmap,ReadScreenshotSource());
    }
    private string? SaveScreenshotBitmap(Bitmap bitmap,CaptureSource source,string? suffix=null)
    {
        try
        {
            var settings=_settings;
            string directory = settings.ResolveScreenshotDirectory();
            Directory.CreateDirectory(directory);
            string name = ScreenshotNames.Format(settings.ResolveScreenshotTemplate(),source.ScreenshotTitle,source.ScreenshotProcess,DateTime.Now);
            if(suffix is not null)name=ScreenshotNames.WithSuffix(name,suffix);
            string path = Path.Combine(directory, name);
            // CreateNew prevents overwriting a screenshot made within the same millisecond.
            for (int index = 1; File.Exists(path); index++) path = Path.Combine(directory, Path.GetFileNameWithoutExtension(name) + "_" + index + ".png");
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            bitmap.Save(file, PngBitmapEncoderOptions.Default);
            if (_message is not null) { _message.Text = "Saved " + path; _message.IsVisible = true; }
            return path;
        }
        catch (Exception error)
        {
            if (_message is not null) { _message.Text = "Screenshot failed: " + error.Message; _message.IsVisible = true; }
            return null;
        }
    }

}
