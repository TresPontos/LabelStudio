namespace LabelStudio.Document;

public sealed record DocumentId(Guid Value)
{
    public static DocumentId New() => new(Guid.NewGuid());

    public static DocumentId Parse(string text) => new(Guid.Parse(text));

    public override string ToString() => Value.ToString("D");
}