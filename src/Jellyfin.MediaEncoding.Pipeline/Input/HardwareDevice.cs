namespace Jellyfin.MediaEncoding.Pipeline.Input;

/// <summary>
/// A hardware device the command line initialises before anything can run on it.
/// </summary>
/// <param name="Kind">The device type ffmpeg knows, such as <c>vaapi</c>.</param>
/// <param name="Alias">The name the rest of the command line refers to the device by.</param>
public sealed record HardwareDevice(string Kind, string Alias)
{
    /// <summary>
    /// Gets how the device is selected, appended after a colon.
    /// </summary>
    public string? Spec { get; init; }

    /// <summary>
    /// Gets the alias of the device this one is derived from, appended after an at sign.
    /// </summary>
    /// <remarks>
    /// A derived device shares its memory with its source, which is what lets a frame be mapped
    /// between the two rather than copied.
    /// </remarks>
    public string? SourceAlias { get; init; }

    /// <summary>
    /// Renders the initialisation argument.
    /// </summary>
    /// <returns>The argument.</returns>
    public string ToArgument()
    {
        var argument = $"-init_hw_device {Kind}={Alias}";

        if (!string.IsNullOrEmpty(SourceAlias))
        {
            return argument + "@" + SourceAlias;
        }

        return string.IsNullOrEmpty(Spec) ? argument : argument + ":" + Spec;
    }
}
