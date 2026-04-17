using System;

namespace Photobooth.Camera.Commands
{
    internal class OpenSessionCommand : Command
    {
        public OpenSessionCommand(ref CameraModel model) : base(ref model) { }

        public override bool Execute()
        {
            uint err;

            // Enable private properties (Canon sample pattern)
            EDSDKLib.EDSDK.EdsSetPropertyData(_model.Camera, 0x01000000, 0x14840DF1, sizeof(uint), EDSDKLib.EDSDK.PropID_TempStatus);
            EDSDKLib.EDSDK.EdsSetPropertyData(_model.Camera, 0x01000000, 0x05B3740D, sizeof(uint), EDSDKLib.EDSDK.PropID_Evf_RollingPitching);
            EDSDKLib.EDSDK.EdsSetPropertyData(_model.Camera, 0x01000000, 0x17AF25B1, sizeof(uint), EDSDKLib.EDSDK.PropID_FixedMovie);
            EDSDKLib.EDSDK.EdsSetPropertyData(_model.Camera, 0x01000000, 0x2A0C1274, sizeof(uint), EDSDKLib.EDSDK.PropID_MovieParam);
            EDSDKLib.EDSDK.EdsSetPropertyData(_model.Camera, 0x01000000, 0x3FB1718B, sizeof(uint), EDSDKLib.EDSDK.PropID_Aspect);
            EDSDKLib.EDSDK.EdsSetPropertyData(_model.Camera, 0x01000000, 0x653048A9, sizeof(uint), EDSDKLib.EDSDK.PropID_Evf_ClickWBCoeffs);
            EDSDKLib.EDSDK.EdsSetPropertyData(_model.Camera, 0x01000000, 0x4D2879F3, sizeof(uint), EDSDKLib.EDSDK.PropID_Evf_VisibleRect);
            EDSDKLib.EDSDK.EdsSetPropertyData(_model.Camera, 0x01000000, 0x517F095D, sizeof(uint), EDSDKLib.EDSDK.PropID_MirrorUpSetting);
            EDSDKLib.EDSDK.EdsSetPropertyData(_model.Camera, 0x01000000, 0x00E13499, sizeof(uint), EDSDKLib.EDSDK.PropID_MirrorLockUpState);
            EDSDKLib.EDSDK.EdsSetPropertyData(_model.Camera, 0x01000000, 0x1C31565B, sizeof(uint), EDSDKLib.EDSDK.PropID_AutoPowerOffSetting);
            EDSDKLib.EDSDK.EdsSetPropertyData(_model.Camera, 0x01000000, 0x44396197, sizeof(uint), EDSDKLib.EDSDK.PropID_MovieHFRSetting);

            err = EDSDKLib.EDSDK.EdsOpenSession(_model.Camera);

            if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                uint fixedMovie;
                EDSDKLib.EDSDK.EdsGetPropertyData(_model.Camera, EDSDKLib.EDSDK.PropID_FixedMovie, 0, out fixedMovie);

                if (fixedMovie == 0)
                {
                    err = EDSDKLib.EDSDK.EdsSetPropertyData(_model.Camera, EDSDKLib.EDSDK.PropID_SaveTo, 0, sizeof(uint), (uint)EDSDKLib.EDSDK.EdsSaveTo.Host);
                    if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
                    {
                        var capacity = new EDSDKLib.EDSDK.EdsCapacity
                        {
                            NumberOfFreeClusters = 0x7FFFFFFF,
                            BytesPerSector = 0x1000,
                            Reset = 1
                        };
                        EDSDKLib.EDSDK.EdsSetCapacity(_model.Camera, capacity);
                    }
                }
                else
                {
                    EDSDKLib.EDSDK.EdsSetPropertyData(_model.Camera, EDSDKLib.EDSDK.PropID_SaveTo, 0, sizeof(uint), (uint)EDSDKLib.EDSDK.EdsSaveTo.Camera);
                }
            }

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                _model.NotifyObservers(new CameraEvent(CameraEvent.Type.ERROR, (IntPtr)err));

            return true;
        }
    }
}
