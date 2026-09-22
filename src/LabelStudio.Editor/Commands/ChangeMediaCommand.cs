using LabelStudio.Document;
using LabelStudio.Document.Units;

namespace LabelStudio.Editor.Commands;

public sealed class ChangeMediaCommand : IEditorCommand
{
    private readonly string _oldProfileId;
    private readonly PhysicalSize _oldPageDimensions;
    private readonly DocumentMediaKind _oldMediaKind;
    private readonly MediaSnapshot _oldMediaGeometry;
    private readonly string _newProfileId;
    private readonly PhysicalSize _newPageDimensions;
    private readonly DocumentMediaKind _newMediaKind;
    private readonly MediaSnapshot _newMediaGeometry;

    public ChangeMediaCommand(
        LabelDocument document,
        string newProfileId,
        PhysicalSize newPageDimensions,
        MediaSnapshot newMediaGeometry,
        DocumentMediaKind newMediaKind = DocumentMediaKind.DieCut)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(newProfileId);
        ArgumentNullException.ThrowIfNull(newMediaGeometry);

        if (!newProfileId.Equals(newMediaGeometry.ProfileId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The media profile and geometry profile must match.", nameof(newMediaGeometry));
        }

        _oldProfileId = document.MediaProfileId;
        _oldPageDimensions = document.PageDimensions;
        _oldMediaKind = document.MediaKind;
        _oldMediaGeometry = document.MediaGeometry;
        _newProfileId = newProfileId;
        _newPageDimensions = newPageDimensions;
        _newMediaKind = newMediaKind;
        _newMediaGeometry = newMediaGeometry;
    }

    public string Description => $"Change roll to '{_newProfileId}'";

    public LabelDocument Execute(LabelDocument document) => document with
    {
        MediaProfileId = _newProfileId,
        PageDimensions = _newPageDimensions,
        MediaKind = _newMediaKind,
        MediaGeometry = _newMediaGeometry,
    };

    public LabelDocument Undo(LabelDocument document) => document with
    {
        MediaProfileId = _oldProfileId,
        PageDimensions = _oldPageDimensions,
        MediaKind = _oldMediaKind,
        MediaGeometry = _oldMediaGeometry,
    };
}
