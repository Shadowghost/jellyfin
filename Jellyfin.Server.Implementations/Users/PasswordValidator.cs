using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Model.Cryptography;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Implementations.Users
{
    /// <summary>
    /// Validates and changes built-in username/password credentials using PBKDF2.
    /// </summary>
    public class PasswordValidator
    {
        private readonly ILogger<PasswordValidator> _logger;
        private readonly ICryptoProvider _cryptographyProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="PasswordValidator"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="cryptographyProvider">The cryptography provider.</param>
        public PasswordValidator(ILogger<PasswordValidator> logger, ICryptoProvider cryptographyProvider)
        {
            _logger = logger;
            _cryptographyProvider = cryptographyProvider;
        }

        /// <summary>
        /// Validates the supplied password against the resolved user's stored credentials.
        /// </summary>
        /// <param name="resolvedUser">The resolved user.</param>
        /// <param name="password">The password to validate.</param>
        /// <exception cref="AuthenticationException">Thrown when the credentials are invalid.</exception>
        public void Validate(User resolvedUser, string password)
        {
            [DoesNotReturn]
            static void ThrowAuthenticationException()
            {
                throw new AuthenticationException("Invalid username or password");
            }

            if (resolvedUser is null)
            {
                ThrowAuthenticationException();
            }

            // As long as jellyfin supports password-less users, we need this little block here to accommodate
            if (string.IsNullOrEmpty(resolvedUser.Password) && string.IsNullOrEmpty(password))
            {
                return;
            }

            // Handle the case when the stored password is null, but the user tried to login with a password
            if (resolvedUser.Password is null)
            {
                ThrowAuthenticationException();
            }

            PasswordHash readyHash = PasswordHash.Parse(resolvedUser.Password);
            if (!_cryptographyProvider.Verify(readyHash, password))
            {
                ThrowAuthenticationException();
            }

            // Migrate old hashes to the new default
            if (!string.Equals(readyHash.Id, _cryptographyProvider.DefaultHashMethod, StringComparison.Ordinal)
                || int.Parse(readyHash.Parameters["iterations"], CultureInfo.InvariantCulture) != Constants.DefaultIterations)
            {
                _logger.LogInformation("Migrating password hash of {User} to the latest default", resolvedUser.Username);
                ChangePassword(resolvedUser, password);
            }
        }

        /// <summary>
        /// Changes the stored password for the given user.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="newPassword">The new password.</param>
        public void ChangePassword(User user, string newPassword)
        {
            if (string.IsNullOrEmpty(newPassword))
            {
                user.Password = null;
                return;
            }

            PasswordHash newPasswordHash = _cryptographyProvider.CreatePasswordHash(newPassword);
            user.Password = newPasswordHash.ToString();
        }
    }
}
