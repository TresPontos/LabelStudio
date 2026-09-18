using LabelStudio.Document;
using LabelStudio.Layout;
using LabelStudio.Rendering;

namespace LabelStudio.Printing;

public sealed record PrintIntent(
    DocumentId DocumentId,
    PreparedScene Scene,
    MediaProfile Media,
    PrintSettings Settings)
{
    public RenderTarget CreateRenderTarget() =>
        new(
            Settings.Dpi,
            Settings.Dpi,
            0,
            0,
            Document.Units.MicrometreRect.Zero,
            0,
            0,
            CreateInkChannels(Media));

    private static IReadOnlyList<InkOutputChannel> CreateInkChannels(MediaProfile media)
    {
        List<InkOutputChannel> channels = [new("Black", false)];
        if (media.SupportsRed)
        {
            channels.Add(new("Red", true));
        }
        return channels;
    }
}