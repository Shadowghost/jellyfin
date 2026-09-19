using System.Collections.Generic;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Filters.Transfers;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using Jellyfin.MediaEncoding.Pipeline.Subtitles;

namespace Jellyfin.MediaEncoding.Pipeline.Graph;

/// <summary>
/// Turns the operations a transcode needs into a filter chain for one hardware accelerator.
/// </summary>
/// <remarks>
/// Callers ask for device independent operations and never place a transfer themselves: filters
/// declare the surface they need and the builder moves the frame there.
/// </remarks>
public sealed class VideoFilterChainBuilder
{
    private static readonly (FrameSurface From, FrameSurface To)[] _mappableSurfaces =
    [
        (FrameSurface.Vaapi, FrameSurface.Qsv),
        (FrameSurface.Vaapi, FrameSurface.Vulkan),
        (FrameSurface.Cuda, FrameSurface.OpenCl),
        (FrameSurface.Cuda, FrameSurface.Vulkan),
        (FrameSurface.D3d11, FrameSurface.Qsv),
        (FrameSurface.Rkmpp, FrameSurface.OpenCl)
    ];

    private readonly IHardwareAccelerator _accelerator;
    private readonly IPipelineCapabilities _capabilities;

    private FrameSurface? _mappedFrom;

    /// <summary>
    /// Initializes a new instance of the <see cref="VideoFilterChainBuilder"/> class.
    /// </summary>
    /// <param name="accelerator">The accelerator the chain runs on.</param>
    /// <param name="capabilities">What ffmpeg and the device can do.</param>
    public VideoFilterChainBuilder(IHardwareAccelerator accelerator, IPipelineCapabilities capabilities)
    {
        _accelerator = accelerator;
        _capabilities = capabilities;
    }

    /// <summary>
    /// Builds the chain.
    /// </summary>
    /// <param name="input">The state of the frame entering the chain.</param>
    /// <param name="requested">The operations the transcode needs, in order.</param>
    /// <param name="outputSurface">The surface the encoder reads from.</param>
    /// <param name="outputFormat">The format the encoder expects.</param>
    /// <returns>The resolved chain.</returns>
    public VideoFilterChain Build(
        FrameState input,
        IReadOnlyList<IVideoFilter> requested,
        FrameSurface outputSurface,
        PixelFormat outputFormat)
    {
        var (accelerator, planned) = _accelerator.Plan(requested, input, _capabilities);

        return ReferenceEquals(accelerator, _accelerator)
            ? BuildPlanned(input, planned, outputSurface, outputFormat)
            : new VideoFilterChainBuilder(accelerator, _capabilities)
                .BuildPlanned(input, planned, outputSurface, outputFormat);
    }

