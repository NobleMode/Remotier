#include "pch.h"
#include "WICEncoder.h"
#include <wincodec.h>

WICEncoder::WICEncoder() : m_width(0), m_height(0)
{
    CoInitialize(nullptr);
}

WICEncoder::~WICEncoder()
{
    CoUninitialize();
}

HRESULT WICEncoder::Initialize(ID3D11Device* device, int width, int height)
{
    m_width = width;
    m_height = height;

    HRESULT hr = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&m_factory));
    if (FAILED(hr)) return hr;

    // Create Staging Texture for CPU access
    D3D11_TEXTURE2D_DESC desc = {};
    desc.Width = width;
    desc.Height = height;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_STAGING;
    desc.BindFlags = 0;
    desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    
    hr = device->CreateTexture2D(&desc, nullptr, &m_stagingTexture);
    return hr;
}

HRESULT WICEncoder::Encode(ID3D11Texture2D* texture, ID3D11DeviceContext* context, int quality, std::vector<BYTE>& outData)
{
    if (!m_stagingTexture || !m_factory) return E_FAIL;

    // Copy to staging
    context->CopyResource(m_stagingTexture.Get(), texture);

    // Map
    D3D11_MAPPED_SUBRESOURCE map;
    HRESULT hr = context->Map(m_stagingTexture.Get(), 0, D3D11_MAP_READ, 0, &map);
    if (FAILED(hr)) return hr;

    // Encode
    ComPtr<IStream> stream;
    hr = CreateStreamOnHGlobal(nullptr, TRUE, &stream);
    
    ComPtr<IWICBitmapEncoder> encoder;
    if (SUCCEEDED(hr)) hr = m_factory->CreateEncoder(GUID_ContainerFormatJpeg, nullptr, &encoder);
    if (SUCCEEDED(hr)) hr = encoder->Initialize(stream.Get(), WICBitmapEncoderNoCache);

    ComPtr<IWICBitmapFrameEncode> frame;
    ComPtr<IPropertyBag2> props;
    if (SUCCEEDED(hr)) hr = encoder->CreateNewFrame(&frame, &props);

    if (SUCCEEDED(hr))
    {
        // Set Quality
        PROPBAG2 option = { 0 };
        option.pstrName = (LPOLESTR)L"ImageQuality";
        VARIANT varValue;    
        VariantInit(&varValue);
        varValue.vt = VT_R4;
        varValue.fltVal = (float)quality / 100.0f;      
        props->Write(1, &option, &varValue);

        hr = frame->Initialize(props.Get());
    }

    if (SUCCEEDED(hr))
    {
        hr = frame->SetSize(m_width, m_height);
        WICPixelFormatGUID format = GUID_WICPixelFormat32bppBGRA;
        if (SUCCEEDED(hr)) hr = frame->SetPixelFormat(&format);
        
        if (SUCCEEDED(hr))
        {
            // Write pixels
            hr = frame->WritePixels(m_height, map.RowPitch, map.RowPitch * m_height, (BYTE*)map.pData);
        }
        
        if (SUCCEEDED(hr)) hr = frame->Commit();
        if (SUCCEEDED(hr)) hr = encoder->Commit();
    }

    context->Unmap(m_stagingTexture.Get(), 0);

    if (SUCCEEDED(hr))
    {
        // Get data
        STATSTG stats;
        stream->Stat(&stats, STATFLAG_NONAME);
        ULONG size = (ULONG)stats.cbSize.QuadPart;
        
        outData.resize(size);
        LARGE_INTEGER seekPos = { 0 };
        stream->Seek(seekPos, STREAM_SEEK_SET, nullptr);
        ULONG bytesRead;
        stream->Read(outData.data(), size, &bytesRead);
    }

    return hr;
}
