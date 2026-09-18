using System;
using System.Collections.Generic;

namespace Jellyfin.MediaEncoding.Pipeline.Frames;

/// <summary>
/// A pixel format, with the properties the filter chain has to reason about.
/// </summary>
/// <param name="Name">The name ffmpeg knows the format by.</param>
/// <param name="BitDepth">The bits per colour component.</param>
/// <param name="HasAlpha">Whether the format carries an alpha channel.</param>
public readonly record struct PixelFormat(string Name, int BitDepth, bool HasAlpha)
{
    /// <summary>
    /// A format that could not be determined.
    /// </summary>
    public static readonly PixelFormat Unknown = new(string.Empty, 0, false);

    /// <summary>
    /// Planar 8 bit YUV 4:2:0.
    /// </summary>
    public static readonly PixelFormat Yuv420p = new("yuv420p", 8, false);

    /// <summary>
    /// Planar 10 bit YUV 4:2:0.
    /// </summary>
    public static readonly PixelFormat Yuv420p10le = new("yuv420p10le", 10, false);

    /// <summary>
    /// Planar 8 bit YUV 4:4:4.
    /// </summary>
    public static readonly PixelFormat Yuv444p = new("yuv444p", 8, false);

    /// <summary>
    /// Planar 8 bit YUV 4:2:0 with an alpha channel.
    /// </summary>
    public static readonly PixelFormat Yuva420p = new("yuva420p", 8, true);

    /// <summary>
    /// Semi planar 8 bit YUV 4:2:0.
    /// </summary>
    public static readonly PixelFormat Nv12 = new("nv12", 8, false);

    /// <summary>
    /// Semi planar 10 bit YUV 4:2:0.
    /// </summary>
    public static readonly PixelFormat P010le = new("p010le", 10, false);

    /// <summary>
    /// Packed 8 bit BGRA.
    /// </summary>
    public static readonly PixelFormat Bgra = new("bgra", 8, true);

    private static readonly Dictionary<string, PixelFormat> _known = new(StringComparer.OrdinalIgnoreCase)
    {
        [Yuv420p.Name] = Yuv420p,
        [Yuv420p10le.Name] = Yuv420p10le,
        [Yuv444p.Name] = Yuv444p,
        [Yuva420p.Name] = Yuva420p,
        [Nv12.Name] = Nv12,
        [P010le.Name] = P010le,
        [Bgra.Name] = Bgra,
        ["yuvj420p"] = Yuv420p,
        ["yuv420p10"] = Yuv420p10le,
        ["p010"] = P010le
    };

    /// <summary>
    /// Gets a value indicating whether the format was determined.
    /// </summary>
    public bool IsKnown => BitDepth > 0;

    /// <summary>
    /// Resolves a pixel format reported by ffprobe.
    /// </summary>
    /// <param name="name">The format name.</param>
    /// <returns>The format, or <see cref="Unknown"/> when it is not one the chain knows.</returns>
    public static PixelFormat Parse(string? name)
        => !string.IsNullOrEmpty(name) && _known.TryGetValue(name, out var format) ? format : Unknown;

    /// <summary>
    /// Gets the canonical format a hardware surface holds frames of this depth in.
    /// </summary>
    /// <returns>The hardware format.</returns>
    public PixelFormat ToHardwareFormat()
    {
        if (HasAlpha)
        {
            return Bgra;
        }

        return BitDepth >= 10 ? P010le : Nv12;
    }
}
