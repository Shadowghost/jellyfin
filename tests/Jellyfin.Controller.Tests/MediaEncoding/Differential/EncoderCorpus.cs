using System.Collections.Generic;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Controller.Tests.MediaEncoding.Differential;

/// <summary>
/// The encoder selections the package is measured on.
/// </summary>
internal static class EncoderCorpus
{
    public static IReadOnlyList<EncoderCase> All { get; } = Build();

    private static EncoderCase[] Build()
    {
        var cases = new List<EncoderCase>();

        foreach (var acceleration in new[]
        {
            HardwareAccelerationType.none,
            HardwareAccelerationType.qsv,
            HardwareAccelerationType.vaapi,
            HardwareAccelerationType.nvenc,
            HardwareAccelerationType.amf,
            HardwareAccelerationType.videotoolbox,
            HardwareAccelerationType.rkmpp,
            HardwareAccelerationType.v4l2m2m
        })
        {
            foreach (var codec in new[] { "h264", "hevc", "h265", "av1" })
            {
                cases.Add(new EncoderCase
                {
                    Name = $"{acceleration}/{codec}",
                    OutputCodec = codec,
                    Acceleration = acceleration
                });

                cases.Add(new EncoderCase
                {
                    Name = $"{acceleration}/{codec}/software-encoding",
                    OutputCodec = codec,
                    Acceleration = acceleration,
                    HardwareEncoding = false
                });

                cases.Add(new EncoderCase
                {
                    Name = $"{acceleration}/{codec}/hardware-disabled",
                    OutputCodec = codec,
                    Acceleration = acceleration,
                    HardwareDisabled = true
                });
            }

            cases.Add(new EncoderCase
            {
                Name = $"{acceleration}/copy",
                OutputCodec = "copy",
                Acceleration = acceleration
            });
        }

        return cases.ToArray();
    }
}
