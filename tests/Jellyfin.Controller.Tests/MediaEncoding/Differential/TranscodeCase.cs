using MediaBrowser.Model.Entities;

namespace Jellyfin.Controller.Tests.MediaEncoding.Differential;

/// <summary>
/// One transcode described in terms both chain builders understand.
/// </summary>
internal sealed record TranscodeCase
{
    public required string Name { get; init; }

    public HardwareAccelerationType Acceleration { get; init; } = HardwareAccelerationType.none;

    public ChainVariant Variant { get; init; } = ChainVariant.Auto;

    public string InputCodec { get; init; } = "h264";

    public int InputWidth { get; init; } = 1920;

    public int InputHeight { get; init; } = 1080;

    public string InputPixelFormat { get; init; } = "yuv420p";

    public bool IsInterlaced { get; init; }

    public int Rotation { get; init; }

    public SubtitleKind Subtitle { get; init; } = SubtitleKind.None;

    public bool IsHdr10 { get; init; }

    public Video3DFormat? Video3DFormat { get; init; }

    public int? RequestedWidth { get; init; }

    public int? RequestedHeight { get; init; }

    public int? RequestedMaxWidth { get; init; }

    public int? RequestedMaxHeight { get; init; }

    public string OutputCodec { get; init; } = "h264";

    public bool EnableTonemapping { get; init; }

    public bool EnableHardwareEncoding { get; init; } = true;

    public bool EnableHardwareDecoding { get; init; } = true;

    public override string ToString() => Name;
}