    /// <summary>
    /// Builds the whole graph, which is the picture chain plus the subtitle drawn over it.
    /// </summary>
    /// <param name="input">The state of the frame entering the chain.</param>
    /// <param name="requested">The operations the transcode needs, in order.</param>
    /// <param name="outputSurface">The surface the encoder reads from.</param>
    /// <param name="outputFormat">The format the encoder expects.</param>
    /// <param name="subtitle">The subtitle to burn in, or <c>null</c> when there is none.</param>
    /// <returns>The graph.</returns>
    public VideoFilterGraph BuildGraph(
        FrameState input,
        IReadOnlyList<IVideoFilter> requested,
        FrameSurface outputSurface,
        PixelFormat outputFormat,
        SubtitleSource? subtitle)
    {
        if (subtitle is null)
        {
            return new VideoFilterGraph(Build(input, requested, outputSurface, outputFormat), null, null);
        }

        var accelerator = _accelerator.ForOverlay(subtitle);
        var builder = ReferenceEquals(accelerator, _accelerator)
            ? this
            : new VideoFilterChainBuilder(accelerator, _capabilities);

        // A device with no overlay of its own hands the picture back to system memory for it, and
        // takes it again afterwards.
        var overlayAtHome = accelerator.SubtitleFormat is null && outputSurface != FrameSurface.System;
        var pictureSurface = overlayAtHome ? FrameSurface.System : outputSurface;

        // An overlay that settles the format itself spares the picture a pass that would only convert.
        var pictureFormat = accelerator.OverlayPinsPixelFormat ? PixelFormat.Unknown : outputFormat;

        // With no device to draw on, a rendered subtitle is drawn last, over the finished picture.
        if (subtitle.IsText && accelerator.SubtitleFormat is null)
        {
            var picture = builder.Build(input, requested, pictureSurface, pictureFormat);
            var drawn = new List<IVideoFilter>(picture.Filters) { CreateTextSubtitleFilter(subtitle, false) };
            var state = picture.OutputState;

            if (overlayAtHome)
            {
                builder.TryTransfer(outputSurface, drawn, ref state);
            }

            return new VideoFilterGraph(new VideoFilterChain(drawn, state), null, null);
        }

        var main = builder.Build(input, requested, pictureSurface, pictureFormat);
        var size = main.OutputState.Size;

        var branch = new List<IVideoFilter>();

        if (subtitle.IsText)
        {
            branch.Add(new AlphaSrcFilter(size, subtitle.FrameRate, "0"));
        }
        else
        {
            branch.Add(new GraphicalSubtitleScaleFilter(size, subtitle.Size));
        }

        if (accelerator.SubtitleFormat is { } subtitleFormat)
        {
            branch.Add(new FormatFilter(subtitleFormat));
        }

        if (subtitle.IsText)
        {
            branch.Add(CreateTextSubtitleFilter(subtitle, true));
        }

        if (accelerator.CreateSubtitleUpload() is { } upload)
        {
            branch.Add(upload);
        }

        var overlayFilters = new List<IVideoFilter> { accelerator.CreateOverlay(size, subtitle.IsText) };
        var overlayState = main.OutputState;

        if (overlayAtHome)
        {
            builder.TryTransfer(outputSurface, overlayFilters, ref overlayState);
        }

        var overlay = new VideoFilterChain(overlayFilters, overlayState);

        return new VideoFilterGraph(main, new VideoFilterChain(branch, main.OutputState), overlay)
        {
            SubtitleIsRendered = subtitle.IsText
        };
    }

    private static TextSubtitleFilter CreateTextSubtitleFilter(SubtitleSource subtitle, bool ontoCanvas)
        => new(subtitle.Path ?? string.Empty, ontoCanvas)
        {
            FontsDirectory = subtitle.FontsDirectory,
            CharacterEncoding = subtitle.CharacterEncoding,
            SeekSeconds = subtitle.SeekSeconds,
            CopyTimestamps = subtitle.CopyTimestamps
        };

    private VideoFilterChain BuildPlanned(
        FrameState input,
        IReadOnlyList<IVideoFilter> requested,
        FrameSurface outputSurface,
        PixelFormat outputFormat)
    {
        var needed = Evaluate(input, requested);
        var (resolved, state) = Resolve(input, needed, outputSurface, outputFormat);
        return new VideoFilterChain(Fuse(resolved), state);
    }

    private List<IVideoFilter> Evaluate(FrameState input, IReadOnlyList<IVideoFilter> requested)
    {
        var state = input;
        var needed = new List<IVideoFilter>();

        foreach (var filter in requested)
        {
            var evaluated = filter.Evaluate(state, _capabilities);
            if (evaluated is null)
            {
                continue;
            }

            state = evaluated.Apply(state);
            needed.Add(evaluated);
        }

        return needed;
    }

