using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace MediaBrowser.Providers.Plugins.Tmdb.Lists
{
    /// <summary>
    /// External id for the TMDb list a collection was synced from.
    /// </summary>
    public class TmdbListExternalId : IExternalId
    {
        /// <inheritdoc />
        public string ProviderName => TmdbUtils.ProviderName;

        /// <inheritdoc />
        public string Key => TmdbUtils.ListProviderId;

        /// <inheritdoc />
        public ExternalIdMediaType? Type => ExternalIdMediaType.List;

        /// <inheritdoc />
        public bool Supports(IHasProviderIds item)
        {
            return item is BoxSet;
        }
    }
}
