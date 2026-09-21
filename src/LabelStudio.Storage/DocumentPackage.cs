using System.IO.Compression;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LabelStudio.Document;
using LabelStudio.Storage.Migrations;

namespace LabelStudio.Storage;

public sealed class DocumentPackage
{
    public const string Extension = ".label";
    public const string DocumentEntry = "document.json";
    public const string PackageEntry = "package.json";
    public const string AssetsPrefix = "assets/";
    public const string PreviewEntry = "preview.png";
    public const int MaxAssetBytes = 25 * 1024 * 1024;
    public const int MaxTotalAssetBytes = 100 * 1024 * 1024;

    private static readonly Regex AssetIdPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$",
        RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ReservedAssetIds = new(StringComparer.OrdinalIgnoreCase)
    {
        DocumentEntry,
        PackageEntry,
        PreviewEntry,
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly JsonSerializerOptions PackageMetadataOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static void Save(LabelDocument document, string path, byte[]? previewPng = null)
        => SaveContent(new DocumentPackageContent(
            document,
            new ReadOnlyDictionary<string, byte[]>(new Dictionary<string, byte[]>()),
            previewPng), path);

    public static void SaveContent(DocumentPackageContent content, string path)
    {
        if (content.Document.FormatVersion != LabelDocument.CurrentFormatVersion)
        {
            throw new InvalidOperationException(
                $"Cannot save document format version {content.Document.FormatVersion}; " +
                $"current format version is {LabelDocument.CurrentFormatVersion}.");
        }

        ValidateAssets(content.Assets);
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = path + ".tmp";
        bool completed = false;
        try
        {
            using FileStream fs = File.Create(tempPath);
            using ZipArchive archive = new(fs, ZipArchiveMode.Create, leaveOpen: false);

            ZipArchiveEntry docEntry = archive.CreateEntry(DocumentEntry);
            using (Stream docStream = docEntry.Open())
            {
                string json = DocumentJsonSerializer.Serialize(content.Document);
                using StreamWriter writer = new(docStream);
                writer.Write(json);
            }

            ZipArchiveEntry metaEntry = archive.CreateEntry(PackageEntry);
            using (Stream metaStream = metaEntry.Open())
            {
                JsonObject meta = new()
                {
                    ["packageVersion"] = 1,
                    ["createdAtUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["labelStudioVersion"] = "1.0",
                };
                using StreamWriter writer = new(metaStream);
                writer.Write(meta.ToJsonString(PackageMetadataOptions));
            }

            foreach ((string assetId, byte[] bytes) in content.Assets.OrderBy(asset => asset.Key, StringComparer.Ordinal))
            {
                ZipArchiveEntry assetEntry = archive.CreateEntry(AssetsPrefix + assetId, CompressionLevel.NoCompression);
                using Stream assetStream = assetEntry.Open();
                assetStream.Write(bytes, 0, bytes.Length);
            }

            if (content.PreviewPng is { Length: > 0 })
            {
                ZipArchiveEntry previewEntry = archive.CreateEntry(PreviewEntry, CompressionLevel.NoCompression);
                using (Stream previewStream = previewEntry.Open())
                {
                    previewStream.Write(content.PreviewPng, 0, content.PreviewPng.Length);
                }
            }

            completed = true;
        }
        finally
        {
            if (completed)
            {
                File.Move(tempPath, path, overwrite: true);
            }
            else if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    public static LabelDocument Load(string path, DocumentMigrator? migrator = null)
        => LoadContent(path, migrator).Document;

    public static DocumentPackageContent LoadContent(string path, DocumentMigrator? migrator = null)
    {
        using FileStream fs = File.OpenRead(path);
        using ZipArchive archive = new(fs, ZipArchiveMode.Read, leaveOpen: false);

        ZipArchiveEntry? docEntry = archive.GetEntry(DocumentEntry)
            ?? throw new InvalidDataException($"Package missing {DocumentEntry}.");

        string json;
        using (Stream docStream = docEntry.Open())
        using (StreamReader reader = new(docStream))
        {
            json = reader.ReadToEnd();
        }

        json = (migrator ?? new DocumentMigrator()).MigrateToCurrent(json);

        Dictionary<string, byte[]> assets = new(StringComparer.Ordinal);
        long totalAssetBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries.Where(entry =>
            entry.FullName.StartsWith(AssetsPrefix, StringComparison.Ordinal)))
        {
            string assetId = entry.FullName[AssetsPrefix.Length..];
            try
            {
                ValidateAssetId(assetId);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"Package contains invalid asset ID '{assetId}'.", exception);
            }
            if (entry.Length > MaxAssetBytes)
            {
                throw new InvalidDataException($"Asset '{assetId}' exceeds the {MaxAssetBytes} byte limit.");
            }

            totalAssetBytes += entry.Length;
            if (totalAssetBytes > MaxTotalAssetBytes)
            {
                throw new InvalidDataException($"Package assets exceed the {MaxTotalAssetBytes} byte total limit.");
            }

            if (!assets.TryAdd(assetId, ReadEntry(entry)))
            {
                throw new InvalidDataException($"Package contains duplicate asset '{assetId}'.");
            }
        }

        ZipArchiveEntry? previewEntry = archive.GetEntry(PreviewEntry);
        byte[]? previewPng = previewEntry is null ? null : ReadEntry(previewEntry);

        return new DocumentPackageContent(
            DocumentJsonSerializer.Deserialize(json),
            new ReadOnlyDictionary<string, byte[]>(assets),
            previewPng);
    }

    public static byte[]? LoadAsset(string path, string assetId)
    {
        ValidateAssetId(assetId);
        using FileStream fs = File.OpenRead(path);
        using ZipArchive archive = new(fs, ZipArchiveMode.Read, leaveOpen: false);

        ZipArchiveEntry? entry = archive.GetEntry(AssetsPrefix + assetId);
        if (entry is null) return null;
        if (entry.Length > MaxAssetBytes)
        {
            throw new InvalidDataException($"Asset '{assetId}' exceeds the {MaxAssetBytes} byte limit.");
        }

        return ReadEntry(entry);
    }

    private static void ValidateAssets(IReadOnlyDictionary<string, byte[]> assets)
    {
        long totalBytes = 0;
        foreach ((string assetId, byte[] bytes) in assets)
        {
            ValidateAssetId(assetId);
            if (bytes.Length > MaxAssetBytes)
            {
                throw new ArgumentException($"Asset '{assetId}' exceeds the {MaxAssetBytes} byte limit.", nameof(assets));
            }

            totalBytes += bytes.Length;
            if (totalBytes > MaxTotalAssetBytes)
            {
                throw new ArgumentException($"Package assets exceed the {MaxTotalAssetBytes} byte total limit.", nameof(assets));
            }
        }
    }

    private static void ValidateAssetId(string assetId)
    {
        string deviceName = Path.GetFileNameWithoutExtension(assetId);
        if (!AssetIdPattern.IsMatch(assetId) || ReservedAssetIds.Contains(assetId) || ReservedAssetIds.Contains(deviceName))
        {
            throw new ArgumentException($"Invalid asset ID '{assetId}'.", nameof(assetId));
        }
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using MemoryStream memory = new(entry.Length > int.MaxValue ? 0 : (int)entry.Length);
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
