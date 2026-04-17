using System;

namespace Photobooth.Camera.Commands
{
    internal class GetPropertyDescCommand : Command
    {
        private readonly uint _propertyID;

        public GetPropertyDescCommand(ref CameraModel model, uint propertyID) : base(ref model)
            => _propertyID = propertyID;

        public override bool Execute()
        {
            EDSDKLib.EDSDK.EdsPropertyDesc desc;
            uint err = EDSDKLib.EDSDK.EdsGetPropertyDesc(_model.Camera, _propertyID, out desc);

            if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                _model.NotifyObservers(new CameraEvent(CameraEvent.Type.PROPERTY_DESC_CHANGED, (IntPtr)_propertyID));
            }
            else if (err == EDSDKLib.EDSDK.EDS_ERR_DEVICE_BUSY)
            {
                _model.NotifyObservers(new CameraEvent(CameraEvent.Type.DEVICE_BUSY, IntPtr.Zero));
                return false;
            }
            else
            {
                _model.NotifyObservers(new CameraEvent(CameraEvent.Type.ERROR, (IntPtr)err));
            }
            return true;
        }
    }
}
