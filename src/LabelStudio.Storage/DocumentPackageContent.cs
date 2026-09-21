using LabelStudio.Document;

namespace LabelStudio.Storage;

public sealed record DocumentPackageContent(
    LabelDocument Document,
    IReadOnlyDictionary<string, byte[]> Assets,
    byte[]? PreviewPng);
