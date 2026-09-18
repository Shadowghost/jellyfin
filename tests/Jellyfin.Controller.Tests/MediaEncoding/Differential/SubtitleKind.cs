namespace Jellyfin.Controller.Tests.MediaEncoding.Differential;

/// <summary>
/// What kind of subtitle a case burns into the picture.
/// </summary>
internal enum SubtitleKind
{
    /// <summary>
    /// No subtitle is burned in.
    /// </summary>
    None = 0,

    /// <summary>
    /// A bitmap subtitle, which arrives as a second input stream.
    /// </summary>
    Graphical = 1,

    /// <summary>
    /// A text subtitle, which a filter renders from the file.
    /// </summary>
    Text = 2
}
