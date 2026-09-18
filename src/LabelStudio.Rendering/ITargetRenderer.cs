namespace LabelStudio.Rendering;

public interface ITargetRenderer
{
    RenderedPlanes Render(LabelStudio.Layout.PreparedScene scene, RenderTarget target);
}