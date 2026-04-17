using System;

namespace Photobooth.Camera.Commands
{
    internal class CloseSessionCommand : Command
    {
        public CloseSessionCommand(ref CameraModel model) : base(ref model) { }

        public override bool Execute()
        {
            uint err = EDSDKLib.EDSDK.EdsCloseSession(_model.Camera);
            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                _model.NotifyObservers(new CameraEvent(CameraEvent.Type.ERROR, (IntPtr)err));
            return true;
        }
    }
}
