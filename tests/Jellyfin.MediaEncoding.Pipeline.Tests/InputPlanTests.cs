using Jellyfin.MediaEncoding.Pipeline;
using Jellyfin.MediaEncoding.Pipeline.Accelerators.Amf;
using Jellyfin.MediaEncoding.Pipeline.Accelerators.Cuda;
using Jellyfin.MediaEncoding.Pipeline.Accelerators.Qsv;
using Jellyfin.MediaEncoding.Pipeline.Input;
using Xunit;

namespace Jellyfin.MediaEncoding.Pipeline.Tests;

/// <summary>
/// The devices a job opens, which the capability report names when it has one.
/// </summary>
public class InputPlanTests
{
    public static TheoryData<IHardwareAccelerator, string, string> ReportedDevices() => new()
    {
        { new CudaAccelerator(), "-init_hw_device cuda=cu:0", "-init_hw_device cuda=cu:1" },
        { new AmfD3d11Accelerator(), "-init_hw_device d3d11va=dx11:,vendor=0x1002", "-init_hw_device d3d11va=dx11:1" },
        { new QsvD3d11Accelerator(), "-init_hw_device d3d11va=dx11:0,vendor=0x8086", "-init_hw_device d3d11va=dx11:1" }
    };

    [Theory]
    [MemberData(nameof(ReportedDevices))]
    public void CreateInputPlan_PinsTheDeviceTheReportNamed(
        IHardwareAccelerator accelerator,
        string byVendor,
        string byIndex)
    {
        var request = new InputPlanRequest(true, false);

        Assert.StartsWith(byVendor, accelerator.CreateInputPlan(request).ToArgument(), System.StringComparison.Ordinal);
        Assert.StartsWith(
            byIndex,
            accelerator.CreateInputPlan(request with { DeviceIndex = 1 }).ToArgument(),
            System.StringComparison.Ordinal);
    }

    [Fact]
    public void CreateInputPlan_QsvOnVaapi_OpensTheReportedRenderNode()
    {
        var accelerator = new QsvVaapiAccelerator();
        var request = new InputPlanRequest(true, false);

        Assert.StartsWith(
            "-init_hw_device vaapi=va:,vendor_id=0x8086,driver=iHD",
            accelerator.CreateInputPlan(request).ToArgument(),
            System.StringComparison.Ordinal);

        Assert.StartsWith(
            "-init_hw_device vaapi=va:/dev/dri/renderD129,driver=iHD",
            accelerator.CreateInputPlan(request with { DevicePath = "/dev/dri/renderD129" }).ToArgument(),
            System.StringComparison.Ordinal);
    }
}
