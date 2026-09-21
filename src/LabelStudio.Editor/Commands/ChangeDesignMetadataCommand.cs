using LabelStudio.Document;

namespace LabelStudio.Editor.Commands;

public sealed class ChangeDesignMetadataCommand : IEditorCommand
{
    private readonly DocumentDesignMetadata _before;
    private readonly DocumentDesignMetadata _after;

    public ChangeDesignMetadataCommand(DocumentDesignMetadata before, DocumentDesignMetadata after)
    {
        _before = before;
        _after = after;
    }

    public string Description => "Change design metadata";

    public LabelDocument Execute(LabelDocument document) =>
        document with { DesignMetadata = _after };

    public LabelDocument Undo(LabelDocument document) =>
        document with { DesignMetadata = _before };
}