using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    private static readonly JsonSerializerOptions PackageMetadataOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static void Save(LabelDocument document, string path, byte[]? previewPng = null)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string tempPath = path + ".tmp";
        try
        {
            using FileStream fs = File.Create(tempPath);
            using ZipArchive archive = new(fs, ZipArchiveMode.Create, leaveOpen: false);

            ZipArchiveEntry docEntry = archive.CreateEntry(DocumentEntry);
            using (Stream docStream = docEntry.Open())
            {
                string json = DocumentJsonSerializer.Serialize(document);
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

            if (previewPng is not null && previewPng.Length > 0)
            {
                ZipArchiveEntry previewEntry = archive.CreateEntry(PreviewEntry);
                using (Stream previewStream = previewEntry.Open())
                {
                    previewStream.Write(previewPng, 0, previewPng.Length);
                }
            }
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(tempPath, path);
        }
    }

    public static LabelDocument Load(string path, DocumentMigrator? migrator = null)
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

        if (migrator is not null)
        {
            json = migrator.MigrateToCurrent(json);
        }
        else
        {
            int version = DocumentJsonSerializer.ReadFormatVersion(json);
            if (version > LabelDocument.CurrentFormatVersion)
            {
                throw new UnsupportedDocumentVersionException(version);
            }
        }

        return DocumentJsonSerializer.Deserialize(json);
    }

    public static byte[]? LoadAsset(string path, string assetId)
    {
        using FileStream fs = File.OpenRead(path);
        using ZipArchive archive = new(fs, ZipArchiveMode.Read, leaveOpen: false);

        ZipArchiveEntry? entry = archive.GetEntry(AssetsPrefix + assetId);
        if (entry is null) return null;

        using Stream stream = entry.Open();
        using MemoryStream ms = new();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}