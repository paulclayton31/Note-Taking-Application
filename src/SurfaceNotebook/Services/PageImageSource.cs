using System.IO;
using System.Windows.Media.Imaging;

namespace SurfaceNotebook.Services;

public static class PageImageSource
{
    public static BitmapSource Decode(byte[] png)
    {
        using var stream = new MemoryStream(png, writable: false);
        var image = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        image.Freeze(); return image;
    }
}
