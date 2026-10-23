using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
namespace Magpie.Desktop.Core;

internal sealed partial class ScalingSession
{
    private ToolbarState _toolbarState;
    private Point? _toolbarPointer;
    private bool _toolbarDragging;
    private DispatcherTimer? _toolbarTimer;
    private ToggleButton? _toolbarPin;
    private bool _syncingToolbarPin;
    private bool _toolbarKeyboardFocus;
    private ToggleButton CreateToolbarPin()
    {
        var pin=new ToggleButton { Name="PinToolbarButton",Content=new Controls.SymbolIcon { Kind="Pin",Width=18,Height=18 },Width=36,Height=36,Padding=new Thickness(8) };
        string label=Localization.Get("Overlay_Toolbar_Pin");
        Avalonia.Automation.AutomationProperties.SetName(pin,label);ToolTip.SetTip(pin,label);
        pin.IsCheckedChanged+=(_,_)=>
        {
            if(_syncingToolbarPin)return;
            SetToolbarState(pin.IsChecked==true?ToolbarState.AlwaysShow:ToolbarState.AutoHide);
        };
        return _toolbarPin=pin;
    }
    private void ConfigureToolbarClose(Button close,bool fullscreen)
    {
        string description=Localization.Get("Overlay_Toolbar_Close_Description");
        string label=Localization.Get("Overlay_Toolbar_Close");
        var shortcut=fullscreen?RuntimeSettings.Current.FullscreenShortcut:RuntimeSettings.Current.WindowedShortcut;
        Avalonia.Automation.AutomationProperties.SetName(close,label);
        Avalonia.Automation.AutomationProperties.SetHelpText(close,description);
        ToolTip.SetTip(close,$"{label} ({shortcut.Trigger})\n{description}");
        // Complete the right-click before hiding: the corresponding mouse-up
        // must remain local to the toolbar, including on the Cocoa input bridge.
        close.AddHandler(InputElement.PointerReleasedEvent,(_,e)=>
        {
            if(e.InitialPressMouseButton!=MouseButton.Right || !new Rect(close.Bounds.Size).Contains(e.GetPosition(close)))return;
            SetToolbarState(ToolbarState.Off);e.Handled=true;
        },Avalonia.Interactivity.RoutingStrategies.Bubble,handledEventsToo:true);
    }
    private void InitializeToolbar(bool fullscreen)
    {
        _toolbarState=ToolbarVisibility.Initial(_settings,!fullscreen);
        var toolbar=_toolbar!;
        toolbar.AddHandler(InputElement.GotFocusEvent,(_,e)=>_toolbarKeyboardFocus=e.NavigationMethod!=NavigationMethod.Pointer || e.Source is TextBox,Avalonia.Interactivity.RoutingStrategies.Bubble);
        _output!.AddHandler(InputElement.PointerMovedEvent,(_,e)=>_toolbarPointer=e.GetPosition(toolbar.Parent as Visual),Avalonia.Interactivity.RoutingStrategies.Bubble,handledEventsToo:true);
        _output.PointerExited+=(_,_)=>_toolbarPointer=null;
        _output.AddHandler(InputElement.PointerPressedEvent,(_,e)=>
        {
            _toolbarDragging=toolbar.Bounds.Contains(e.GetPosition(toolbar.Parent as Visual))&&toolbar.Opacity>0;
            TryMoveFromToolbar(e);
        },Avalonia.Interactivity.RoutingStrategies.Bubble,handledEventsToo:true);
        _output.AddHandler(InputElement.PointerReleasedEvent,(_,_)=>_toolbarDragging=false,Avalonia.Interactivity.RoutingStrategies.Bubble,handledEventsToo:true);
        _output.Deactivated+=(_,_)=> { _toolbarDragging=false;_toolbarPointer=null; };
        _output.Closed+=(_,_)=> { _toolbarTimer?.Stop();_toolbarTimer=null; };
        _toolbarTimer=new DispatcherTimer(TimeSpan.FromMilliseconds(40),DispatcherPriority.Background,(_,_)=>UpdateToolbarVisibility());
        _toolbarTimer.Start();UpdateToolbarVisibility();
    }
    private void UpdateToolbarVisibility()
    {
        if(_toolbar is null || _output is null)return;
        bool active=_stageMenuOpen || _toolbarDragging || (_toolbarKeyboardFocus && _toolbar.IsKeyboardFocusWithin) || _toolbar.GetVisualDescendants().OfType<Popup>().Any(p=>p.IsOpen);
        double alpha=ToolbarVisibility.Alpha(_toolbarState,_toolbar.Bounds,_toolbarPointer,active);
        bool inputRegionChanged=_toolbar.IsVisible!=(_toolbarState!=ToolbarState.Off) || _toolbar.IsHitTestVisible!=(alpha>0);
        _toolbar.IsVisible=_toolbarState!=ToolbarState.Off;
        _toolbar.Opacity=alpha;_toolbar.IsHitTestVisible=alpha>0;
        if(_toolbarPin is {} pin)
        {
            _syncingToolbarPin=true;
            try { pin.IsChecked=_toolbarState==ToolbarState.AlwaysShow; }
            finally { _syncingToolbarPin=false; }
        }
        if(inputRegionChanged)ConfigureMacInput();
    }
    private void SetToolbarState(ToolbarState state)
    {
        _toolbarState=state;UpdateToolbarVisibility();
        Status?.Invoke("Toolbar: "+_toolbarState);
    }
    private void CycleToolbarState()
    {
        if(_toolbar is null)return;
        SetToolbarState(ToolbarVisibility.Next(_toolbarState));
    }
}
