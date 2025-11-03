namespace MediaBrowser.Providers.Plugins.Omdb.Dto;

/// <summary>
/// Describes OMDB rating.
/// </summary>
internal class OmdbRating
{
    /// <summary>
    /// Gets or sets rating source.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets rating value.
    /// </summary>
    public string? Value { get; set; }
}
