using System.Collections.Generic;
using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Accelerators;
using Jellyfin.MediaEncoding.Pipeline.Accelerators.Cuda;
using Jellyfin.MediaEncoding.Pipeline.Accelerators.Qsv;
using Jellyfin.MediaEncoding.Pipeline.Accelerators.Rkrga;
using Jellyfin.MediaEncoding.Pipeline.Accelerators.Vaapi;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Controller.Tests.MediaEncoding.FilterChains;

/// <summary>
/// One encoder selection, described in terms both selectors understand.
/// </summary>
internal sealed record EncoderCase
{
    public required string Name { get; init; }

    public required string OutputCodec { get; init; }

    public HardwareAccelerationType Acceleration { get; init; }

    public bool HardwareEncoding { get; init; } = true;

    public bool HardwareDisabled { get; init; }

    public override string ToString() => Name;

    public static IHardwareAccelerator AcceleratorFor(HardwareAccelerationType type) => type switch
    {
        HardwareAccelerationType.nvenc => new CudaAccelerator(),
        HardwareAccelerationType.qsv => new QsvVaapiAccelerator(),
        HardwareAccelerationType.vaapi => new VaapiAccelerator(),
        HardwareAccelerationType.rkmpp => new RkrgaAccelerator(true),
        HardwareAccelerationType.amf => new EncoderOnlyAccelerator("amf"),
        HardwareAccelerationType.videotoolbox => new EncoderOnlyAccelerator("videotoolbox"),
        HardwareAccelerationType.v4l2m2m => new EncoderOnlyAccelerator("v4l2m2m"),
        _ => SoftwareAccelerator.Instance
    };
}
