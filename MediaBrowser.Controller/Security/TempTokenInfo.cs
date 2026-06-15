using System;
using System.Collections.Generic;

namespace MediaBrowser.Controller.Security
{
    /// <summary>
    /// Metadata describing an outstanding temp token.
    /// </summary>
    public class TempTokenInfo
    {
        /// <summary>
        /// Gets or sets the token database id (used to revoke it).
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Gets or sets the label.
        /// </summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the granted scopes.
        /// </summary>
        public IReadOnlyList<string> Scopes { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Gets or sets the bound item id, if any.
        /// </summary>
        public string? ItemId { get; set; }

        /// <summary>
        /// Gets or sets when the token was issued.
        /// </summary>
        public DateTime IssuedAt { get; set; }

        /// <summary>
        /// Gets or sets when the token expires.
        /// </summary>
        public DateTime ExpiresAt { get; set; }
    }
}
