namespace Auth.Services.Interfaces.Storage
{
    /// <summary>Outcome of a Dropbox operation. Never an exception — see <see cref="IDropboxStorage"/>.</summary>
    public class DropboxResult
    {
        public bool Success { get; init; }

        /// <summary>True when the operation was skipped because Dropbox isn't configured.</summary>
        public bool Skipped { get; init; }

        public string? Error { get; init; }

        public static DropboxResult Ok() => new() { Success = true };
        public static DropboxResult NotConfigured(string hint) => new() { Skipped = true, Error = hint };
        public static DropboxResult Fail(string error) => new() { Error = error };
    }

    /// <summary>
    /// Dropbox access for the seeders and any future export.
    ///
    /// Every method is Try-shaped and returns a result rather than throwing. That is the
    /// whole point of this interface: the previous static helper threw when its environment
    /// variables were missing, and it was called from the seeders, which run unguarded
    /// before app.Run(). A missing Dropbox credential therefore crashed the container on
    /// boot instead of skipping one optional CSV upload.
    ///
    /// Dropbox is a nice-to-have for this app — it receives generated password exports.
    /// Nothing about it should be able to stop the API from serving traffic.
    /// </summary>
    public interface IDropboxStorage
    {
        /// <summary>False when the app key, secret or refresh token is missing.</summary>
        bool IsConfigured { get; }

        /// <summary>Which variables are missing, for the health endpoint and startup log.</summary>
        string? ConfigurationHint { get; }

        Task<DropboxResult> TryUploadTextAsync(string dropboxPath, string content, CancellationToken cancellationToken = default);

        /// <summary>
        /// Downloads a file by its path inside the Dropbox app folder, authenticated.
        ///
        /// Preferred over a public share URL: share links carry an <c>st=</c> signature
        /// parameter that expires, so a hardcoded share URL silently starts returning an
        /// HTML error page after a while. A path plus the refresh token never expires.
        /// </summary>
        Task<byte[]?> TryDownloadAsync(string dropboxPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Fetches a public share URL. Kept for the seed CSVs that are still configured as
        /// links. Returns null rather than throwing so a rotated link degrades to "skip
        /// seeding" instead of "fail to start".
        /// </summary>
        Task<byte[]?> TryDownloadUrlAsync(string url, CancellationToken cancellationToken = default);
    }
}
