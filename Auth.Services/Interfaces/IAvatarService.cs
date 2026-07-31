using Auth.Models.DTOs.Account;
using Microsoft.AspNetCore.Http;

namespace Auth.Services.Interfaces
{
    /// <summary>
    /// Profile pictures — upload, read, remove.
    ///
    /// Separate from <see cref="IAccountService"/> even though both are self-service, because
    /// the two have almost nothing in common at the implementation level: this one is an image
    /// pipeline whose entire job is to distrust its input, and it is the only place in the
    /// system that turns bytes a user supplied into bytes the system serves back to other
    /// users. Keeping that in one file makes the validation reviewable as a unit.
    /// </summary>
    public interface IAvatarService
    {
        /// <summary>
        /// Validates, re-encodes and stores a new picture for this account, replacing any
        /// existing one.
        ///
        /// Returns nothing on purpose. The caller wants the refreshed
        /// <c>AccountOverviewDto</c>, which is <see cref="IAccountService"/>'s job — and
        /// returning the stored image here would push several kilobytes of bytes back through
        /// a caller that only ever wanted to know the upload succeeded.
        /// </summary>
        /// <exception cref="Models.Exceptions.ValidationException">
        /// The file is empty, over the size limit, or is not something that decodes as an image.
        /// </exception>
        Task UploadAsync(string userId, IFormFile file, CancellationToken cancellationToken = default);

        /// <summary>
        /// The stored image for a user, or null when they have not uploaded one.
        ///
        /// Null rather than an exception: "this person has no picture" is the ordinary case for
        /// most of the roster, not a failure, and the caller answers it with a 404 that the
        /// client already knows how to fall back from.
        /// </summary>
        Task<AvatarImageDto?> GetAsync(string userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes this account's picture. Idempotent — removing one that is not there is a
        /// success, because the caller's intent ("I should have no avatar") is satisfied either
        /// way and a double-click should not produce an error.
        /// </summary>
        Task DeleteAsync(string userId, CancellationToken cancellationToken = default);
    }
}
