using System.IO;
using System.Text.Json;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.Services;

public static class PageStore
{
    private const long MaxFileBytes = 64 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static async Task SaveAsync(string path, PageDocument page)
    {
        page.Validate();
        // A same-directory temporary file keeps replacement on the same filesystem.
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, page, Options);
                if (stream.Length > MaxFileBytes)
                    throw new InvalidDataException("Page exceeds the prototype's 64 MB limit.");
                await stream.FlushAsync();
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static async Task<PageDocument> LoadAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        if (stream.Length > MaxFileBytes)
            throw new InvalidDataException("Page exceeds the prototype's 64 MB limit.");
        var page = await JsonSerializer.DeserializeAsync<PageDocument>(stream, Options)
            ?? throw new InvalidDataException("The page is empty.");
        page.Validate();
        return page;
    }
}