    private (List<IVideoFilter> Filters, FrameState State) Resolve(
        FrameState input,
        List<IVideoFilter> needed,
        FrameSurface outputSurface,
        PixelFormat outputFormat)
    {
        var state = input;
        var resolved = new List<IVideoFilter>();

        foreach (var filter in needed)
        {
            var best = _accelerator.SelectFilter(filter, state, _capabilities);
            if (best is null)
            {
                continue;
            }

            if (best.RequiredInputFormat is { } inputFormat)
            {
                PinFormat(inputFormat, resolved, ref state);
            }

            if (best.RequiredSurface is { } required
                && state.Surface != required
                && !TryTransfer(required, resolved, ref state))
            {
                best = filter;
                if (best.RequiredSurface is { } fallback && state.Surface != fallback)
                {
                    TryTransfer(fallback, resolved, ref state);
                }
            }

            state = best.Apply(state);
            resolved.Add(best);
        }

        // The pin exists to hand the encoder, or the trip home, the format it reads; a caller that
        // asks for no particular format has something else settling it.
        if (state.Surface != FrameSurface.System && outputFormat.IsKnown)
        {
            PinFormat(_accelerator.GetDeviceFormat(state), resolved, ref state);
        }

        if (state.Surface != outputSurface)
        {
            TryTransfer(outputSurface, resolved, ref state);
        }

        PinFormat(outputFormat, resolved, ref state);

        return (resolved, state);
    }

    private void PinFormat(PixelFormat format, List<IVideoFilter> resolved, ref FrameState state)
    {
        if (!format.IsKnown)
        {
            return;
        }

        if (resolved.Count > 0 && resolved[^1].PinsPixelFormat && state.PixelFormat == format)
        {
            return;
        }

        // A frame that is already home is converted in software, whatever device the chain was
        // meant to run on; only a frame still on a device asks the device to convert it.
        var convert = state.Surface == FrameSurface.System
            ? new FormatFilter(format)
            : _accelerator.CreateFormatFilter(format);

        if (convert is not null)
        {
            if (convert.RequiredSurface is { } required && state.Surface != required)
            {
                TryTransfer(required, resolved, ref state);
            }

            state = convert.Apply(state);
            resolved.Add(convert);
        }
    }

    private bool TryTransfer(FrameSurface target, List<IVideoFilter> resolved, ref FrameState state)
    {
        if (state.Surface == target)
        {
            return true;
        }

        var reverse = _mappedFrom == target;
        if (_accelerator.CreateTransferFilter(target, state, reverse) is { } vendorTransfer)
        {
            _mappedFrom = state.Surface;
            return Append(vendorTransfer, resolved, ref state);
        }

        if (target == FrameSurface.System)
        {
            return Append(new DownloadFilter(state.PixelFormat), resolved, ref state);
        }

        if (state.Surface != FrameSurface.System)
        {
            if (CanMap(state.Surface, target))
            {
                _mappedFrom = state.Surface;
                return Append(new MapFilter(target, reverse), resolved, ref state);
            }

            Append(new DownloadFilter(state.PixelFormat), resolved, ref state);
        }

        var uploadFormat = state.PixelFormat.IsKnown ? state.PixelFormat.ToHardwareFormat() : PixelFormat.Nv12;
        if (!_capabilities.SupportsSurfaceFormat(uploadFormat))
        {
            return false;
        }

        return Append(
            new UploadFilter(
                target,
                uploadFormat,
                state.PixelFormat != uploadFormat,
                target != _accelerator.EncoderSurface),
            resolved,
            ref state);
    }

    private static bool Append(IVideoFilter filter, List<IVideoFilter> resolved, ref FrameState state)
    {
        state = filter.Apply(state);
        resolved.Add(filter);
        return true;
    }

    private static bool CanMap(FrameSurface from, FrameSurface to)
    {
        foreach (var (a, b) in _mappableSurfaces)
        {
            if ((a == from && b == to) || (a == to && b == from))
            {
                return true;
            }
        }

        return false;
    }

    private List<IVideoFilter> Fuse(List<IVideoFilter> resolved)
    {
        var fused = new List<IVideoFilter>(resolved.Count);

        foreach (var filter in resolved)
        {
            if (fused.Count > 0 && _accelerator.TryFuse(fused[^1], filter) is { } merged)
            {
                fused[^1] = merged;
                continue;
            }

            fused.Add(filter);
        }

        return fused;
    }
}
