#pragma once
namespace magpie {
enum class MaximizedPolicy { Allowed,Disabled,Windowed,Fullscreen };
inline MaximizedPolicy maximizedPolicy(bool maximized,bool windowed,bool allow) {
    if(!maximized)return MaximizedPolicy::Allowed;
    if(windowed)return MaximizedPolicy::Windowed;
    return allow?MaximizedPolicy::Allowed:MaximizedPolicy::Disabled;
}
inline MaximizedPolicy fullscreenPolicy(bool nativeFrame,bool rendererMatches,bool allow) {
    return !nativeFrame&&rendererMatches&&!allow?MaximizedPolicy::Fullscreen:MaximizedPolicy::Allowed;
}
inline const char* maximizedPolicyMessage(MaximizedPolicy policy) {
    switch(policy) {
    case MaximizedPolicy::Disabled:return "Scaling a maximized source window is disabled in settings";
    case MaximizedPolicy::Windowed:return "Windowed scaling does not support maximized source windows";
    case MaximizedPolicy::Fullscreen:return "Scaling a borderless fullscreen source is disabled in settings";
    default:return "";
    }
}
}
