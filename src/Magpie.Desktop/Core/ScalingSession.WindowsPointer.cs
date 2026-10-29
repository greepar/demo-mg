using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
namespace Magpie.Desktop.Core;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeCursorShape {internal ulong Handle;internal uint Width,Height,HotX,HotY;}

internal sealed partial class ScalingSession
{
    [StructLayout(LayoutKind.Sequential)]private struct WindowsCursorInfo {public uint Size,Flags;public nint Cursor;public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]private struct WindowsGuiInfo
    {
        public uint Size,Flags;
        public nint Active,Focus,Capture,Menu,MoveSize,Caret;
        public NativePointerRect CaretRect;
    }
    [DllImport("user32",EntryPoint="GetCursorInfo")]private static extern bool ReadWindowsCursor(ref WindowsCursorInfo info);
    [DllImport("user32",EntryPoint="GetCursorPos")]private static extern bool ReadWindowsPointer(out PixelPoint point);
    [DllImport("user32",EntryPoint="SetCursorPos")]private static extern bool MoveWindowsPointer(int x,int y);
    [DllImport("user32",EntryPoint="GetClipCursor")]private static extern bool ReadWindowsClip(out NativePointerRect clip);
    [DllImport("user32",EntryPoint="ClipCursor")]private static extern bool SetWindowsClip(ref NativePointerRect clip);
    [DllImport("user32",EntryPoint="ClipCursor")]private static extern bool ClearWindowsClip(nint clip);
    [DllImport("user32",EntryPoint="GetGUIThreadInfo")]private static extern bool ReadWindowsGui(uint thread,ref WindowsGuiInfo info);
    [DllImport("user32",EntryPoint="GetAncestor")]private static extern nint WindowsPointerRoot(nint window,uint kind);
    [DllImport("user32",EntryPoint="WindowFromPoint")]private static extern nint WindowsPointerChild(PixelPoint point);
    [DllImport("user32",EntryPoint="PostMessageW")]private static extern bool HintWindowsCursor(nint window,uint message,nint w,nint l);
    [DllImport("user32",EntryPoint="MonitorFromPoint")]private static extern nint WindowsPointerMonitor(PixelPoint point,uint flags);
    [DllImport("user32",EntryPoint="SendMessageTimeoutW")]private static extern nint WindowsPointerHit(nint window,uint message,nint w,nint l,uint flags,uint timeout,out nint result);
    [DllImport("user32",EntryPoint="SetLayeredWindowAttributes",SetLastError=true)]private static extern bool SetWindowsPointerAlpha(nint window,uint color,byte alpha,uint flags);
    private bool _windowsPointerSuppressed,_windowsPointerCaptured,_windowsSystemCursorHidden,_windowsPointerLayered,_windowsPointerPassthrough;
    private WindowsPointerMapping _windowsPointerMapping;
    private Image? _windowsCursorOverlay;
    private WriteableBitmap? _windowsCursorBitmap;
    private NativePointerRect? _windowsAppliedClip;
    private NativePointerRect? _windowsRequestedClip;
    private int _windowsHitGeneration;
    private bool _windowsHitPending;
    private int _windowsHitArea;
    private PixelPoint? _windowsHitPoint;
    private string _windowsPointerStatus="not started";
    private uint _windowsCursorBeforeHide,_windowsCursorAfterHide;
    private readonly CursorAutoHide _windowsCursorAutoHide=new();

