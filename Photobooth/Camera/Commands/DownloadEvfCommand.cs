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

            uint err = TryDownload();
            return err == EDSDKLib.EDSDK.EDS_ERR_OK || err != EDSDKLib.EDSDK.EDS_ERR_DEVICE_BUSY;
            // true  = done (success or unrecoverable) — CommandProcessor moves on
            // false = camera physically busy — CommandProcessor retries after 500 ms (appropriate)
        }

        // Attempts one EVF download, retrying briefly on OBJECT_NOTREADY without handing
        // control back to CommandProcessor (which would impose a 500 ms penalty).
        private uint TryDownload()
        {
            const ulong bufferSize = 2 * 1024 * 1024;
            const int maxNotReadyRetries = 6;   // 6 × 20 ms = 120 ms max wait
            const int notReadySleepMs   = 20;

            uint err = EDSDKLib.EDSDK.EDS_ERR_OK;

            for (int attempt = 0; attempt <= maxNotReadyRetries; attempt++)
            {
                IntPtr evfImage  = IntPtr.Zero;
                IntPtr stream    = IntPtr.Zero;
                IntPtr dataSetPtr = IntPtr.Zero;

                err = EDSDKLib.EDSDK.EdsCreateMemoryStream(bufferSize, out stream);

                if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
                    err = EDSDKLib.EDSDK.EdsCreateEvfImageRef(stream, out evfImage);

                if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
                    err = EDSDKLib.EDSDK.EdsDownloadEvfImage(_model.Camera, evfImage);

                if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
                {
                    var dataset = new EvfDataSet { Stream = stream };

                    EDSDKLib.EDSDK.EdsGetPropertyData(evfImage, EDSDKLib.EDSDK.PropID_Evf_Zoom,             0, out dataset.Zoom);
                    EDSDKLib.EDSDK.EdsGetPropertyData(evfImage, EDSDKLib.EDSDK.PropID_Evf_ImagePosition,    0, out dataset.ImagePosition);
                    EDSDKLib.EDSDK.EdsGetPropertyData(evfImage, EDSDKLib.EDSDK.PropID_Evf_ZoomRect,         0, out dataset.ZoomRect);
                    EDSDKLib.EDSDK.EdsGetPropertyData(evfImage, EDSDKLib.EDSDK.PropID_Evf_CoordinateSystem, 0, out dataset.SizeJpegLarge);
                    EDSDKLib.EDSDK.EdsGetPropertyData(evfImage, EDSDKLib.EDSDK.PropID_Evf_VisibleRect,      0, out dataset.VisibleRect);

                    _model.SizeJpegLarge = dataset.SizeJpegLarge;
                    _model.SetPropertyRect(EDSDKLib.EDSDK.PropID_Evf_ZoomRect,    dataset.ZoomRect);
                    _model.SetPropertyRect(EDSDKLib.EDSDK.PropID_Evf_VisibleRect, dataset.VisibleRect);

                    dataSetPtr = Marshal.AllocHGlobal(Marshal.SizeOf(dataset));
                    Marshal.StructureToPtr(dataset, dataSetPtr, false);
                    _model.NotifyObservers(new CameraEvent(CameraEvent.Type.EVFDATA_CHANGED, dataSetPtr));
                }

                if (stream    != IntPtr.Zero) EDSDKLib.EDSDK.EdsRelease(stream);
                if (evfImage  != IntPtr.Zero) EDSDKLib.EDSDK.EdsRelease(evfImage);
                if (dataSetPtr != IntPtr.Zero) Marshal.FreeHGlobal(dataSetPtr);

                if (err == EDSDKLib.EDSDK.EDS_ERR_OK) return err;

                // Camera hasn't prepared the next frame yet — wait briefly and retry within
                // this Execute() call rather than returning false to CommandProcessor, which
                // would impose a 500 ms penalty that collapses EVF to ~2 fps.
                if (err == EDSDKLib.EDSDK.EDS_ERR_OBJECT_NOTREADY)
                {
                    if (attempt < maxNotReadyRetries)
                    {
                        Thread.Sleep(notReadySleepMs);
                        continue;
                    }
                    return err; // Exhausted retries; caller skips this frame
                }

                // EDS_ERR_DEVICE_BUSY → surface to caller (returns false → 500 ms retry is correct)
                if (err == EDSDKLib.EDSDK.EDS_ERR_DEVICE_BUSY)
                    return err;

                // Any other error: notify and stop
                _model.NotifyObservers(new CameraEvent(CameraEvent.Type.ERROR, (IntPtr)err));
                return err;
            }

            return err;
        }
    }
}
