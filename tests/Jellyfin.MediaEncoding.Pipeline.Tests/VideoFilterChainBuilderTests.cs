using Jellyfin.MediaEncoding.Pipeline.Accelerators;
using Jellyfin.MediaEncoding.Pipeline.Accelerators.Qsv;
using Jellyfin.MediaEncoding.Pipeline.Filters;
using Jellyfin.MediaEncoding.Pipeline.Frames;
using Jellyfin.MediaEncoding.Pipeline.Graph;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaEncoding.Hardware;
using Xunit;

namespace Jellyfin.MediaEncoding.Pipeline.Tests;

public class VideoFilterChainBuilderTests
{
    private static FrameState Interlaced1080p => new()
    {
        Surface = FrameSurface.System,
        PixelFormat = PixelFormat.Yuv420p,
        Size = new FrameSize(1920, 1080),
        IsInterlaced = true
    };

    [Fact]
    public void Build_DropsTheDeviceScalerWhenTheFrameAlreadyHasTheSize()
    {
        var capabilities = new FakePipelineCapabilities("vpp_qsv");
        var builder = new VideoFilterChainBuilder(new QsvD3d11Accelerator(), capabilities);
        var input = Interlaced1080p with { IsInterlaced = false, Surface = FrameSurface.Qsv };

        var chain = builder.Build(
            input,
            [new ScaleFilter(new FrameSize(1920, 1080))],
            FrameSurface.Qsv,
            PixelFormat.Nv12);

        // Only the output format pin survives; the resize itself is dropped.
        Assert.Equal("vpp_qsv=format=nv12", chain.ToFilterArgument());
    }

    [Fact]
    public void Build_KeepsASoftwareScaleThatWasAskedForExplicitly()
    {
        var capabilities = new FakePipelineCapabilities("bwdif");
        var builder = new VideoFilterChainBuilder(SoftwareAccelerator.Instance, capabilities);
        var input = Interlaced1080p with { IsInterlaced = false };

        var chain = builder.Build(
            input,
            [new ScaleFilter(new FrameSize(1920, 1080))],
            FrameSurface.System,
            PixelFormat.Yuv420p);

        Assert.Equal("scale=trunc(1920/2)*2:trunc(1080/2)*2,format=yuv420p", chain.ToFilterArgument());
    }

    [Fact]
    public void Build_FlattensFramePackedThreeDBeforeScaling()
    {
        var capabilities = new FakePipelineCapabilities("bwdif");
        var builder = new VideoFilterChainBuilder(SoftwareAccelerator.Instance, capabilities);
        var input = Interlaced1080p with { IsInterlaced = false, Size = new FrameSize(3840, 1080) };

        var chain = builder.Build(
            input,
            [new Flatten3DFilter(Video3DFormat.FullSideBySide, false), new ScaleFilter(default(ScalingRequest))],
            FrameSurface.System,
            PixelFormat.Yuv420p);

        Assert.Equal("crop=trunc(iw/4)*2:ih:0:0,format=yuv420p", chain.ToFilterArgument());
        Assert.Equal(new FrameSize(1920, 1080), chain.OutputState.Size);
    }

    [Fact]
    public void Build_UploadsBeforeTheFirstFilterThatNeedsTheDevice()
    {
        var capabilities = new FakePipelineCapabilities("vpp_qsv", "deinterlace_qsv", "bwdif");
        var builder = new VideoFilterChainBuilder(new QsvD3d11Accelerator(), capabilities);

        var chain = builder.Build(
            Interlaced1080p,
            [new DeinterlaceFilter("bwdif", false)],
            FrameSurface.Qsv,
            PixelFormat.Nv12);

        Assert.Equal(
            "format=nv12,hwupload,deinterlace_qsv=mode=2,vpp_qsv=format=nv12",
            chain.ToFilterArgument());
        Assert.Equal(FrameSurface.Qsv, chain.OutputState.Surface);
    }

