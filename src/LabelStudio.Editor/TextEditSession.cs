using LabelStudio.Document.Elements;
using LabelStudio.Editor.Commands;

namespace LabelStudio.Editor;

public sealed class TextEditSession
{
    private readonly DocumentSession _documentSession;
    private readonly Func<TextElement, TextElement> _resolveFrame;
    private TextElement? _originalElement;

    public string? ElementId { get; private set; }
    public string OriginalText { get; private set; } = string.Empty;
    public string CurrentText { get; private set; } = string.Empty;
    public bool IsActive => ElementId is not null;

    public TextEditSession(
        DocumentSession documentSession,
        Func<TextElement, TextElement>? resolveFrame = null)
    {
        _documentSession = documentSession;
        _resolveFrame = resolveFrame ?? (element => element);
    }

    public void Begin(string elementId)
    {
        TextElement element = _documentSession.Document.Elements
            .OfType<TextElement>()
            .Single(element => string.Equals(element.Id, elementId, StringComparison.Ordinal));

        ElementId = element.Id;
        _originalElement = element;
        OriginalText = element.Text;
        CurrentText = element.Text;
    }

    public void Update(string text)
    {
        if (ElementId is null)
        {
            throw new InvalidOperationException("No text edit session is active.");
        }

        if (string.Equals(CurrentText, text, StringComparison.Ordinal)) return;

        CurrentText = text;
        string elementId = ElementId;
        _documentSession.UpdateTransient(document => document.WithElements(
            document.Elements.Select(element =>
                element is TextElement current && string.Equals(current.Id, elementId, StringComparison.Ordinal)
                    ? _resolveFrame(current with { Text = text })
                    : element).ToList()));
        _documentSession.SetTransientDirty(!string.Equals(CurrentText, OriginalText, StringComparison.Ordinal));
    }

    public void Commit()
    {
        if (ElementId is null) return;

        string elementId = ElementId;
        TextElement? originalElement = _originalElement;
        TextElement? currentElement = _documentSession.Document.Elements
            .OfType<TextElement>()
            .FirstOrDefault(element => string.Equals(element.Id, elementId, StringComparison.Ordinal));
        Reset();
        _documentSession.SetTransientDirty(false);

        if (originalElement is not null && currentElement is not null && originalElement != currentElement)
        {
            _documentSession.ExecuteCommand(new ReplaceElementsCommand([(originalElement, currentElement)]));
        }
    }

    public void Cancel()
    {
        if (ElementId is null) return;

        string elementId = ElementId;
        TextElement? originalElement = _originalElement;
        Reset();
        if (originalElement is not null)
        {
            _documentSession.UpdateTransient(document => document.WithElements(
                document.Elements.Select(element =>
                    element is TextElement current && string.Equals(current.Id, elementId, StringComparison.Ordinal)
                        ? originalElement
                        : element).ToList()));
        }
        _documentSession.SetTransientDirty(false);
    }

    private void Reset()
    {
        ElementId = null;
        OriginalText = string.Empty;
        CurrentText = string.Empty;
        _originalElement = null;
    }
}
