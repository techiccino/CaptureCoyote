using System.IO.Compression;
using System.IO;
using System.Text.Json;
using CaptureCoyote.Core.Models;
using CaptureCoyote.Infrastructure.Helpers;
using CaptureCoyote.Services.Abstractions;

namespace CaptureCoyote.Infrastructure.Services;

public sealed class ProjectSerializationService : IProjectSerializationService
{
    private const string MetadataEntryName = "project.json";
    private const string ImageEntryName = "original.png";

    public async Task SaveAsync(string path, ScreenshotProject project, bool preserveEditableProjectPath = false, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var copy = project.Clone();
        var originalBytes = copy.OriginalImagePngBytes.ToArray();
        copy.OriginalImagePngBytes = [];
        if (!preserveEditableProjectPath)
        {
            copy.EditableProjectPath = path;
        }

        copy.ModifiedAt = DateTimeOffset.Now;

        await using var fileStream = File.Create(path);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);

        var projectEntry = archive.CreateEntry(MetadataEntryName, CompressionLevel.Optimal);
        await using (var projectStream = projectEntry.Open())
        {
            await JsonSerializer.SerializeAsync(projectStream, copy, JsonHelper.Default, cancellationToken).ConfigureAwait(false);
        }

        var imageEntry = archive.CreateEntry(ImageEntryName, CompressionLevel.NoCompression);
        await using (var imageStream = imageEntry.Open())
        {
            await imageStream.WriteAsync(originalBytes, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<ScreenshotProject> LoadAsync(string path, bool useContainerPathAsEditableProjectPath = true, CancellationToken cancellationToken = default)
    {
        await using var fileStream = File.OpenRead(path);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);
        var projectEntry = archive.GetEntry(MetadataEntryName) ?? throw new InvalidDataException("project.json was not found in the .coyote file.");
        var imageEntry = archive.GetEntry(ImageEntryName) ?? throw new InvalidDataException("original.png was not found in the .coyote file.");

        ScreenshotProject? project;
        await using (var projectStream = projectEntry.Open())
        {
            project = await JsonSerializer.DeserializeAsync<ScreenshotProject>(projectStream, JsonHelper.Default, cancellationToken).ConfigureAwait(false);
        }

        await using var imageStream = imageEntry.Open();
        using var memory = new MemoryStream();
        await imageStream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);

        if (project is null)
        {
            throw new InvalidDataException("The project payload was empty.");
        }

        project.OriginalImagePngBytes = memory.ToArray();
        if (useContainerPathAsEditableProjectPath)
        {
            project.EditableProjectPath = path;
        }

        return project;
    }
}
