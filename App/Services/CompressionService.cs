using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Remotier.Services;

public class CompressionService
{
    private ImageCodecInfo _jpegEncoder;
    private EncoderParameters _encoderParams;

    public CompressionService(long quality)
    {
        _jpegEncoder = GetEncoder(ImageFormat.Jpeg);
        _encoderParams = new EncoderParameters(1);
        _encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
    }

    public void SetQuality(long quality)
    {
        _encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
    }

    public byte[] Compress(Bitmap bitmap)
    {
        if (bitmap == null) return null;

        using (var ms = new MemoryStream())
        {
            bitmap.Save(ms, _jpegEncoder, _encoderParams);
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
        return null;
    }

    public static Bitmap Decompress(byte[] data)
    {
        if (data == null || data.Length == 0) return null;

        using (var ms = new MemoryStream(data))
        {
            return new Bitmap(ms);
        }
    }
}
