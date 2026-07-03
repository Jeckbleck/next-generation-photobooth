using System;
using Serilog;

namespace Photobooth.Camera.Commands
{
    internal class WarmupShutterCommand : Command
    {
        public WarmupShutterCommand(ref CameraModel model) : base(ref model) { }

        public override bool Execute()
        {
            // Fire without AF — used to complete a capture cycle and restore EVF after an error.
            uint err = EDSDKLib.EDSDK.EdsSendCommand(
                _model.Camera,
                EDSDKLib.EDSDK.CameraCommand_PressShutterButton,
                (int)EDSDKLib.EDSDK.EdsShutterButton.CameraCommand_ShutterButton_Completely_NonAF);

            EDSDKLib.EDSDK.EdsSendCommand(
                _model.Camera,
                EDSDKLib.EDSDK.CameraCommand_PressShutterButton,
                (int)EDSDKLib.EDSDK.EdsShutterButton.CameraCommand_ShutterButton_OFF);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                Log.Warning("Warmup shutter returned 0x{Error:X8} — EVF recovery may take longer", err);

            return true;
        }
    }
}
