namespace Ql800Spike.Core.Media;

public readonly record struct MediaStatusSignature(
    byte MediaType,
    byte WidthMillimetres,
    byte LengthMillimetres);

public sealed record MediaIdentification(
    MediaStatusSignature Signature,
    IReadOnlyList<MediaProfile> CompatibleProfiles,
    bool IsExactSku,
    string Explanation);
