using System;
using System.Collections.Generic;
using Jellyfin.Extensions;

namespace MediaBrowser.Providers.Plugins.Tmdb
{
    /// <summary>
    /// Folds TMDb's genre lists into the map a backfill needs.
    /// </summary>
    public static class TmdbGenreVocabulary
    {
        /// <summary>
        /// Maps every genre name TMDb gave, in every language it was asked for, onto the id behind it.
        /// </summary>
        /// <param name="names">The names and ids, from any number of lists and languages.</param>
        /// <returns>The clean name of each genre, mapped to its id.</returns>
        /// <remarks>
        /// Keyed on the clean name, because that is what a genre item is identified by. A name that two
        /// ids claim is dropped: merging on it would fold two genres together and delete one's artwork,
        /// and a translation fusing two names is likelier than the vocabulary being wrong.
        /// </remarks>
        public static IReadOnlyDictionary<string, int> Build(IEnumerable<TmdbGenreName> names)
        {
            ArgumentNullException.ThrowIfNull(names);

            var byCleanName = new Dictionary<string, int>(StringComparer.Ordinal);
            var ambiguous = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (name, id) in names)
            {
                if (string.IsNullOrWhiteSpace(name) || id <= 0)
                {
                    continue;
                }

                var cleanName = name.GetCleanValue();
                if (string.IsNullOrEmpty(cleanName) || ambiguous.Contains(cleanName))
                {
                    continue;
                }

                if (byCleanName.TryGetValue(cleanName, out var known) && known != id)
                {
                    byCleanName.Remove(cleanName);
                    ambiguous.Add(cleanName);
                    continue;
                }

                byCleanName[cleanName] = id;
            }

            return byCleanName;
        }
    }
}
