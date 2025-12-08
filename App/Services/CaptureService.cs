using System.Drawing;
using System.Drawing.Imaging;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace Remotier.Services;

public class CaptureService : IDisposable
{
    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private IDXGIOutputDuplication _duplication = null!;
    private ID3D11Texture2D? _stagingTexture;

    public int ScreenWidth { get; private set; }
    public int ScreenHeight { get; private set; }

    public void Initialize()
    {
        D3D11CreateDevice(null, DriverType.Hardware, DeviceCreationFlags.VideoSupport,
            new[] { FeatureLevel.Level_11_0 }, out _device, out _context);

        using var factory = CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out var adapter);
        adapter.EnumOutputs(0, out var output);
        using var output1 = output.QueryInterface<IDXGIOutput1>();

        ScreenWidth = output.Description.DesktopCoordinates.Right - output.Description.DesktopCoordinates.Left;
        ScreenHeight = output.Description.DesktopCoordinates.Bottom - output.Description.DesktopCoordinates.Top;

        _duplication = output1.DuplicateOutput(_device);
    }

    public Bitmap CaptureFrame()
    {
        bool frameAcquired = false;
        try
        {
            var result = _duplication.AcquireNextFrame(100, out var frameInfo, out var desktopResource);
            if (result.Failure)
            {
                // Timeout is normal, other failures mean something is wrong
                return null;
            }

            frameAcquired = true;

            if (desktopResource == null)
            {
                return null;
            }

            using var texture = desktopResource.QueryInterface<ID3D11Texture2D>();
            if (texture == null)
            {
                return null;
            }

            // Create staging texture if needed
            if (_stagingTexture == null)
            {
                var desc = texture.Description;
                desc.Usage = ResourceUsage.Staging;
                desc.BindFlags = BindFlags.None;
                desc.CPUAccessFlags = CpuAccessFlags.Read;
                desc.MiscFlags = ResourceOptionFlags.None;
                _stagingTexture = _device.CreateTexture2D(desc);
            }

            if (_stagingTexture == null) return null;

            _context.CopyResource(_stagingTexture, texture);

            // Release frame immediately after copy to allow next frame to be ready
            _duplication.ReleaseFrame();
            frameAcquired = false;

            var map = _context.Map(_stagingTexture, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

            try
            {
                // Create bitmap from map.Data
                var bitmap = new Bitmap(ScreenWidth, ScreenHeight, PixelFormat.Format32bppArgb);
                var bounds = new Rectangle(0, 0, ScreenWidth, ScreenHeight);
                var mapData = bitmap.LockBits(bounds, ImageLockMode.WriteOnly, bitmap.PixelFormat);

                // Copy line by line
                unsafe
                {
                    byte* source = (byte*)map.DataPointer;
                    byte* dest = (byte*)mapData.Scan0;
                    for (int y = 0; y < ScreenHeight; y++)
                    {
                        Buffer.MemoryCopy(source, dest, mapData.Stride, ScreenWidth * 4);
                        source += map.RowPitch;
                        dest += mapData.Stride;
                    }
                }

                bitmap.UnlockBits(mapData);
                return bitmap;
            }
            finally
            {
                _context.Unmap(_stagingTexture, 0);
            }
        }
        catch (Exception ex)
        {
            // Log if needed
            // Debug.WriteLine(ex);
            return null;
        }
        finally
        {
            if (frameAcquired)
            {
                try { _duplication.ReleaseFrame(); } catch { }
            }
        }
    }

    public void Dispose()
    {
        _stagingTexture?.Dispose();
        _duplication?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
    }
}
