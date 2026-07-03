namespace Photobooth.Camera.Commands
{
    internal class EvfAfCommand : Command
    {
        private readonly bool _start;

        public EvfAfCommand(ref CameraModel model, bool start) : base(ref model)
        {
            _start = start;
        }

        public override bool Execute()
        {
            var param = _start
                ? (int)EDSDKLib.EDSDK.EdsEvfAf.CameraCommand_EvfAf_ON
                : (int)EDSDKLib.EDSDK.EdsEvfAf.CameraCommand_EvfAf_OFF;
            EDSDKLib.EDSDK.EdsSendCommand(_model.Camera, EDSDKLib.EDSDK.CameraCommand_DoEvfAf, param);
            return true;
        }
    }
}
