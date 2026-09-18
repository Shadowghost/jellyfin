using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.MediaEncoding.Pipeline.Input;

/// <summary>
/// Everything the command line has to say before the input file, which is the devices the job runs
/// on and how the source is decoded.
/// </summary>
public sealed record InputPlan
{
    /// <summary>
    /// A plan that asks for nothing, which is what a software transcode needs.
    /// </summary>
    public static readonly InputPlan Empty = new();

    /// <summary>
    /// Gets the devices to initialise, in the order they are derived from one another.
    /// </summary>
    public IReadOnlyList<HardwareDevice> Devices { get; init; } = [];

    /// <summary>
    /// Gets the device the filter chain runs on.
    /// </summary>
    public string? FilterDeviceAlias { get; init; }

    /// <summary>
    /// Gets how the source is decoded.
    /// </summary>
    public VideoDecoder? Decoder { get; init; }

    /// <summary>
    /// Renders the whole thing.
    /// </summary>
    /// <returns>The arguments, space separated.</returns>
    public string ToArgument()
    {
        var arguments = new List<string>();

        foreach (var device in Devices)
        {
            arguments.Add(device.ToArgument());
        }

        if (!string.IsNullOrEmpty(FilterDeviceAlias))
        {
            arguments.Add("-filter_hw_device " + FilterDeviceAlias);
        }

        if (Decoder is not null)
        {
            arguments.Add(string.Join(' ', Decoder.ToArguments()));
        }

        return string.Join(' ', arguments.Where(a => !string.IsNullOrEmpty(a)));
    }
}
