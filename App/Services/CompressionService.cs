using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Remotier.Models;

namespace Remotier.Services;

public class CompressionService
{
    private ImageCodecInfo _jpegEncoder;
    private EncoderParameters _encoderParams;
    private bool _enableScaling;
    private int _scaleWidth;
    private int _scaleHeight;

    public CompressionService(StreamOptions options)
    {
        _jpegEncoder = GetEncoder(ImageFormat.Jpeg);
        _encoderParams = new EncoderParameters(1);
        _encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)options.Quality);

        _enableScaling = options.EnableScaling;
        _scaleWidth = options.ScaleWidth;
        _scaleHeight = options.ScaleHeight;
    }

    public void SetQuality(long quality)
    {
        _encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
    }

    public void SetScaling(bool enable, int width, int height)
    {
        _enableScaling = enable;
        _scaleWidth = width;
        _scaleHeight = height;
    }

    public byte[] Compress(Bitmap bitmap)
    {
        if (bitmap == null) return null;

        using (var ms = new MemoryStream())
        {
            if (_enableScaling)
            {
                // Optimization: Use Graphics for faster scaling (NearestNeighbor or Low)
                // The default 'new Bitmap(source, width, height)' uses HighQualityBicubic which is very slow
                using (var resized = new Bitmap(_scaleWidth, _scaleHeight))
                {
                    using (var g = Graphics.FromImage(resized))
                    {
                        // Use low quality for speed
                        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighSpeed;

                        g.DrawImage(bitmap, 0, 0, _scaleWidth, _scaleHeight);
                    }
                    resized.Save(ms, _jpegEncoder, _encoderParams);
                }
            }
            else
            {
                bitmap.Save(ms, _jpegEncoder, _encoderParams);
            }
            return ms.ToArray();
        }
    }

    private ImageCodecInfo GetEncoder(ImageFormat format)
    {
        ImageCodecInfo[] codecs = ImageCodecInfo.GetImageDecoders();
        foreach (ImageCodecInfo codec in codecs)
        {
            if (codec.FormatID == format.Guid)
            {
                return codec;
            }
        }
        return null; // Should handle this better
    }

    public static Bitmap Decompress(byte[] data)
    {
        if (data == null || data.Length == 0) return null;

        using (var ms = new MemoryStream(data))
        {
            using (var temp = new Bitmap(ms))
            {
                return new Bitmap(temp);
            }
        }
    }
}
