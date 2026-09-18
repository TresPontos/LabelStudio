using LabelStudio.Document;
using LabelStudio.Document.Elements;
using LabelStudio.Document.Ink;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Commands;

public sealed class ChangePropertyCommand : IEditorCommand
{
    private readonly string _elementId;
    private readonly string _propertyName;
    private readonly object _oldValue;
    private readonly object _newValue;

    public ChangePropertyCommand(string elementId, string propertyName, object oldValue, object newValue)
    {
        _elementId = elementId;
        _propertyName = propertyName;
        _oldValue = oldValue;
        _newValue = newValue;
    }

    public string Description => $"Change {_propertyName} on '{_elementId}'";

    public LabelDocument Execute(LabelDocument document) =>
        document.WithElements(document.Elements.Select(e => ApplyValue(e, _newValue)).ToList());

    public LabelDocument Undo(LabelDocument document) =>
        document.WithElements(document.Elements.Select(e => ApplyValue(e, _oldValue)).ToList());

    private DocumentElement ApplyValue(DocumentElement element, object value)
    {
        if (element.Id != _elementId) return element;

        return (_propertyName, element) switch
        {
            ("ink", RectangleElement r) => r with { Ink = (InkChannel)value },
            ("ink", LineElement l) => l with { Ink = (InkChannel)value },
            ("ink", ImageElement i) => i with { Ink = (InkChannel)value },
            ("ink", TextElement t) => t with { Ink = (InkChannel)value },
            ("fill", RectangleElement r) => r with { Fill = (bool)value },
            ("strokeWidth", RectangleElement r) => r with { StrokeWidth = (Micrometre)value },
            ("thickness", LineElement l) => l with { Thickness = (Micrometre)value },
            ("text", TextElement t) => t with { Text = (string)value },
            ("fontSizePoints", TextElement t) => t with { FontSizePoints = (int)value },
            ("fontFamily", TextElement t) => t with { FontFamily = (string?)value },
            ("assetId", ImageElement i) => i with { AssetId = (string)value },
            _ => element,
        };
    }
}