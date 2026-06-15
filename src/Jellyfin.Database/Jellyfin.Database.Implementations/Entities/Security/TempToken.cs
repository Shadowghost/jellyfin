using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jellyfin.Database.Implementations.Entities.Security
{
    /// <summary>
    /// An entity recording an issued short-lived temp token, for listing and revocation.
    /// </summary>
    public class TempToken
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TempToken"/> class.
        /// </summary>
        /// <param name="jti">The unique token id (jti).</param>
        /// <param name="actingUserId">The id of the user the token acts on behalf of.</param>
        /// <param name="issuedAt">When the token was issued.</param>
        /// <param name="expiresAt">When the token expires.</param>
        /// <param name="scopes">The space-delimited scopes granted to the token.</param>
        /// <param name="label">A human-readable label.</param>
        public TempToken(string jti, Guid actingUserId, DateTime issuedAt, DateTime expiresAt, string scopes, string label)
        {
            Jti = jti;
            ActingUserId = actingUserId;
            IssuedAt = issuedAt;
            ExpiresAt = expiresAt;
            Scopes = scopes;
            Label = label;
        }

        /// <summary>
        /// Gets the id.
        /// </summary>
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; private set; }

        /// <summary>
        /// Gets or sets the unique token id (jti).
        /// </summary>
        [MaxLength(64)]
        [StringLength(64)]
        public string Jti { get; set; }

        /// <summary>
        /// Gets or sets the id of the user the token acts on behalf of.
        /// </summary>
        public Guid ActingUserId { get; set; }

        /// <summary>
        /// Gets or sets when the token was issued.
        /// </summary>
        public DateTime IssuedAt { get; set; }

        /// <summary>
        /// Gets or sets when the token expires.
        /// </summary>
        public DateTime ExpiresAt { get; set; }

        /// <summary>
        /// Gets or sets the space-delimited scopes granted to the token.
        /// </summary>
        public string Scopes { get; set; }

        /// <summary>
        /// Gets or sets the item id the token is bound to, if any.
        /// </summary>
        public string? ItemId { get; set; }

        /// <summary>
        /// Gets or sets a human-readable label.
        /// </summary>
        [MaxLength(256)]
        [StringLength(256)]
        public string Label { get; set; }

        /// <summary>
        /// Gets or sets when the token was revoked, if it has been.
        /// </summary>
        public DateTime? RevokedAt { get; set; }
    }
}
