#pragma once
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>
#include <vector>

using namespace Microsoft::WRL;

class DXGIManager
{
public:
    DXGIManager();
    ~DXGIManager();

    HRESULT Initialize(int monitorIndex);
    HRESULT CaptureFrame(ID3D11Texture2D** ppTexture, int timeoutMs);
    void ReleaseCurrentFrame();
    void Release();

    int GetWidth() const { return m_width; }
    int GetHeight() const { return m_height; }
    ID3D11Device* GetDevice() const { return m_device.Get(); }
    ID3D11DeviceContext* GetContext() const { return m_context.Get(); }

private:
    ComPtr<ID3D11Device> m_device;
    ComPtr<ID3D11DeviceContext> m_context;
    ComPtr<IDXGIOutputDuplication> m_duplication;
    
    int m_width;
    int m_height;
};
