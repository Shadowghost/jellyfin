using Jellyfin.MediaEncoding.Pipeline.Frames;

namespace Jellyfin.MediaEncoding.Pipeline.Filters.Transfers;

/// <summary>
/// Moves a frame from system memory onto a hardware surface.
/// </summary>
/// <param name="TargetSurface">The surface to upload to.</param>
/// <param name="UploadFormat">The format the frame is converted to before the upload.</param>
/// <param name="IncludeFormat">Whether the frame still has to be converted before the upload.</param>
/// <param name="DeriveDevice">
/// Whether the device is derived from the one the chain already holds, as it is for a second device
/// the chain only borrows for one operation.
/// </param>
public sealed record UploadFilter(
    FrameSurface TargetSurface,
    PixelFormat UploadFormat,
    bool IncludeFormat = true,
    bool DeriveDevice = false) : IVideoFilter
{
    /// <inheritdoc />
    public FrameSurface? RequiredSurface => FrameSurface.System;

    /// <inheritdoc />
    public bool PinsPixelFormat => true;

    /// <inheritdoc />
    public IVideoFilter Evaluate(FrameState state, IPipelineCapabilities capabilities) => this;

    /// <inheritdoc />
    public FrameState Apply(FrameState state) => state with
    {
        Surface = TargetSurface,
        PixelFormat = UploadFormat
    };

    /// <inheritdoc />
    public string? ToFilterArgument()
    {
        var device = TargetSurface.GetDeviceName();
        if (device is null)
        {
            return null;
        }

        var upload = DeriveDevice ? $"hwupload=derive_device={device}" : TargetSurface.GetUploadFilterName();
        if (upload is null)
        {
            return null;
        }

        return IncludeFormat ? $"format={UploadFormat.Name},{upload}" : upload;
    }
}
