using System;

namespace Photobooth.Camera.Commands
{
    internal class StartEvfCommand : Command
    {
        public StartEvfCommand(ref CameraModel model) : base(ref model) { }

        public override bool Execute()
        {
            uint err = EDSDKLib.EDSDK.EDS_ERR_OK;

            uint evfMode = _model.EvfMode;
            if (evfMode == 0)
            {
                evfMode = 1;
                err = EDSDKLib.EDSDK.EdsSetPropertyData(_model.Camera, EDSDKLib.EDSDK.PropID_Evf_Mode, 0, sizeof(uint), evfMode);
            }

            if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                uint device = _model.EvfOutputDevice;
                device |= EDSDKLib.EDSDK.EvfOutputDevice_PC;
                err = EDSDKLib.EDSDK.EdsSetPropertyData(_model.Camera, EDSDKLib.EDSDK.PropID_Evf_OutputDevice, 0, sizeof(uint), device);
            }

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
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
