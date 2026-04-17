using System;

namespace Photobooth.Camera.Commands
{
    internal class SetPropertyCommand : Command
    {
        private readonly uint _propertyID;
        private readonly uint _value;

        public SetPropertyCommand(ref CameraModel model, uint propertyID, uint value) : base(ref model)
        {
            _propertyID = propertyID;
            _value = value;
        }

        public override bool Execute()
        {
            uint err = EDSDKLib.EDSDK.EdsSetPropertyData(_model.Camera, _propertyID, 0, sizeof(uint), _value);

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
