#include "pch.h"
#include "NativeHost.h"
#include "DXGIManager.h"
#include "WICEncoder.h"
#include <memory>
#include <mutex>

static std::unique_ptr<DXGIManager> g_Capture;
static std::unique_ptr<WICEncoder> g_Encoder;
static std::vector<BYTE> g_Buffer;
static std::mutex g_Mutex;

int Init(int monitorIndex)
{
    std::lock_guard<std::mutex> lock(g_Mutex);
    
    g_Capture = std::make_unique<DXGIManager>();
    if (FAILED(g_Capture->Initialize(monitorIndex)))
    {
        return -1;
    }

    g_Encoder = std::make_unique<WICEncoder>();
    if (FAILED(g_Encoder->Initialize(g_Capture->GetDevice(), g_Capture->GetWidth(), g_Capture->GetHeight())))
    {
        return -2;
    }

    return 0; // Success
}

int CaptureAndEncode(int quality, BYTE** outData, int* outSize)
{
    std::lock_guard<std::mutex> lock(g_Mutex);
    
    if (!g_Capture || !g_Encoder) return -1;

    ID3D11Texture2D* texture = nullptr;
    HRESULT hr = g_Capture->CaptureFrame(&texture, 100);
    
    if (hr == DXGI_ERROR_WAIT_TIMEOUT) return 0; // Timeout
    if (FAILED(hr) || !texture) return -2; // Error

    // Encode
    hr = g_Encoder->Encode(texture, g_Capture->GetContext(), quality, g_Buffer);
    
    // Release frame immediately after usage to unblock DWM
    g_Capture->ReleaseCurrentFrame();

    if (FAILED(hr)) return -3; // Encode failed
    
    *outData = g_Buffer.data();
    *outSize = (int)g_Buffer.size();

    return 1; // Success
}

void Release()
{
    std::lock_guard<std::mutex> lock(g_Mutex);
    g_Encoder.reset();
    g_Capture.reset();
}
