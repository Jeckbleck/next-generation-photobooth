using System;
using System.Runtime.InteropServices;

namespace Photobooth.Camera.Commands
{
    public struct EvfDataSet
    {
        public IntPtr Stream;
        public uint Zoom;
        public EDSDKLib.EDSDK.EdsRect ZoomRect;
        public EDSDKLib.EDSDK.EdsRect VisibleRect;
        public EDSDKLib.EDSDK.EdsPoint ImagePosition;
        public EDSDKLib.EDSDK.EdsSize SizeJpegLarge;
    }

    internal class DownloadEvfCommand : Command
    {
        public DownloadEvfCommand(ref CameraModel model) : base(ref model) { }

        public override bool Execute()
        {
            // Silently skip — don't retry — so the queue drains quickly for TakePictureCommand
            if (_model.IsCapturing) return true;

            if ((_model.EvfOutputDevice & EDSDKLib.EDSDK.EvfOutputDevice_PC) == 0)
                return true;

            uint err;
            IntPtr evfImage = IntPtr.Zero;
            IntPtr stream = IntPtr.Zero;
            IntPtr dataSetPtr = IntPtr.Zero;
            const ulong bufferSize = 2 * 1024 * 1024;

            err = EDSDKLib.EDSDK.EdsCreateMemoryStream(bufferSize, out stream);

            if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
                err = EDSDKLib.EDSDK.EdsCreateEvfImageRef(stream, out evfImage);

            if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
                err = EDSDKLib.EDSDK.EdsDownloadEvfImage(_model.Camera, evfImage);

            if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                var dataset = new EvfDataSet { Stream = stream };

                EDSDKLib.EDSDK.EdsGetPropertyData(evfImage, EDSDKLib.EDSDK.PropID_Evf_Zoom, 0, out dataset.Zoom);
                EDSDKLib.EDSDK.EdsGetPropertyData(evfImage, EDSDKLib.EDSDK.PropID_Evf_ImagePosition, 0, out dataset.ImagePosition);
                EDSDKLib.EDSDK.EdsGetPropertyData(evfImage, EDSDKLib.EDSDK.PropID_Evf_ZoomRect, 0, out dataset.ZoomRect);
                EDSDKLib.EDSDK.EdsGetPropertyData(evfImage, EDSDKLib.EDSDK.PropID_Evf_CoordinateSystem, 0, out dataset.SizeJpegLarge);
                EDSDKLib.EDSDK.EdsGetPropertyData(evfImage, EDSDKLib.EDSDK.PropID_Evf_VisibleRect, 0, out dataset.VisibleRect);

                _model.SizeJpegLarge = dataset.SizeJpegLarge;
                _model.SetPropertyRect(EDSDKLib.EDSDK.PropID_Evf_ZoomRect, dataset.ZoomRect);
                _model.SetPropertyRect(EDSDKLib.EDSDK.PropID_Evf_VisibleRect, dataset.VisibleRect);

                dataSetPtr = Marshal.AllocHGlobal(Marshal.SizeOf(dataset));
                Marshal.StructureToPtr(dataset, dataSetPtr, false);
                _model.NotifyObservers(new CameraEvent(CameraEvent.Type.EVFDATA_CHANGED, dataSetPtr));
            }

            if (stream != IntPtr.Zero) EDSDKLib.EDSDK.EdsRelease(stream);
            if (evfImage != IntPtr.Zero) EDSDKLib.EDSDK.EdsRelease(evfImage);
            if (dataSetPtr != IntPtr.Zero) Marshal.FreeHGlobal(dataSetPtr);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                if (err == EDSDKLib.EDSDK.EDS_ERR_OBJECT_NOTREADY) return false;
                if (err == EDSDKLib.EDSDK.EDS_ERR_DEVICE_BUSY)
                {
                    _model.NotifyObservers(new CameraEvent(CameraEvent.Type.DEVICE_BUSY, IntPtr.Zero));
                    return false;
                }
                _model.NotifyObservers(new CameraEvent(CameraEvent.Type.ERROR, (IntPtr)err));
            }
            return true;
        }
    }
}
