using Photobooth.Camera;
using Xunit;

namespace Photobooth.Tests.Unit;

public class CameraPropertyMapsTests
{
    [Fact]
    public void IsSupportedValue_NullDesc_ReturnsTrue()
    {
        Assert.True(CameraPropertyMaps.IsSupportedValue(null, 0x48));
    }

    [Fact]
    public void IsSupportedValue_EmptyDesc_ReturnsTrue()
    {
        Assert.True(CameraPropertyMaps.IsSupportedValue(Array.Empty<int>(), 0x48));
    }

    [Fact]
    public void IsSupportedValue_ValueInDesc_ReturnsTrue()
    {
        Assert.True(CameraPropertyMaps.IsSupportedValue(new[] { 0x48, 0x58 }, 0x48));
    }

    [Fact]
    public void IsSupportedValue_ValueNotInDesc_ReturnsFalse()
    {
        Assert.False(CameraPropertyMaps.IsSupportedValue(new[] { 0x48, 0x58 }, 0x60));
    }
}
