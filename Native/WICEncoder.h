#pragma once
#include <wincodec.h>
#include <d3d11.h>
#include <wrl/client.h>
#include <vector>

using namespace Microsoft::WRL;

class WICEncoder
{
public:
    WICEncoder();
    ~WICEncoder();

    HRESULT Initialize(ID3D11Device* device, int width, int height);
    HRESULT Encode(ID3D11Texture2D* texture, ID3D11DeviceContext* context, int scalePercent, int quality, std::vector<BYTE>& outData);

private:
    ComPtr<IWICImagingFactory> m_factory;
    ComPtr<ID3D11Texture2D> m_stagingTexture;
    int m_width;
    int m_height;
};
