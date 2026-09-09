namespace MediaBrowser.Model.Dto;

/// <summary>
/// The kind of trailer attached to a hero item.
/// </summary>
public enum HeroTrailerKind
{
    /// <summary>
    /// A trailer stored in the library, playable through the regular playback endpoints.
    /// </summary>
    Local = 0,

    /// <summary>
    /// A trailer hosted elsewhere, playable only by fetching it from its host.
    /// </summary>
    Remote = 1
}
