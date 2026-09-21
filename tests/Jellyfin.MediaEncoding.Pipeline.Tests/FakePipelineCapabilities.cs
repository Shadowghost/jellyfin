using System;
using System.Collections.Generic;
using Jellyfin.MediaEncoding.Pipeline.BitStreams;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using MediaBrowser.Model.MediaEncoding.Hardware;

namespace Jellyfin.MediaEncoding.Pipeline.Tests;

internal sealed class FakePipelineCapabilities : IPipelineCapabilities
{
    public FakePipelineCapabilities(params string[] filters)
    {
        Filters = new HashSet<string>(filters, StringComparer.Ordinal);
    }

    public HashSet<string> Filters { get; }

    public bool SurfaceFormatsAccepted { get; set; } = true;

    public HwVppKind? UnsupportedOperation { get; set; }

    public bool SupportsFilter(string filterName) => Filters.Contains(filterName);

    public bool SupportsEncoder(string encoderName) => true;

    public bool SupportsBitStreamFilterOption(BitStreamFilterOption option) => true;

    public bool CanPerform(HwVppKind kind, FrameSize size, PixelFormat format) => UnsupportedOperation != kind;

    public bool SupportsSurfaceFormat(PixelFormat format) => SurfaceFormatsAccepted;
}
