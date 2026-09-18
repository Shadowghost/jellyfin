using System.Collections.Generic;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Controller.Tests.MediaEncoding.Differential;

/// <summary>
/// The transcodes the new builder is measured against the legacy chains on.
/// </summary>
internal static class TranscodeCorpus
{
    public static IReadOnlyList<TranscodeCase> All { get; } = Build();

    private static TranscodeCase[] Build()
    {
        var cases = new List<TranscodeCase>();

        foreach (var acceleration in new[]
        {
            HardwareAccelerationType.none,
            HardwareAccelerationType.qsv,
            HardwareAccelerationType.vaapi,
            HardwareAccelerationType.nvenc,
            HardwareAccelerationType.amf,
            HardwareAccelerationType.videotoolbox,
            HardwareAccelerationType.rkmpp
        })
        {
            AddShapes(cases, acceleration.ToString(), acceleration, ChainVariant.Auto);
        }

        // Chains this machine could never reach, measured against the legacy vendor method directly.
        AddShapes(cases, "vt", HardwareAccelerationType.videotoolbox, ChainVariant.VideoToolbox);
        AddShapes(cases, "amf-dx11", HardwareAccelerationType.amf, ChainVariant.AmdD3d11);
        AddShapes(cases, "qsv-dx11", HardwareAccelerationType.qsv, ChainVariant.QsvD3d11);
        AddShapes(cases, "vaapi-ihd", HardwareAccelerationType.vaapi, ChainVariant.VaapiIntelFull);
        AddShapes(cases, "vaapi-amd", HardwareAccelerationType.vaapi, ChainVariant.VaapiAmdFull);

        return cases.ToArray();
    }

    private static void AddShapes(
        List<TranscodeCase> cases,
        string prefix,
        HardwareAccelerationType acceleration,
        ChainVariant variant)
    {
        void Add(string name, TranscodeCase shape)
            => cases.Add(shape with { Name = $"{prefix}/{name}", Acceleration = acceleration, Variant = variant });

        Add("passthrough-1080p", new TranscodeCase { Name = string.Empty });

        Add("downscale-720p", new TranscodeCase
        {
            Name = string.Empty,
            RequestedWidth = 1280,
            RequestedHeight = 720
        });

        Add("deinterlace-1080i", new TranscodeCase { Name = string.Empty, IsInterlaced = true });

        Add("deinterlace-and-downscale", new TranscodeCase
        {
            Name = string.Empty,
            IsInterlaced = true,
            RequestedWidth = 1280,
            RequestedHeight = 720
        });

        Add("hdr10-tonemap", new TranscodeCase
        {
            Name = string.Empty,
            InputCodec = "hevc",
            InputPixelFormat = "yuv420p10le",
            IsHdr10 = true,
            EnableTonemapping = true
        });

        Add("hdr10-tonemap-and-downscale", new TranscodeCase
        {
            Name = string.Empty,
            InputCodec = "hevc",
            InputPixelFormat = "yuv420p10le",
            IsHdr10 = true,
            EnableTonemapping = true,
            RequestedWidth = 1280,
            RequestedHeight = 720
        });

        Add("max-dimensions-720p", new TranscodeCase
        {
            Name = string.Empty,
            RequestedMaxWidth = 1280,
            RequestedMaxHeight = 720
        });

        Add("max-width-only", new TranscodeCase { Name = string.Empty, RequestedMaxWidth = 1280 });

        Add("request-matches-source", new TranscodeCase
        {
            Name = string.Empty,
            RequestedWidth = 1920,
            RequestedHeight = 1080
        });

        Add("full-side-by-side", new TranscodeCase
        {
            Name = string.Empty,
            InputWidth = 3840,
            Video3DFormat = Video3DFormat.FullSideBySide
        });

        Add("half-side-by-side-downscale", new TranscodeCase
        {
            Name = string.Empty,
            Video3DFormat = Video3DFormat.HalfSideBySide,
            RequestedWidth = 1280,
            RequestedHeight = 720
        });

        Add("rotate-90", new TranscodeCase { Name = string.Empty, Rotation = 90 });

        Add("rotate-180", new TranscodeCase { Name = string.Empty, Rotation = 180 });

        Add("rotate-90-and-downscale", new TranscodeCase
        {
            Name = string.Empty,
            Rotation = 90,
            RequestedWidth = 1280,
            RequestedHeight = 720
        });

        Add("graphical-subs", new TranscodeCase { Name = string.Empty, Subtitle = SubtitleKind.Graphical });

        Add("graphical-subs-and-downscale", new TranscodeCase
        {
            Name = string.Empty,
            Subtitle = SubtitleKind.Graphical,
            RequestedWidth = 1280,
            RequestedHeight = 720
        });

        Add("text-subs", new TranscodeCase { Name = string.Empty, Subtitle = SubtitleKind.Text });

        // Only the dispatcher decides to decode in software; a chain measured directly is always
        // handed a decoder, so these shapes would be measuring a different branch of it.
        if (variant == ChainVariant.Auto)
        {
            Add("software-decode", new TranscodeCase { Name = string.Empty, EnableHardwareDecoding = false });

            Add("software-decode-and-downscale", new TranscodeCase
            {
                Name = string.Empty,
                EnableHardwareDecoding = false,
                RequestedWidth = 1280,
                RequestedHeight = 720
            });

            Add("software-decode-deinterlace", new TranscodeCase
            {
                Name = string.Empty,
                EnableHardwareDecoding = false,
                IsInterlaced = true
            });
        }

        Add("mjpeg", new TranscodeCase { Name = string.Empty, Mjpeg = true });

        Add("mjpeg-downscale", new TranscodeCase
        {
            Name = string.Empty,
            Mjpeg = true,
            RequestedWidth = 320,
            RequestedHeight = 180
        });

        Add("mjpeg-max-dimensions", new TranscodeCase
        {
            Name = string.Empty,
            Mjpeg = true,
            RequestedMaxWidth = 320,
            RequestedMaxHeight = 180
        });

        Add("software-encoder-downscale", new TranscodeCase
        {
            Name = string.Empty,
            RequestedWidth = 1280,
            RequestedHeight = 720,
            EnableHardwareEncoding = false
        });
    }
}