    [Fact]
    public void Build_FusesTheResizeAndTheFormatPinIntoOneFilter()
    {
        var capabilities = new FakePipelineCapabilities("vpp_qsv", "deinterlace_qsv", "bwdif");
        var builder = new VideoFilterChainBuilder(new QsvD3d11Accelerator(), capabilities);

        var chain = builder.Build(
            Interlaced1080p with { IsInterlaced = false },
            [new ScaleFilter(new FrameSize(1280, 720))],
            FrameSurface.Qsv,
            PixelFormat.Nv12);

        // The resize and the output format pin are one pass over the frame, not two.
        Assert.Equal("format=nv12,hwupload,vpp_qsv=w=1280:h=720:format=nv12", chain.ToFilterArgument());
        Assert.Equal(new FrameSize(1280, 720), chain.OutputState.Size);
    }

    [Fact]
    public void Build_DownloadsWhenTheEncoderReadsFromSystemMemory()
    {
        var capabilities = new FakePipelineCapabilities("vpp_qsv", "deinterlace_qsv", "bwdif");
        var builder = new VideoFilterChainBuilder(new QsvD3d11Accelerator(), capabilities);

        var chain = builder.Build(
            Interlaced1080p,
            [new DeinterlaceFilter("bwdif", false)],
            FrameSurface.System,
            PixelFormat.Nv12);

        // The device converts to the format the encoder reads before the frame comes home.
        Assert.Equal(
            "format=nv12,hwupload,deinterlace_qsv=mode=2,vpp_qsv=format=nv12,hwdownload,format=nv12",
            chain.ToFilterArgument());
        Assert.Equal(FrameSurface.System, chain.OutputState.Surface);
    }

    [Fact]
    public void Build_FallsBackToSoftwareWhenTheDeviceLacksTheFilter()
    {
        var capabilities = new FakePipelineCapabilities("bwdif");
        var builder = new VideoFilterChainBuilder(new QsvD3d11Accelerator(), capabilities);

        var chain = builder.Build(
            Interlaced1080p,
            [new DeinterlaceFilter("bwdif", false), new ScaleFilter(new FrameSize(1280, 720))],
            FrameSurface.System,
            PixelFormat.Yuv420p);

        Assert.Equal("bwdif=0:-1:0,scale=trunc(1280/2)*2:trunc(720/2)*2,format=yuv420p", chain.ToFilterArgument());
    }

    [Fact]
    public void Build_FallsBackToSoftwareWhenTheDeviceRejectsTheUploadFormat()
    {
        var capabilities = new FakePipelineCapabilities("vpp_qsv", "deinterlace_qsv", "bwdif")
        {
            SurfaceFormatsAccepted = false
        };
        var builder = new VideoFilterChainBuilder(new QsvD3d11Accelerator(), capabilities);

        var chain = builder.Build(
            Interlaced1080p,
            [new DeinterlaceFilter("bwdif", false)],
            FrameSurface.System,
            PixelFormat.Yuv420p);

        Assert.Equal("bwdif=0:-1:0,format=yuv420p", chain.ToFilterArgument());
    }

    [Fact]
    public void Build_KeepsTheOperationInSoftwareWhenTheDeviceCannotPerformIt()
    {
        var capabilities = new FakePipelineCapabilities("vpp_qsv", "deinterlace_qsv", "bwdif")
        {
            UnsupportedOperation = HwVppKind.Deinterlace
        };
        var builder = new VideoFilterChainBuilder(new QsvD3d11Accelerator(), capabilities);

        var chain = builder.Build(
            Interlaced1080p,
            [new DeinterlaceFilter("bwdif", false), new ScaleFilter(new FrameSize(1280, 720))],
            FrameSurface.Qsv,
            PixelFormat.Nv12);

        Assert.Equal(
            "bwdif=0:-1:0,format=nv12,hwupload,vpp_qsv=w=1280:h=720:format=nv12",
            chain.ToFilterArgument());
    }
}
