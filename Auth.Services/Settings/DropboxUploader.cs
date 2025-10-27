using Dropbox.Api;
using Dropbox.Api.Files;

public static class DropboxUploader
{
    public static async Task UploadTextAsync(string dropboxPath, string content)
    {
        var token = Environment.GetEnvironmentVariable("DROPBOX_ACCESS_TOKEN");
        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("Missing DROPBOX_ACCESS_TOKEN environment variable");

        using var dbx = new DropboxClient(token);
        using var mem = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content));
        await dbx.Files.UploadAsync(
            dropboxPath,
            WriteMode.Overwrite.Instance,
            body: mem
        );
    }
}

