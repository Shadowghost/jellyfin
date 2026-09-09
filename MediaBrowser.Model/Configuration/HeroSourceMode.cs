namespace MediaBrowser.Model.Configuration;

/// <summary>
/// Defines where the hero section takes the items it shows from.
/// </summary>
public enum HeroSourceMode
{
    /// <summary>
    /// Pick items at random from everything the user can see.
    /// </summary>
    Random = 0,

    /// <summary>
    /// Pick items from a playlist.
    /// </summary>
    Playlist = 1,

    /// <summary>
    /// Pick items from a collection.
    /// </summary>
    Collection = 2
}
