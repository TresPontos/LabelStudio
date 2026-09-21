namespace LabelStudio.Editor.Selection;

public abstract record SelectionTarget
{
    public abstract string Id { get; }

    public sealed record ElementTarget(string ElementId) : SelectionTarget
    {
        public override string Id => ElementId;
    }

    public sealed record GroupTarget(string GroupId) : SelectionTarget
    {
        public override string Id => GroupId;
    }

    public static SelectionTarget Element(string elementId) => new ElementTarget(elementId);
    public static SelectionTarget Group(string groupId) => new GroupTarget(groupId);
}