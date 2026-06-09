using System;

namespace Photobooth.Camera;

internal struct EvfDataSet
{
    public IntPtr Stream;
    public uint Zoom;
    public EDSDKLib.EDSDK.EdsRect ZoomRect;
    public EDSDKLib.EDSDK.EdsRect VisibleRect;
    public EDSDKLib.EDSDK.EdsPoint ImagePosition;
    public EDSDKLib.EDSDK.EdsSize SizeJpegLarge;
}
