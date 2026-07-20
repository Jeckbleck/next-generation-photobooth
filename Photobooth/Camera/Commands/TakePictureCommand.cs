using System;
using System.Threading;
using Serilog;

namespace Photobooth.Camera.Commands
{
    internal class TakePictureCommand : Command
    {
        private int _attempt;
        private DateTime? _firstAttemptAt;

        public TakePictureCommand(ref CameraModel model) : base(ref model) { }

        public override bool Execute()
        {
            _attempt++;
            _firstAttemptAt ??= DateTime.UtcNow;

            // Half-press to allow AF to lock, brief wait, then full press
            uint err = EDSDKLib.EDSDK.EdsSendCommand(
                _model.Camera,
                EDSDKLib.EDSDK.CameraCommand_PressShutterButton,
                (int)EDSDKLib.EDSDK.EdsShutterButton.CameraCommand_ShutterButton_Halfway);

            if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                Thread.Sleep(300);

                err = EDSDKLib.EDSDK.EdsSendCommand(
                    _model.Camera,
                    EDSDKLib.EDSDK.CameraCommand_PressShutterButton,
                    (int)EDSDKLib.EDSDK.EdsShutterButton.CameraCommand_ShutterButton_Completely);
            }

            EDSDKLib.EDSDK.EdsSendCommand(
                _model.Camera,
                EDSDKLib.EDSDK.CameraCommand_PressShutterButton,
                (int)EDSDKLib.EDSDK.EdsShutterButton.CameraCommand_ShutterButton_OFF);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                if (err == EDSDKLib.EDSDK.EDS_ERR_DEVICE_BUSY)
                {
                    var elapsedMs = (int)(DateTime.UtcNow - _firstAttemptAt.Value).TotalMilliseconds;
                    Log.Warning("Camera busy during shutter — will retry (attempt {Attempt}, {ElapsedMs}ms since first attempt)",
                        _attempt, elapsedMs);
                    _model.NotifyObservers(new CameraEvent(CameraEvent.Type.DEVICE_BUSY, IntPtr.Zero));
                    return false; // CommandProcessor will retry
                }

                Log.Error("Shutter command failed with error 0x{Error:X8}", err);
                _model.NotifyObservers(new CameraEvent(CameraEvent.Type.ERROR, (IntPtr)err));
                return true;
            }

            if (_attempt > 1)
            {
                var elapsedMs = (int)(DateTime.UtcNow - _firstAttemptAt!.Value).TotalMilliseconds;
                Log.Information("Shutter succeeded after {Attempt} attempts, {ElapsedMs}ms since first attempt",
                    _attempt, elapsedMs);
            }

            _model.CanDownloadImage = true;
            return true;
        }
    }
}
