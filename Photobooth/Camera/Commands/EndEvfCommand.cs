using System;

namespace Photobooth.Camera.Commands
{
    internal class EndEvfCommand : Command
    {
        public EndEvfCommand(ref CameraModel model) : base(ref model) { }

        public override bool Execute()
        {
            uint device = _model.EvfOutputDevice;
            device &= ~EDSDKLib.EDSDK.EvfOutputDevice_PC;
            uint err = EDSDKLib.EDSDK.EdsSetPropertyData(_model.Camera, EDSDKLib.EDSDK.PropID_Evf_OutputDevice, 0, sizeof(uint), device);

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
