#pragma once
#import <AppKit/AppKit.h>

// All methods and event callbacks belong to the Cocoa main thread.
@interface MagpieInputBridge : NSObject
- (void)setSourceWindow:(CGWindowID)window process:(pid_t)process;
- (int)configureWindow:(NSWindow*)window destination:(CGRect)destination crop:(CGRect)crop toolbarHeight:(double)toolbarHeight;
- (void)setReservedCharacter:(NSString*)character keyCode:(int)keyCode modifiers:(CGEventFlags)modifiers;
- (void)setScreenshotCharacter:(NSString*)character keyCode:(int)keyCode modifiers:(CGEventFlags)modifiers;
- (NSEvent*)handleEvent:(NSEvent*)event;
- (void)releaseInput;
- (void)stop;
@end
