namespace MediaBrowser.Model.Dto;

/// <summary>
/// A label/count pair used for playback statistics breakdowns.
/// </summary>
public class NameCountDto
{
    /// <summary>
    /// Gets or sets the label (e.g. a resolution, codec, or language). Null represents "unknown/none".
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the count.
    /// </summary>
    public int Count { get; set; }
}
