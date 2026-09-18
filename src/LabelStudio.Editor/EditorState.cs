using LabelStudio.Document;
using LabelStudio.Editor.Commands;
using LabelStudio.Editor.Selection;

namespace LabelStudio.Editor;

public enum EditorTool
{
    Select,
    Text,
    Rectangle,
    Line,
    Image,
    Pan,
}

public sealed class EditorState
{
    public DocumentSession Session { get; }
    public SelectionModel Selection { get; } = new();
    public DragTransaction Drag { get; } = new();
    public EditorTool ActiveTool { get; set; } = EditorTool.Select;
    public CanvasTransform ViewTransform { get; } = new();

    public const double ScreenHitToleranceDip = 7.0;
    public const int NudgeSmallMicrometres = 100;
    public const int NudgeLargeMicrometres = 1000;

    public EditorState(DocumentSession session)
    {
        Session = session;
    }

    public EditorState(LabelDocument document) : this(new DocumentSession(document))
    {
    }
}