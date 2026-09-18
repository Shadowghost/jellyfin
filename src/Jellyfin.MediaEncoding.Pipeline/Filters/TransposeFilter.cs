using System;
using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Filters;

/// <summary>
/// Undoes the rotation a source carries in its metadata.
/// </summary>
/// <remarks>
/// It emits nothing on its own: a frame decoded in system memory has already been turned the right
/// way up by the decoder, so only a device that was told not to autorotate needs a filter, and each
/// one has its own. <see cref="IHardwareAccelerator.SelectFilter"/> swaps this for the device's.
/// </remarks>
/// <param name="Direction">The direction ffmpeg turns the picture, such as <c>cclock</c>.</param>
public sealed record TransposeFilter(string Direction) : IVideoFilter
{
    /// <inheritdoc />
    public FrameSurface? RequiredSurface => null;

    /// <inheritdoc />
    public bool PinsPixelFormat => false;

    /// <summary>
    /// Gets a value indicating whether the turn puts the picture on its side, so that its width and
    /// height change places.
    /// </summary>
    public bool SwapsDimensions =>
        string.Equals(Direction, "cclock", StringComparison.Ordinal)
        || string.Equals(Direction, "clock", StringComparison.Ordinal);

    /// <summary>
    /// Gets the direction that undoes a rotation, or an empty string when there is nothing to undo.
    /// </summary>
    /// <param name="rotation">The rotation the source carries, in degrees.</param>
    /// <returns>The direction.</returns>
    public static string DirectionFor(int rotation) => rotation switch
    {
        90 => "cclock",
        -90 => "clock",
        180 or -180 => "reversal",
        _ => string.Empty
    };

    /// <inheritdoc />
    public IVideoFilter? Evaluate(FrameState state, IPipelineCapabilities capabilities)
        => string.IsNullOrEmpty(Direction) ? null : this;

    /// <inheritdoc />
    public FrameState Apply(FrameState state) => state;

    /// <inheritdoc />
    public string? ToFilterArgument() => null;
}
