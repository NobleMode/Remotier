namespace Remotier.Models;

public class StreamOptions
{
    public int Quality { get; set; } = 75; // 1–100 jpeg quality
    public int Framerate { get; set; } = 60;
    public bool EnableScaling { get; set; } = false;
    public int ScaleWidth { get; set; } = 1920;
    public int ScaleHeight { get; set; } = 1080;
}
