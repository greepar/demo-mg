#import "input_macos.h"
#include <algorithm>
#include <map>
#include <unistd.h>

@implementation MagpieInputBridge {
    __weak NSWindow* _window;
    id _monitor, _resignMonitor;
    CGWindowID _sourceWindow;
    pid_t _sourceProcess;
    CGRect _destination, _crop;
    CGPoint _lastPoint;
    bool _keyboardFocus, _requestedAccess;
    std::map<int, CGEventRef> _pressed;
    CGEventFlags _modifiers;
    double _toolbarHeight;
    NSString* _reservedCharacter;
    int _reservedCode;
    NSString* _screenshotCharacter;
    int _screenshotCode;
    CGEventFlags _screenshotModifiers;
    CGEventFlags _reservedModifiers;
}
- (void)setSourceWindow:(CGWindowID)window process:(pid_t)process {
    if(process==getpid()) {window=0;process=0;}
    if(_sourceWindow==window && _sourceProcess==process)return;
    [self releaseInput];
    _sourceWindow=process==getpid()?0:window;
    _sourceProcess=process==getpid()?0:process;
    _keyboardFocus=false;
}
- (void)setReservedCharacter:(NSString*)character keyCode:(int)keyCode modifiers:(CGEventFlags)modifiers {
    _reservedCharacter=character;_reservedCode=keyCode;_reservedModifiers=modifiers;
}
- (void)setScreenshotCharacter:(NSString*)character keyCode:(int)keyCode modifiers:(CGEventFlags)modifiers {
    _screenshotCharacter=character;_screenshotCode=keyCode;_screenshotModifiers=modifiers;
}
- (int)configureWindow:(NSWindow*)window destination:(CGRect)destination crop:(CGRect)crop toolbarHeight:(double)toolbarHeight {
    if(_window!=window) {
        [self stop];_window=window;
        window.acceptsMouseMovedEvents=YES;
        __weak MagpieInputBridge* weakSelf=self;
        _monitor=[NSEvent addLocalMonitorForEventsMatchingMask:
            NSEventMaskMouseMoved|NSEventMaskLeftMouseDown|NSEventMaskLeftMouseUp|
            NSEventMaskRightMouseDown|NSEventMaskRightMouseUp|NSEventMaskOtherMouseDown|
            NSEventMaskOtherMouseUp|NSEventMaskLeftMouseDragged|NSEventMaskRightMouseDragged|
            NSEventMaskOtherMouseDragged|NSEventMaskScrollWheel|NSEventMaskKeyDown|
            NSEventMaskKeyUp|NSEventMaskFlagsChanged handler:^NSEvent*(NSEvent* event) {
                MagpieInputBridge* bridge=weakSelf;
                return bridge?[bridge handleEvent:event]:event;
            }];
        _resignMonitor=[NSNotificationCenter.defaultCenter addObserverForName:NSWindowDidResignKeyNotification
            object:window queue:nil usingBlock:^(NSNotification* notification) {
                (void)notification;[weakSelf releaseInput];
            }];
    }
    _destination=destination;_crop=crop;_toolbarHeight=toolbarHeight;
    if(!_sourceWindow || _sourceProcess<=0)return 0;
    return CGPreflightPostEventAccess()?1:2;
}
- (bool)sourceFrame:(CGRect*)frame {
    if(!_sourceWindow || _sourceProcess<=0)return false;
    NSArray* windows=CFBridgingRelease(CGWindowListCopyWindowInfo(kCGWindowListOptionIncludingWindow,_sourceWindow));
    for(NSDictionary* window in windows) {
        if([window[(__bridge NSString*)kCGWindowNumber] unsignedIntValue]!=_sourceWindow ||
           [window[(__bridge NSString*)kCGWindowOwnerPID] intValue]!=_sourceProcess)continue;
        return CGRectMakeWithDictionaryRepresentation((__bridge CFDictionaryRef)window[(__bridge NSString*)kCGWindowBounds],frame)
            && frame->size.width>0 && frame->size.height>0;
    }
    return false;
}
- (void)post:(CGEventRef)event {
    CGEventSetIntegerValueField(event,kCGMouseEventWindowUnderMousePointer,_sourceWindow);
    CGEventSetIntegerValueField(event,kCGMouseEventWindowUnderMousePointerThatCanHandleThisEvent,_sourceWindow);
    CGEventPostToPid(_sourceProcess,event);
}
- (NSEvent*)handleEvent:(NSEvent*)event {
    if(!_window || event.window!=_window || !_sourceWindow)return event;
    const auto type=event.type;
    const bool key=type==NSEventTypeKeyDown || type==NSEventTypeKeyUp || type==NSEventTypeFlagsChanged;
    const bool down=type==NSEventTypeLeftMouseDown || type==NSEventTypeRightMouseDown || type==NSEventTypeOtherMouseDown;
    const bool up=type==NSEventTypeLeftMouseUp || type==NSEventTypeRightMouseUp || type==NSEventTypeOtherMouseUp;
    const bool drag=type==NSEventTypeLeftMouseDragged || type==NSEventTypeRightMouseDragged || type==NSEventTypeOtherMouseDragged;
    const int identity=key?int(event.keyCode):128+((down||up||drag)?int(event.buttonNumber):0);
    bool dragging=false;
    for(const auto& entry:_pressed)if(entry.first>=128)dragging=true;
    if(key) {
        // Session commands stay local. Toolbar text fields keep their own keyboard focus.
        if(event.keyCode==53 || event.keyCode==111)return event;
        if(type!=NSEventTypeFlagsChanged) {
            const auto flags=event.modifierFlags&(NSEventModifierFlagShift|NSEventModifierFlagControl|NSEventModifierFlagOption|NSEventModifierFlagCommand);
            if(flags==_screenshotModifiers && (int(event.keyCode)==_screenshotCode ||
                (_screenshotCharacter.length && [event.charactersIgnoringModifiers.lowercaseString isEqualToString:_screenshotCharacter])))return event;
            if(flags==_reservedModifiers && (int(event.keyCode)==_reservedCode ||
                (_reservedCharacter.length && [event.charactersIgnoringModifiers.lowercaseString isEqualToString:_reservedCharacter])))return event;
        }
        if(!_keyboardFocus && !_pressed.contains(identity))return event;
    } else {
        NSPoint point=[_window.contentView convertPoint:event.locationInWindow fromView:nil];
        if(!_window.contentView.isFlipped)point.y=_window.contentView.bounds.size.height-point.y;
        if((!CGRectContainsPoint(_destination,point) || point.y<_toolbarHeight) && !dragging) {
            if(down)[self releaseInput];
            return event;
        }
        if(down)_keyboardFocus=true;
        if(_destination.size.width<=0 || _destination.size.height<=0)return event;
        CGRect source;
        if(![self sourceFrame:&source]) { [self releaseInput];return nil; }
        double x=std::clamp((point.x-_destination.origin.x)/_destination.size.width,0.0,1.0);
        double y=std::clamp((point.y-_destination.origin.y)/_destination.size.height,0.0,1.0);
        _lastPoint=CGPointMake(source.origin.x+(_crop.origin.x+x*_crop.size.width)*source.size.width,
                              source.origin.y+(_crop.origin.y+y*_crop.size.height)*source.size.height);
    }
    if(!CGPreflightPostEventAccess()) {
        if(down && !_requestedAccess) { _requestedAccess=true;CGRequestPostEventAccess(); }
        [self releaseInput];return nil;
    }
    if(key) {
        CGRect source;
        if(![self sourceFrame:&source]) { [self releaseInput];return nil; }
    }
    CGEventRef original=event.CGEvent;
    if(!original)return event;
    CGEventRef copy=CGEventCreateCopy(original);
    if(!copy)return event;
    CGEventSetLocation(copy,_lastPoint);
    // Retain the real hardware key code, flags, wheel phases, click count and pressure.
    [self post:copy];
    _modifiers=CGEventGetFlags(copy);
    if(down || type==NSEventTypeKeyDown) {
        auto previous=_pressed.find(identity);
        if(previous!=_pressed.end())CFRelease(previous->second);
        _pressed[identity]=CGEventCreateCopy(copy);
    } else if(up || type==NSEventTypeKeyUp) {
        auto previous=_pressed.find(identity);
        if(previous!=_pressed.end()) { CFRelease(previous->second);_pressed.erase(previous); }
    }
    CFRelease(copy);
    return nil;
}
- (void)releaseInput {
    for(const auto& entry:_pressed) {
        CGEventRef event=entry.second;
        if(entry.first<128)CGEventSetType(event,kCGEventKeyUp);
        else {
            int button=entry.first-128;
            CGEventSetType(event,button==0?kCGEventLeftMouseUp:button==1?kCGEventRightMouseUp:kCGEventOtherMouseUp);
        }
        CGEventSetFlags(event,0);CGEventSetLocation(event,_lastPoint);
        [self post:event];CFRelease(event);
    }
    _pressed.clear();
    if(_modifiers && _sourceProcess>0) {
        for(CGKeyCode code:{CGKeyCode(56),CGKeyCode(59),CGKeyCode(58),CGKeyCode(55)}) {
            CGEventRef event=CGEventCreateKeyboardEvent(nullptr,code,false);
            if(event) { CGEventSetType(event,kCGEventFlagsChanged);CGEventSetFlags(event,0);[self post:event];CFRelease(event); }
        }
    }
    _modifiers=0;_keyboardFocus=false;
}
- (void)stop {
    [self releaseInput];
    if(_monitor) { [NSEvent removeMonitor:_monitor];_monitor=nil; }
    if(_resignMonitor) { [NSNotificationCenter.defaultCenter removeObserver:_resignMonitor];_resignMonitor=nil; }
    _window=nil;
}
@end
