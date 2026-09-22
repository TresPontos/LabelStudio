using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LabelStudio.Rendering;
using SkiaSharp;

namespace LabelStudio.Desktop;

public partial class ThermalPreviewWindow : Window
{
    private readonly RenderedPlanes _planes;

    public ThermalPreviewWindow(RenderedPlanes planes)
    {
        ArgumentNullException.ThrowIfNull(planes);
        _planes = planes;
        InitializeComponent();
        RedRadio.Visibility = planes.RedPlane is not null ? Visibility.Visible : Visibility.Collapsed;
        UpdatePreview();
    }

    private void OnPlaneChanged(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        MonochromeRaster? raster = BlackRadio.IsChecked == true ? _planes.BlackPlane : _planes.RedPlane;
        if (raster is null) return;

        using SKBitmap bitmap = RasterToBitmap(raster);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);

        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = new System.IO.MemoryStream(data.ToArray());
        bitmapImage.EndInit();
        bitmapImage.Freeze();
        PreviewImage.Source = bitmapImage;
    }

    private static SKBitmap RasterToBitmap(MonochromeRaster raster)
    {
        SKBitmap bmp = new(new SKImageInfo(raster.Width, raster.Height, SKColorType.Rgba8888));
        ReadOnlySpan<byte> src = raster.Data.Span;

        for (int y = 0; y < raster.Height; y++)
        {
            for (int x = 0; x < raster.Width; x++)
            {
                bool set = (src[y * raster.BytesPerRow + x / 8] & (0x80 >> (x % 8))) != 0;
                bmp.SetPixel(x, y, set ? SKColors.Black : SKColors.White);
            }
        }

        return bmp;
    }
}
