using System;

namespace Photobooth.Camera
{
    public class CameraModel : Observable
    {
        private const uint Unknown = 0xffffffff;

        public IntPtr Camera { get; set; }
        public string? ModelName { get; set; }
        public bool IsTypeDS { get; set; }

        public uint AEMode { get; set; } = Unknown;
        public uint AFMode { get; set; } = Unknown;
        public uint DriveMode { get; set; } = Unknown;
        public uint WhiteBalance { get; set; } = Unknown;
        public uint Av { get; set; } = Unknown;
        public uint Tv { get; set; } = Unknown;
        public uint Iso { get; set; } = Unknown;
        public uint MeteringMode { get; set; } = Unknown;
        public uint ExposureCompensation { get; set; } = Unknown;
        public uint ImageQuality { get; set; } = Unknown;
        public uint EvfMode { get; set; } = Unknown;
        public uint StartupEvfOutputDevice { get; set; }
        public uint EvfOutputDevice { get; set; } = Unknown;
        public uint EvfDepthOfFieldPreview { get; set; } = Unknown;
        public uint EvfAFMode { get; set; } = Unknown;
        public uint BatteryLevel { get; set; } = Unknown;
        public uint Zoom { get; set; } = Unknown;
        public uint FlashMode { get; set; } = Unknown;
        public uint AvailableShot { get; set; }
        public uint TempStatus { get; set; } = Unknown;
        public uint RollPitch { get; set; } = 1;
        public uint MovieQuality { get; set; } = Unknown;
        public uint MovieHFR { get; set; } = Unknown;
        public uint PictureStyle { get; set; } = Unknown;
        public uint Aspect { get; set; } = Unknown;
        public uint FixedMovie { get; set; } = Unknown;
        public uint MirrorUpSetting { get; set; } = Unknown;
        public uint MirrorLockUpState { get; set; } = Unknown;
        public uint AutoPowerOff { get; set; } = Unknown;
        public bool CanDownloadImage { get; set; } = true;
        public bool IsEvfEnabled { get; set; }
        public bool IsCapturing { get; set; }
        public byte[]? ClickWB { get; set; }
        public EDSDKLib.EDSDK.EdsFocusInfo FocusInfo { get; set; }
        public EDSDKLib.EDSDK.EdsRect ZoomRect { get; set; }
        public EDSDKLib.EDSDK.EdsRect VisibleRect { get; set; }
        public EDSDKLib.EDSDK.EdsSize SizeJpegLarge { get; set; }

        public CameraModel(IntPtr camera)
        {
            Camera = camera;
        }

        public void SetPropertyUInt32(uint propertyID, uint value)
        {
            switch (propertyID)
            {
                case EDSDKLib.EDSDK.PropID_AEModeSelect: AEMode = value; break;
                case EDSDKLib.EDSDK.PropID_AFMode: AFMode = value; break;
                case EDSDKLib.EDSDK.PropID_DriveMode: DriveMode = value; break;
                case EDSDKLib.EDSDK.PropID_Tv: Tv = value; break;
                case EDSDKLib.EDSDK.PropID_Av: Av = value; break;
                case EDSDKLib.EDSDK.PropID_ISOSpeed: Iso = value; break;
                case EDSDKLib.EDSDK.PropID_MeteringMode: MeteringMode = value; break;
                case EDSDKLib.EDSDK.PropID_ExposureCompensation: ExposureCompensation = value; break;
                case EDSDKLib.EDSDK.PropID_ImageQuality: ImageQuality = value; break;
                case EDSDKLib.EDSDK.PropID_Evf_Mode: EvfMode = value; break;
                case EDSDKLib.EDSDK.PropID_Evf_OutputDevice:
                    if (EvfOutputDevice == Unknown) StartupEvfOutputDevice = value;
                    EvfOutputDevice = value;
                    break;
                case EDSDKLib.EDSDK.PropID_Evf_DepthOfFieldPreview: EvfDepthOfFieldPreview = value; break;
                case EDSDKLib.EDSDK.PropID_Evf_AFMode: EvfAFMode = value; break;
                case EDSDKLib.EDSDK.PropID_AvailableShots: AvailableShot = value; break;
                case EDSDKLib.EDSDK.PropID_DC_Zoom: Zoom = value; break;
                case EDSDKLib.EDSDK.PropID_DC_Strobe: FlashMode = value; break;
                case EDSDKLib.EDSDK.PropID_TempStatus: TempStatus = value; break;
                case EDSDKLib.EDSDK.PropID_PictureStyle: PictureStyle = value; break;
                case EDSDKLib.EDSDK.PropID_MovieHFRSetting: MovieHFR = value; break;
                case EDSDKLib.EDSDK.PropID_Aspect: Aspect = value; break;
                case EDSDKLib.EDSDK.PropID_FixedMovie: FixedMovie = value; break;
                case EDSDKLib.EDSDK.PropID_MirrorUpSetting: MirrorUpSetting = value; break;
                case EDSDKLib.EDSDK.PropID_MirrorLockUpState: MirrorLockUpState = value; break;
                case EDSDKLib.EDSDK.PropID_AutoPowerOffSetting: AutoPowerOff = value; break;
            }
        }

        public void SetPropertyInt32(uint propertyID, uint value)
        {
            switch (propertyID)
            {
                case EDSDKLib.EDSDK.PropID_WhiteBalance: WhiteBalance = value; break;
                case EDSDKLib.EDSDK.PropID_BatteryLevel: BatteryLevel = value; break;
            }
        }

        public void SetPropertyString(uint propertyID, string str)
        {
            if (propertyID == EDSDKLib.EDSDK.PropID_ProductName)
            {
                ModelName = str;
                IsTypeDS = str.Contains("EOS");
            }
        }

        public void SetPropertyFocusInfo(uint propertyID, EDSDKLib.EDSDK.EdsFocusInfo info)
        {
            if (propertyID == EDSDKLib.EDSDK.PropID_FocusInfo)
                FocusInfo = info;
        }

        public void SetPropertyByteBlock(uint propertyID, byte[] data)
        {
            switch (propertyID)
            {
                case EDSDKLib.EDSDK.PropID_MovieParam: MovieQuality = BitConverter.ToUInt32(data, 0); break;
                case EDSDKLib.EDSDK.PropID_Evf_ClickWBCoeffs: ClickWB = data; break;
            }
        }

        public void SetPropertyRect(uint propertyID, EDSDKLib.EDSDK.EdsRect info)
        {
            switch (propertyID)
            {
                case EDSDKLib.EDSDK.PropID_Evf_ZoomRect: ZoomRect = info; break;
                case EDSDKLib.EDSDK.PropID_Evf_VisibleRect: VisibleRect = info; break;
            }
        }

        public EDSDKLib.EDSDK.EdsPoint GetZoomPosition() =>
            new() { x = ZoomRect.x, y = ZoomRect.y };
    }
}