    private static NativePointerRect WindowsRect(Visual visual,double width,double height)
    {
        var origin=visual.PointToScreen(default);double scale=TopLevel.GetTopLevel(visual)!.RenderScaling;
        return new(origin.X,origin.Y,checked(origin.X+(int)Math.Round(width*scale)),checked(origin.Y+(int)Math.Round(height*scale)));
    }
    private bool WindowsPointerOnOverlay(int x,int y)
    {
        if(_toolbar is {IsVisible:true,IsHitTestVisible:true} toolbar&&WindowsRect(toolbar,toolbar.Bounds.Width,toolbar.Bounds.Height).Contains(x,y))return true;
        return _profilerWindow is {IsVisible:true} profiler&&WindowsRect(profiler,profiler.Bounds.Width,profiler.Bounds.Height).Contains(x,y);
    }
    private void SetWindowsPassthrough(bool enabled)
    {
        if(_output?.TryGetPlatformHandle() is not {HandleDescriptor:"HWND",Handle:not 0} handle)return;
        _windowsPointerPassthrough=enabled;
        nint style=OutputWindowStyle(handle.Handle,-20),wanted=(style&~(nint)0x20)|(enabled?0x80020:0);
        Marshal.SetLastPInvokeError(0);
        if(style!=wanted&&SetOutputWindowStyle(handle.Handle,-20,wanted)==0&&Marshal.GetLastPInvokeError()!=0)
            throw new InvalidOperationException("Cannot update native pointer passthrough.");
        if(enabled&&!_windowsPointerLayered)
        {
            if(!SetWindowsPointerAlpha(handle.Handle,0,255,2))throw new InvalidOperationException("Cannot initialize opaque layered pointer output.");
            _windowsPointerLayered=true;
        }
    }
    private static void MoveWindowsPointerReliably(int x,int y)
    {
        if(!ReadWindowsClip(out var previous))throw new InvalidOperationException("Cannot preserve native pointer clip.");
        var target=new NativePointerRect(x,y,checked(x+1),checked(y+1));
        try
        {
            if(!SetWindowsClip(ref target)||!MoveWindowsPointer(x,y))throw new InvalidOperationException("Cannot move native pointer to its mapped pixel.");
            Thread.Sleep(8); // original: drain already queued hardware movement
        }
        finally {SetWindowsClip(ref previous);}
    }
    private void ClipWindowsSource(bool clip)=>ApplyWindowsClip(clip?_windowsPointerMapping.Source:null);
    private void ApplyWindowsClip(NativePointerRect? clip)
    {
        if(clip is {} wanted)
        {
            if(ReadWindowsClip(out var actual)&&_windowsAppliedClip is {} last&&actual.Equals(last)&&_windowsRequestedClip is {} request&&request.Equals(wanted))return;
            if(!SetWindowsClip(ref wanted)||!ReadWindowsClip(out actual))throw new InvalidOperationException("Cannot apply native pointer boundaries.");
            _windowsRequestedClip=wanted;_windowsAppliedClip=actual;
        }
        else if(_windowsAppliedClip is {} previous)
        {
            // Do not clear a clip subsequently installed by another app.
            if(ReadWindowsClip(out var actual)&&actual.Equals(previous)&&!ClearWindowsClip(0))throw new InvalidOperationException("Cannot release native pointer boundaries.");
            _windowsAppliedClip=null;_windowsRequestedClip=null;
        }
    }
    private void ApplyWindowsMonitorClip(int x,int y,bool captured,nint owner)
    {
        if(_output is null)return;
        var monitors=_output.Screens.All.Select(s=>new NativePointerRect(s.Bounds.X,s.Bounds.Y,s.Bounds.Right,s.Bounds.Bottom)).ToArray();
        ApplyWindowsClip(WindowsMonitorClip.Compute(_windowsPointerMapping,monitors,x,y,captured,OutputForeground()==owner));
    }
    private void EndWindowsPointer(bool mapBack=true)
    {
        if(!OperatingSystem.IsWindows())return;
        _windowsCursorAutoHide.Reset();
        bool captured=_windowsPointerCaptured;_windowsPointerCaptured=false;
        bool ownsState=captured||_windowsSystemCursorHidden||_windowsPointerPassthrough||_windowsAppliedClip is not null||_windowsCursorOverlay is {IsVisible:true};
        ++_windowsHitGeneration;_windowsHitPoint=null;_windowsHitArea=0;
        ClipWindowsSource(false);
        try
        {
            if(ownsState)SetWindowsPassthrough(false);
            if(mapBack&&captured&&ReadWindowsPointer(out var point))
            {
                var mapped=_windowsPointerMapping.ToOutput(point.X,point.Y,OutputForeground()==unchecked((nint)_lastSourceInfo.WindowId));
                if(WindowsPointerMonitor(new(mapped.X,mapped.Y),0)!=0)MoveWindowsPointerReliably(mapped.X,mapped.Y);
            }
        }
        finally
        {
            if(_windowsSystemCursorHidden)
            {
                if(NativeCore.WindowsSystemCursor(1)!=1)throw new InvalidOperationException("Cannot restore the native system cursor.");
                _windowsSystemCursorHidden=false;
            }
            if(_windowsCursorOverlay is {} overlay)overlay.IsVisible=false;
        }
    }
    private void CheckWindowsPointerEdge(nint source,PixelPoint point)
    {
        if(_windowsHitPending||_windowsHitPoint==point)return;
        _windowsHitPoint=point;_windowsHitPending=true;int generation=_windowsHitGeneration;
        _=Task.Run(()=>
        {
            nint packed=unchecked((nint)((uint)(ushort)point.X|((uint)(ushort)point.Y<<16)));
            bool valid=WindowsPointerHit(source,0x84,0,packed,2,100,out nint hit)!=0;
            Avalonia.Threading.Dispatcher.UIThread.Post(()=>
            {
                _windowsHitPending=false;
                if(generation==_windowsHitGeneration)_windowsHitArea=valid?(int)hit:10;
            });
        });
    }
    private void UpdateWindowsPointer()
    {
        if(!OperatingSystem.IsWindows())return;
        if(_windowsPointerSuppressed||_sourceSuspended||_stop.IsCancellationRequested||_output is not {IsVisible:true}||_bitmap is null||_image is null)
        {_windowsPointerStatus="inactive/suppressed";EndWindowsPointer();return;}
        NativePointerRect source;
        lock(_captureLock)
        {
            if(_capture is not {IsClosed:false,IsInvalid:false} capture||NativeCore.CaptureWindowsPointerRect(capture,
                _lastSourceInfo.WindowId,_lastSourceInfo.ProcessId,_lastSourceSequence,out source,16)!=1)
            {_windowsPointerStatus="waiting for matching source geometry";EndWindowsPointer();return;}
        }
        var crop=SourceRegion.Resolve(_settings,_captureFrameWidth,_captureFrameHeight);
        source=new(checked(source.Left+(int)crop.X),checked(source.Top+(int)crop.Y),checked(source.Left+(int)(crop.X+crop.Width)),checked(source.Top+(int)(crop.Y+crop.Height)));
        var mapping=new WindowsPointerMapping(source,WindowsRect(_image,_image.Bounds.Width,_image.Bounds.Height),WindowsRect(_output,_output.ClientSize.Width,_output.ClientSize.Height));
        if(mapping.Destination.Width!=_bitmap.PixelSize.Width||mapping.Destination.Height!=_bitmap.PixelSize.Height)
        {_windowsPointerStatus="waiting for pixel-aligned destination geometry";EndWindowsPointer();return;}
        if(_windowsPointerCaptured&&!mapping.Equals(_windowsPointerMapping))EndWindowsPointer();
        _windowsPointerMapping=mapping;
        if(!ReadWindowsPointer(out var physical)) {_windowsPointerStatus="GetCursorPos failed";EndWindowsPointer(false);return;}
        nint output=_output.TryGetPlatformHandle()!.Handle,owner=unchecked((nint)_lastSourceInfo.WindowId);
        var visual=_windowsPointerCaptured?mapping.ToOutput(physical.X,physical.Y):(physical.X,physical.Y);
        // Native source input bypasses Avalonia mouse messages. The toolbar
        // still consumes the same virtual pointer that is drawn on the output.
        var origin=_output.PointToScreen(default);double dpi=_output.RenderScaling;
        _toolbarPointer=new Point((visual.Item1-origin.X)/dpi,(visual.Item2-origin.Y)/dpi);UpdateToolbarVisibility();
        var gui=new WindowsGuiInfo {Size=(uint)Marshal.SizeOf<WindowsGuiInfo>()};
        bool nativeDrag=ReadWindowsGui(0,ref gui)&&gui.Capture!=0&&(gui.Flags&0x1c)==0;
        if(nativeDrag&&WindowsPointerRoot(gui.Capture,2)!=owner) {_windowsPointerStatus="another application's native capture";EndWindowsPointer(false);return;}
        if(nativeDrag&&_windowsPointerCaptured)
        {
            // Source owns the actual drag/capture; never warp midway through it.
            _windowsPointerStatus="native source drag";ClipWindowsSource((gui.Flags&2)==0);DrawWindowsCursor(visual.Item1,visual.Item2);return;
        }
        bool onOutput=NativeCore.WindowsPointerWindow(output,visual.Item1,visual.Item2,0)==(ulong)output;
        if(!_windowsPointerCaptured&&onOutput&&!nativeDrag&&OutputForeground()==owner&&
            !WindowsPointerOnOverlay(visual.Item1,visual.Item2)&&!mapping.Destination.Contains(visual.Item1,visual.Item2))
            visual=(Math.Clamp(visual.Item1,mapping.Destination.Left,mapping.Destination.Right-1),
                Math.Clamp(visual.Item2,mapping.Destination.Top,mapping.Destination.Bottom-1));
        if(!onOutput||WindowsPointerOnOverlay(visual.Item1,visual.Item2)||!mapping.Destination.Contains(visual.Item1,visual.Item2))
        {
            _windowsPointerStatus="outside output/image or over local overlay";
            if(_windowsPointerCaptured)
            {
                var exit=mapping.ToOutput(physical.X,physical.Y,OutputForeground()==owner);
                if(!nativeDrag&&WindowsPointerMonitor(new(exit.X,exit.Y),0)==0)
                {
                    MoveWindowsPointerReliably(Math.Clamp(physical.X,source.Left,source.Right-1),Math.Clamp(physical.Y,source.Top,source.Bottom-1));
                    DrawWindowsCursor(visual.Item1,visual.Item2);return;
                }
                EndWindowsPointer(!nativeDrag);
            }
            else ClipWindowsSource(false);
            return;
        }
        var mapped=_windowsPointerCaptured?(physical.X,physical.Y):mapping.ToSource(visual.Item1,visual.Item2);
        bool sourceVisible=NativeCore.WindowsPointerWindow(output,mapped.Item1,mapped.Item2,1)==(ulong)owner;
        if(!sourceVisible||nativeDrag) {_windowsPointerStatus="source point occluded or native capture busy";EndWindowsPointer(!nativeDrag);return;}
        CheckWindowsPointerEdge(owner,new(mapped.Item1,mapped.Item2));
        if(_windowsHitArea is >=10 and <=17) {_windowsPointerStatus="native sizing edge";EndWindowsPointer();return;}
        _windowsPointerStatus="captured";
        if(!_windowsPointerCaptured)
        {
            if(!source.Contains(mapped.Item1,mapped.Item2)||!DrawWindowsCursor(visual.Item1,visual.Item2))return;
            SetWindowsPassthrough(true);
            MoveWindowsPointerReliably(mapped.Item1,mapped.Item2);
            _windowsCursorBeforeHide=0;_windowsCursorAfterHide=0;
            var beforeHide=new WindowsCursorInfo {Size=(uint)Marshal.SizeOf<WindowsCursorInfo>()};
            if(ReadWindowsCursor(ref beforeHide))_windowsCursorBeforeHide=beforeHide.Flags;
            if(NativeCore.WindowsSystemCursor(0)!=1) {_windowsPointerStatus="system cursor visibility API unavailable";SetWindowsPassthrough(false);MoveWindowsPointerReliably(visual.Item1,visual.Item2);return;}
            var afterHide=new WindowsCursorInfo {Size=(uint)Marshal.SizeOf<WindowsCursorInfo>()};
            if(ReadWindowsCursor(ref afterHide))_windowsCursorAfterHide=afterHide.Flags;
            _windowsSystemCursorHidden=true;_windowsPointerCaptured=true;
            ApplyWindowsMonitorClip(mapped.Item1,mapped.Item2,true,owner);
            // Original _ReliableSetCursorPos refreshes the real source cursor.
            // This is a cursor notification, never fabricated mouse input.
            nint child=WindowsPointerChild(new(mapped.Item1,mapped.Item2));
            if(child!=0&&WindowsPointerRoot(child,2)==owner&&OutputWindowProcess(child,out uint process)!=0&&process==_lastSourceInfo.ProcessId)
            {
                nint packed=unchecked((nint)((uint)(ushort)mapped.Item1|((uint)(ushort)mapped.Item2<<16)));
                WindowsPointerHit(child,0x84,0,packed,2,10,out nint area);
                HintWindowsCursor(child,0x20,owner,unchecked((nint)((0x200u<<16)|(uint)(ushort)area)));
            }
        }
        else if(!DrawWindowsCursor(visual.Item1,visual.Item2))EndWindowsPointer();
        if(_windowsPointerCaptured)ApplyWindowsMonitorClip(mapped.Item1,mapped.Item2,true,owner);
    }
    private unsafe bool DrawWindowsCursor(int x,int y)
    {
        if(_bitmap is null||_output?.Content is not Grid grid||_image is null)return false;
        var cursor=new WindowsCursorInfo {Size=(uint)Marshal.SizeOf<WindowsCursorInfo>()};
        if(!ReadWindowsCursor(ref cursor)) {_windowsPointerStatus="GetCursorInfo failed";return false;}
        bool visible=WindowsCursorVisibility.Draw(cursor.Flags,_windowsSystemCursorHidden,_windowsCursorBeforeHide,_windowsCursorAfterHide);
        bool moving=false;
        if(_settings.AutoHideCursorEnabled)
        {
            var sourceGui=new WindowsGuiInfo {Size=(uint)Marshal.SizeOf<WindowsGuiInfo>()};
            var outputGui=new WindowsGuiInfo {Size=(uint)Marshal.SizeOf<WindowsGuiInfo>()};
            uint sourceThread=OutputWindowProcess(unchecked((nint)_lastSourceInfo.WindowId),out _);
            uint outputThread=OutputWindowProcess(_output.TryGetPlatformHandle()!.Handle,out _);
            const uint guiInMoveSize=2;
            moving=(sourceThread!=0&&ReadWindowsGui(sourceThread,ref sourceGui)&&(sourceGui.Flags&guiInMoveSize)!=0)||
                (outputThread!=0&&ReadWindowsGui(outputThread,ref outputGui)&&(outputGui.Flags&guiInMoveSize)!=0);
        }
        bool idleVisible=_windowsCursorAutoHide.Visible(System.Diagnostics.Stopwatch.GetElapsedTime(0),x,y,visible?cursor.Cursor:0,
            _windowsPointerCaptured,moving,_settings.AutoHideCursorEnabled,_settings.AutoHideCursorDelay);
        if(!visible||!idleVisible)
        {_windowsPointerStatus=visible?"cursor idle timeout":"source cursor not visible: flags="+cursor.Flags;if(_windowsCursorOverlay is {} hidden)hidden.IsVisible=false;return true;}
        if(NativeCore.WindowsCursorCopy((ulong)cursor.Cursor,out var shape,24)!=1) {_windowsPointerStatus="native cursor copy: "+NativeCore.Error().Message;return false;}
        try
        {
            var dest=_windowsPointerMapping.Destination;
            int left=Math.Max(dest.Left,checked(x-(int)shape.HotX)),top=Math.Max(dest.Top,checked(y-(int)shape.HotY));
            int right=Math.Min(dest.Right,checked(x-(int)shape.HotX+(int)shape.Width)),bottom=Math.Min(dest.Bottom,checked(y-(int)shape.HotY+(int)shape.Height));
            if(right<=left||bottom<=top) {if(_windowsCursorOverlay is {} hidden)hidden.IsVisible=false;return true;}
            int width=right-left,height=bottom-top;var pixels=new byte[checked(width*height*4)];
            using(var background=_bitmap.Lock())
                for(int row=0;row<height;row++)new ReadOnlySpan<byte>((byte*)background.Address+(top-dest.Top+row)*background.RowBytes+(left-dest.Left)*4,width*4).CopyTo(pixels.AsSpan(row*width*4,width*4));
            fixed(byte* data=pixels)
                if(NativeCore.WindowsCursorDraw(shape.Handle,data,(ulong)pixels.Length,(uint)width,(uint)height,x-(int)shape.HotX-left,y-(int)shape.HotY-top,shape.Width,shape.Height)!=1) {_windowsPointerStatus="native cursor draw: "+NativeCore.Error().Message;return false;}
            if(_windowsCursorOverlay is null)
            {
                _windowsCursorOverlay=new Image {IsHitTestVisible=false,Stretch=Avalonia.Media.Stretch.Fill,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};
                grid.Children.Insert(1,_windowsCursorOverlay);
            }
            if(_windowsCursorBitmap?.PixelSize!=new PixelSize(width,height))
            {
                _windowsCursorOverlay.Source=null;_windowsCursorBitmap?.Dispose();
                _windowsCursorBitmap=new(new(width,height),new(96,96),PixelFormat.Rgba8888,AlphaFormat.Opaque);
                _windowsCursorOverlay.Source=_windowsCursorBitmap;
            }
            using(var destination=_windowsCursorBitmap.Lock())
                for(int row=0;row<height;row++)pixels.AsSpan(row*width*4,width*4).CopyTo(new Span<byte>((byte*)destination.Address+row*destination.RowBytes,width*4));
            double scale=_output.RenderScaling;var origin=_output.PointToScreen(default);
            _windowsCursorOverlay.Margin=new Thickness((left-origin.X)/scale,(top-origin.Y)/scale,0,0);
            _windowsCursorOverlay.Width=width/scale;_windowsCursorOverlay.Height=height/scale;
            _windowsCursorOverlay.IsVisible=true;_windowsCursorOverlay.InvalidateVisual();return true;
        }
        finally {NativeCore.WindowsCursorDestroy(shape.Handle);}
    }
}
