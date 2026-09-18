using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ql800Spike.Core.Media;

public sealed class MediaCatalog
{
    private const string ResourceName = "Ql800Spike.Core.Media.media-profiles.json";
    private readonly IReadOnlyList<MediaProfile> profiles;

    private MediaCatalog(IReadOnlyList<MediaProfile> profiles)
    {
        this.profiles = profiles;
    }

    public IReadOnlyList<MediaProfile> Profiles => profiles;

    public static MediaCatalog LoadBuiltIn()
    {
        using Stream stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded media resource '{ResourceName}' was not found.");

        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        MediaProfile[] loaded = JsonSerializer.Deserialize<MediaProfile[]>(stream, options)
            ?? throw new InvalidDataException("The built-in media catalog is empty.");

        Validate(loaded);
        return new MediaCatalog(loaded);
    }

    public MediaProfile GetRequired(string profileId)
    {
        return profiles.FirstOrDefault(
                profile => string.Equals(profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Unknown media profile '{profileId}'.");
    }

    public MediaIdentification Identify(MediaStatusSignature signature)
    {
        MediaProfile[] matches = profiles
            .Where(profile =>
                profile.BrotherQl.StatusMediaType == signature.MediaType &&
                profile.BrotherQl.ProtocolWidthMillimetres == signature.WidthMillimetres &&
                profile.BrotherQl.ProtocolLengthMillimetres == signature.LengthMillimetres)
            .ToArray();

        string explanation = matches.Length == 0
            ? "No built-in profile matches the reported Brother media geometry."
            : "Brother status reports media kind and nominal dimensions, not the DK SKU or ink capability.";

        return new MediaIdentification(signature, matches, false, explanation);
    }

    private static void Validate(IReadOnlyCollection<MediaProfile> loaded)
    {
        if (loaded.Count == 0)
        {
            throw new InvalidDataException("At least one media profile is required.");
        }

        string[] duplicates = loaded
            .GroupBy(profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new InvalidDataException($"Duplicate media profile IDs: {string.Join(", ", duplicates)}.");
        }

        foreach (MediaProfile profile in loaded)
        {
            BrotherQlMediaMapping mapping = profile.BrotherQl;
            if (mapping.HeadLeftBlankDots + mapping.PrintableWidthDots + mapping.HeadRightBlankDots != 720)
            {
                throw new InvalidDataException(
                    $"Media '{profile.ProfileId}' does not span the QL-800's 720-dot head.");
            }

            if (profile.Kind == MediaKind.DieCut && mapping.PrintableLengthDots is null)
            {
                throw new InvalidDataException(
                    $"Die-cut media '{profile.ProfileId}' requires a fixed printable length.");
            }
        }
    }
}
