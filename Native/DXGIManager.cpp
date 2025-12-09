#include "pch.h"
#include "DXGIManager.h"

DXGIManager::DXGIManager() : m_width(0), m_height(0)
{
}

DXGIManager::~DXGIManager()
{
    Release();
}

HRESULT DXGIManager::Initialize(int monitorIndex)
{
    HRESULT hr = S_OK;

    // Create Device and Context
    D3D_FEATURE_LEVEL featureLevels[] = { D3D_FEATURE_LEVEL_11_0 };
    UINT numFeatureLevels = ARRAYSIZE(featureLevels);
    D3D_FEATURE_LEVEL featureLevel;

    hr = D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, featureLevels, numFeatureLevels, 
                           D3D11_SDK_VERSION, &m_device, &featureLevel, &m_context);
    if (FAILED(hr)) return hr;

    // Get DXGI Factory
    ComPtr<IDXGIDevice> dxgiDevice;
    hr = m_device.As(&dxgiDevice);
    if (FAILED(hr)) return hr;

    ComPtr<IDXGIAdapter> dxgiAdapter;
    hr = dxgiDevice->GetAdapter(&dxgiAdapter);
    if (FAILED(hr)) return hr;

    // Get Output
    ComPtr<IDXGIOutput> dxgiOutput;
    hr = dxgiAdapter->EnumOutputs(monitorIndex, &dxgiOutput);
    if (FAILED(hr))
    {
        // Fallback to primary if index invalid
        dxgiAdapter->EnumOutputs(0, &dxgiOutput);
    }

    if (!dxgiOutput) return E_FAIL;

    ComPtr<IDXGIOutput1> dxgiOutput1;
    hr = dxgiOutput->QueryInterface(__uuidof(IDXGIOutput1), (void**)&dxgiOutput1);
    if (FAILED(hr)) return hr;

    // Duplicate Output
    hr = dxgiOutput1->DuplicateOutput(m_device.Get(), &m_duplication);
    if (FAILED(hr)) return hr;

    // Get Desc
    DXGI_OUTPUT_DESC desc;
    dxgiOutput->GetDesc(&desc);
    m_width = desc.DesktopCoordinates.right - desc.DesktopCoordinates.left;
    m_height = desc.DesktopCoordinates.bottom - desc.DesktopCoordinates.top;

    return S_OK;
}

HRESULT DXGIManager::CaptureFrame(ID3D11Texture2D** ppTexture, int timeoutMs)
{
    if (!m_duplication) return E_POINTER;

    DXGI_OUTDUPL_FRAME_INFO frameInfo;
    ComPtr<IDXGIResource> desktopResource;
    HRESULT hr = m_duplication->AcquireNextFrame(timeoutMs, &frameInfo, &desktopResource);

    if (hr == DXGI_ERROR_WAIT_TIMEOUT)
    {
        return hr;
    }
    if (FAILED(hr))
    {
        // Try to release frame just in case
        m_duplication->ReleaseFrame();
        return hr;
    }

    hr = desktopResource->QueryInterface(__uuidof(ID3D11Texture2D), (void**)ppTexture);
    
    // We must release the frame *after* copying or processing, but since we are returning the texture pointer directly 
    // and it's a D3D texture from the duplication service, we actually need to COPY it if we want to hold it, 
    // OR we process it immediately.
    // However, AcquireNextFrame gives us access to the desktop image in GPU memory. 
    // IMPORTANT: We must call ReleaseFrame before the next Acquire. 
    // The efficient way is: Capture -> Process/Copy -> Release.
    // But here we return the texture. The caller MUST NOT ReleaseFrame, WE must do it.
    // Better pattern: Copty to staging or shared texture here? 
    // NATIVE HOST will coordinate: Capture -> Encode -> ReleaseFrame.
    // So returning the texture is fine as long as caller calls ReleaseFrame on duplication? 
    // Actually, duplication is member of this class.
    // Let's keep it simple: ReleaseFrame needs to be called by this class. 
    // The caller gets a texture, uses it, then calls "ReleaseFrame" on this manager?
    // OR, we just copy to a staging texture here and release immediately. Safeguards global state.
    
    // DECISION: Copy to valid texture and Release immediately to prevent blocking DWM.
    // ... Actually, WIC Encode works on the GPU texture? Only if it's staging or readable?
    // WIC usually needs CPU access or specific Direct2D interop. 
    // TurboJPEG needs CPU access.
    // If we use WIC with CPU access, we need a staging texture.
    
    // Let's create a staging texture if needed or just return the resource and let NativeHost handle release?
    // To separate concerns: CaptureFrame returns the raw resource.
    // And we add a "DoneWithFrame" method.
    
    return hr;
}

void DXGIManager::ReleaseCurrentFrame()
{
    if (m_duplication)
    {
        m_duplication->ReleaseFrame();
    }
}

void DXGIManager::Release()
{
    if (m_duplication)
    {
        m_duplication->ReleaseFrame(); // Just in case
        m_duplication.Reset();
    }
    m_context.Reset();
    m_device.Reset();
}
