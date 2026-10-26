#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include "windows_graphics_capture.h"
#include "capture_mailbox.h"
#include "error.h"
#include <d3d11.h>
#include <dxgi.h>
#include <dwmapi.h>
#include <roapi.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Metadata.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <chrono>
#include <condition_variable>
#include <mutex>
#include <thread>

namespace magpie {
namespace {
using namespace winrt;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
using winrt::Windows::Foundation::Metadata::ApiInformation;
using winrt::Windows::Foundation::Metadata::IApiInformationStatics;
// These apartments are shorter-lived than the process. Use uncached factory
// references: C++/WinRT's convenience static calls keep process-wide factories
// that can point into unloaded COM modules after the last RoUninitialize.
template<class Class,class Interface> auto factory() {
    auto value=try_get_activation_factory<Class,Interface>();
    if(!value)throw std::runtime_error("Required Windows Graphics Capture factory is unavailable");
    return value;
}
struct Apartment {
    HRESULT result=RoInitialize(RO_INIT_MULTITHREADED);
    Apartment() {if(FAILED(result)&&result!=RPC_E_CHANGED_MODE)check_hresult(result);}
    ~Apartment() {if(SUCCEEDED(result))RoUninitialize();}
};
struct Geometry {
    RECT frame{},client{};
    bool operator==(const Geometry& other) const {return EqualRect(&frame,&other.frame)&&EqualRect(&client,&other.client);}
};
Geometry geometry(HWND window) {
    Geometry result;
    check_hresult(DwmGetWindowAttribute(window,DWMWA_EXTENDED_FRAME_BOUNDS,&result.frame,sizeof(result.frame)));
    if(!GetClientRect(window,&result.client))throw std::runtime_error("Cannot read source client bounds");
    POINT origin{result.client.left,result.client.top};
    if(!ClientToScreen(window,&origin))throw std::runtime_error("Cannot map source client bounds");
    OffsetRect(&result.client,origin.x,origin.y);
    // Match original CalcWindowCapturedFrameBounds: maximized Windows 11 before
    // 24H2 has a different capture origin. RtlGetVersion avoids manifest-based
    // version virtualization. No window styles or source ownership are changed.
    if(IsZoomed(window)) {
        using VersionFunction=LONG(WINAPI*)(OSVERSIONINFOW*);
        auto address=GetProcAddress(GetModuleHandleW(L"ntdll.dll"),"RtlGetVersion");
        VersionFunction version{};static_assert(sizeof(version)==sizeof(address));memcpy(&version,&address,sizeof(version));
        OSVERSIONINFOW os{};os.dwOSVersionInfoSize=sizeof(os);
        if(!version||version(&os)!=0)throw std::runtime_error("Cannot determine Graphics Capture frame bounds on this Windows version");
        if(os.dwBuildNumber>=22000&&os.dwBuildNumber<26100) {
            BOOL border{};check_hresult(DwmGetWindowAttribute(window,DWMWA_NCRENDERING_ENABLED,&border,sizeof(border)));
            if(border) {
                MONITORINFO monitor{};monitor.cbSize=sizeof(monitor);
                if(!GetMonitorInfoW(MonitorFromWindow(window,MONITOR_DEFAULTTONEAREST),&monitor))throw std::runtime_error("Cannot read source monitor bounds");
                if(result.client.top<monitor.rcWork.top)result.frame=result.client;
                else if(!IntersectRect(&result.frame,&result.frame,&monitor.rcWork))throw std::runtime_error("Source frame is outside the monitor");
            }
        }
    }
    return result;
}
bool fits(const Geometry& g,int32_t width,int32_t height) {
    return width>0&&height>0&&uint64_t(width)*uint32_t(height)<=16777216&&
        g.frame.right-g.frame.left==width&&g.frame.bottom-g.frame.top==height&&
        g.client.left>=g.frame.left&&g.client.top>=g.frame.top&&g.client.right<=g.frame.right&&g.client.bottom<=g.frame.bottom&&
        g.client.right>g.client.left&&g.client.bottom>g.client.top;
}
struct Wake {
    std::mutex mutex;
    std::condition_variable changed;
    bool arrived{},closed{};
};
}

struct WindowsGraphicsCapture::State {
    HWND window;
    const std::atomic<bool>& cancelled;
    std::function<bool()> valid;
    std::atomic<bool> stop{false};
    std::atomic<bool> suspension{false};
    std::shared_ptr<Wake> wake=std::make_shared<Wake>();
    std::mutex mutex;
    std::condition_variable started;
    CaptureMailbox mailbox;
    std::string failure;
    std::string waiting="Waiting for the first Graphics Capture frame";
    bool ready{};
    uint64_t revision{};
    std::thread worker;
    State(HWND target,const std::atomic<bool>& cancellation,std::function<bool()> validator):window(target),cancelled(cancellation),valid(std::move(validator)) {
        auto source=emptySource();source.kind=1;source.window_id=uint64_t(reinterpret_cast<uintptr_t>(window));
        DWORD process{};GetWindowThreadProcessId(window,&process);source.process_id=process;
        source.flags=MP_SOURCE_WINDOW|MP_SOURCE_PID;mailbox.begin(source);
    }
    ~State() {stop=true;wake->changed.notify_all();if(worker.joinable())worker.join();}
    bool stopping() const {return stop||cancelled;}
    void checkSource() {if(stopping()||!valid())throw std::runtime_error("Graphics Capture source was stopped or destroyed");}
    void run() noexcept {
        try {produce();}
        catch(const hresult_error& ex) {fail("Graphics Capture: "+to_string(ex.message()));}
        catch(const std::exception& ex) {fail(ex.what());}
        catch(...) {fail("Unexpected Graphics Capture failure");}
    }
    void pause() {mailbox.pauseInput();++revision;}
    void fail(std::string message) {std::lock_guard lock(mutex);failure=std::move(message);pause();ready=true;started.notify_all();}
    void produce() {
        Apartment apartment;
        checkSource();
        if(!factory<GraphicsCaptureSession,IGraphicsCaptureSessionStatics>().IsSupported())throw std::runtime_error("Windows Graphics Capture is not supported by this desktop");
        com_ptr<ID3D11Device> device;com_ptr<ID3D11DeviceContext> context;
        check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr,0,D3D11_SDK_VERSION,device.put(),nullptr,context.put()));
        IDirect3DDevice wrapped{nullptr};auto dxgi=device.as<IDXGIDevice>();
        check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(),reinterpret_cast<::IInspectable**>(put_abi(wrapped))));
        GraphicsCaptureItem item{nullptr};
        auto interop=factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();
        check_hresult(interop->CreateForWindow(window,guid_of<GraphicsCaptureItem>(),put_abi(item)));
        auto size=item.Size();
        if(size.Width<=0||size.Height<=0||uint64_t(size.Width)*uint32_t(size.Height)>16777216)throw std::runtime_error("Unsupported Graphics Capture dimensions");
        auto pool=factory<Direct3D11CaptureFramePool,IDirect3D11CaptureFramePoolStatics2>().CreateFreeThreaded(wrapped,DirectXPixelFormat::B8G8R8A8UIntNormalized,2,size);
        auto session=pool.CreateCaptureSession(item);
        auto configure=[](const GraphicsCaptureSession& target) {
            if(factory<ApiInformation,IApiInformationStatics>().IsPropertyPresent(L"Windows.Graphics.Capture.GraphicsCaptureSession",L"IsCursorCaptureEnabled"))target.IsCursorCaptureEnabled(false);
        };
        struct Close {
            GraphicsCaptureSession& session;Direct3D11CaptureFramePool& pool;
            ~Close() {try {session.Close();}catch(...) {}try {pool.Close();}catch(...) {}}
        } close{session,pool};
        // No access request/picker is triggered. Keep the system capture border
        // where permission is required; omit captured cursor like the other ports.
        configure(session);
        std::weak_ptr<Wake> weak=wake;
        auto arrived=pool.FrameArrived(auto_revoke,[weak](const auto&,const auto&) {
            if(auto state=weak.lock()) {std::lock_guard lock(state->mutex);state->arrived=true;state->changed.notify_one();}
        });
        auto closed=item.Closed(auto_revoke,[weak](const auto&,const auto&) {
            if(auto state=weak.lock()) {std::lock_guard lock(state->mutex);state->closed=true;state->changed.notify_one();}
        });
        session.StartCapture();
        {std::lock_guard lock(mutex);ready=true;started.notify_all();}
        com_ptr<ID3D11Texture2D> staging;uint32_t stagingWidth{},stagingHeight{};
        Geometry previous{};bool haveGeometry=false,inactive=false;
        while(!stopping()) {
            {
                std::unique_lock lock(wake->mutex);
                wake->changed.wait_for(lock,std::chrono::milliseconds(25),[&]{return wake->arrived||wake->closed||stopping();});
                if(wake->closed)throw std::runtime_error("The Graphics Capture source was closed");
                wake->arrived=false;
            }
            checkSource();
            if(suspension.exchange(false)) {
                std::lock_guard lock(mutex);pause();inactive=true;haveGeometry=false;
            }
            if(IsIconic(window)||!IsWindowVisible(window)) {
                {std::lock_guard lock(mutex);if(!inactive)pause();}
                inactive=true;haveGeometry=false;
                while(auto obsolete=pool.TryGetNextFrame())obsolete.Close();
                continue;
            }
            if(inactive) {
                // Drop buffered pre-suspension content even when the restored
                // client has the same size. Recreate alone can discard the sole
                // restore update of a static source. Restart the session too,
                // requiring a new initial frame without repainting the source.
                session.Close();
                pool.Recreate(wrapped,DirectXPixelFormat::B8G8R8A8UIntNormalized,2,size);
                session=pool.CreateCaptureSession(item);configure(session);session.StartCapture();
                {std::lock_guard lock(mutex);waiting="Waiting for a fresh frame from the restarted restored-source session";}
                inactive=false;continue;
            }
            auto before=geometry(window);
            if(!haveGeometry||!(before==previous)) {
                std::lock_guard lock(mutex);pause();previous=before;haveGeometry=true;
            }
            auto frame=pool.TryGetNextFrame();if(!frame)continue;
            while(auto next=pool.TryGetNextFrame())frame=std::move(next);
            auto content=frame.ContentSize();
            if(content.Width!=size.Width||content.Height!=size.Height) {
                {std::lock_guard lock(mutex);pause();}
                frame.Close();frame=nullptr;
                if(content.Width<=0||content.Height<=0||uint64_t(content.Width)*uint32_t(content.Height)>16777216)throw std::runtime_error("Unsupported resized Graphics Capture dimensions");
                size=content;pool.Recreate(wrapped,DirectXPixelFormat::B8G8R8A8UIntNormalized,2,size);continue;
            }
            if(!fits(before,content.Width,content.Height)) {
                std::lock_guard lock(mutex);pause();
                waiting="Waiting for matching WGC geometry: content="+std::to_string(content.Width)+"x"+std::to_string(content.Height)+
                    " frame="+std::to_string(before.frame.right-before.frame.left)+"x"+std::to_string(before.frame.bottom-before.frame.top)+
                    " client-offset="+std::to_string(before.client.left-before.frame.left)+","+std::to_string(before.client.top-before.frame.top)+
                    " client="+std::to_string(before.client.right-before.client.left)+"x"+std::to_string(before.client.bottom-before.client.top);
                continue;
            }
            auto access=frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
            com_ptr<ID3D11Texture2D> texture;check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D),texture.put_void()));
            D3D11_TEXTURE2D_DESC description{};texture->GetDesc(&description);
            if(description.Format!=DXGI_FORMAT_B8G8R8A8_UNORM||description.Width<uint32_t(content.Width)||description.Height<uint32_t(content.Height))
                throw std::runtime_error("Unexpected Graphics Capture texture: format="+std::to_string(uint32_t(description.Format))+
                    " texture="+std::to_string(description.Width)+"x"+std::to_string(description.Height)+
                    " content="+std::to_string(content.Width)+"x"+std::to_string(content.Height)+
                    " pool="+std::to_string(size.Width)+"x"+std::to_string(size.Height));
            uint32_t width=uint32_t(before.client.right-before.client.left),height=uint32_t(before.client.bottom-before.client.top);
            if(!staging||width!=stagingWidth||height!=stagingHeight) {
                staging=nullptr;D3D11_TEXTURE2D_DESC desc{};desc.Width=width;desc.Height=height;desc.MipLevels=desc.ArraySize=1;
                desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
                check_hresult(device->CreateTexture2D(&desc,nullptr,staging.put()));stagingWidth=width;stagingHeight=height;
            }
            auto x=UINT(before.client.left-before.frame.left),y=UINT(before.client.top-before.frame.top);
            D3D11_BOX crop{x,y,0,x+width,y+height,1};context->CopySubresourceRegion(staging.get(),0,0,0,0,texture.get(),0,&crop);
            D3D11_MAPPED_SUBRESOURCE mapped{};check_hresult(context->Map(staging.get(),0,D3D11_MAP_READ,0,&mapped));
            struct Unmap {ID3D11DeviceContext* context;ID3D11Texture2D* texture;~Unmap(){context->Unmap(texture,0);}} unmap{context.get(),staging.get()};
            checkSource();auto after=geometry(window);
            std::lock_guard lock(mutex);
            if(suspension||IsIconic(window)||!IsWindowVisible(window)||!(before==after)) {pause();continue;}
            mailbox.publish(mailbox.generation(),static_cast<const uint8_t*>(mapped.pData),width,height,mapped.RowPitch);
            waiting="Waiting for the next Graphics Capture frame";
        }
        // Revoke before closing; queued callbacks only hold weak signal state.
        arrived.revoke();closed.revoke();
    }
};
bool WindowsGraphicsCapture::supported() noexcept {
    try {
        Apartment apartment;
        return factory<GraphicsCaptureSession,IGraphicsCaptureSessionStatics>().IsSupported()&&bool(try_get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>());
    } catch(...) {return false;}
}
WindowsGraphicsCapture::WindowsGraphicsCapture(HWND window,const std::atomic<bool>& cancelled,std::function<bool()> valid)
    :state_(std::make_unique<State>(window,cancelled,std::move(valid))) {
    state_->worker=std::thread([state=state_.get()]{state->run();});
    std::unique_lock lock(state_->mutex);
    if(!state_->started.wait_for(lock,std::chrono::seconds(10),[&]{return state_->ready;}))throw std::runtime_error("Windows Graphics Capture initialization timed out");
    if(!state_->failure.empty())throw std::runtime_error(state_->failure);
}
WindowsGraphicsCapture::~WindowsGraphicsCapture()=default;
int WindowsGraphicsCapture::frame(const uint8_t** rgba,uint32_t* width,uint32_t* height,uint64_t* revision) {
    std::lock_guard lock(state_->mutex);
    if(!state_->failure.empty())throw std::runtime_error(state_->failure);
    state_->checkSource();*revision=state_->revision;
    if(state_->suspension) {error="Waiting for the Graphics Capture source to resume";return 0;}
    if(state_->mailbox.deliver(rgba,width,height))return 1;
    error=state_->waiting;return 0;
}
bool WindowsGraphicsCapture::paused() const {std::lock_guard lock(state_->mutex);return state_->mailbox.inputPaused()||!state_->failure.empty();}
uint64_t WindowsGraphicsCapture::revision() const {std::lock_guard lock(state_->mutex);return state_->revision;}
void WindowsGraphicsCapture::suspend() {
    std::lock_guard lock(state_->mutex);state_->suspension=true;state_->pause();state_->wake->changed.notify_one();
}
}
