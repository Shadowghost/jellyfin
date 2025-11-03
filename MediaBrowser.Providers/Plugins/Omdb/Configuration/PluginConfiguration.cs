using MediaBrowser.Model.Plugins;

namespace MediaBrowser.Providers.Plugins.Omdb;

/// <summary>
/// Plugin configuration for OMDb provider.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets a value indicating whether to fetch cast and crew information.
    /// </summary>
    public bool CastAndCrew { get; set; }
}
