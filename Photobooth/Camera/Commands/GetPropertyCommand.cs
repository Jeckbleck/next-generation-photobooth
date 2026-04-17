using System;
using Serilog;

namespace Photobooth.Camera.Commands
{
    internal class GetPropertyCommand : Command
    {
        private readonly uint _propertyID;

        public GetPropertyCommand(ref CameraModel model, uint propertyID) : base(ref model)
            => _propertyID = propertyID;

        public override bool Execute()
        {
            uint err = GetProperty(_propertyID);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                if (err == EDSDKLib.EDSDK.EDS_ERR_DEVICE_BUSY)
                {
                    _model.NotifyObservers(new CameraEvent(CameraEvent.Type.DEVICE_BUSY, IntPtr.Zero));
                    return false;
                }

                // These are expected during EVF / on unsupported camera models — not actionable errors
                if (err == EDSDKLib.EDSDK.EDS_ERR_NOT_SUPPORTED ||
                    err == EDSDKLib.EDSDK.EDS_ERR_PROTECTION_VIOLATION ||
                    err == EDSDKLib.EDSDK.EDS_ERR_PROPERTIES_UNAVAILABLE)
                {
                    Log.Debug("Property 0x{PropertyID:X8} unavailable (0x{Error:X8}) — skipping", _propertyID, err);
                    return true;
                }

                _model.NotifyObservers(new CameraEvent(CameraEvent.Type.ERROR, (IntPtr)err));
            }
            return true;
        }

        private uint GetProperty(uint propertyID)
        {
            uint err = EDSDKLib.EDSDK.EDS_ERR_OK;

            if (propertyID == EDSDKLib.EDSDK.PropID_Unknown)
            {
                uint[] props = {
                    EDSDKLib.EDSDK.PropID_AEModeSelect, EDSDKLib.EDSDK.PropID_DriveMode,
                    EDSDKLib.EDSDK.PropID_WhiteBalance, EDSDKLib.EDSDK.PropID_Tv,
                    EDSDKLib.EDSDK.PropID_Av, EDSDKLib.EDSDK.PropID_ISOSpeed,
                    EDSDKLib.EDSDK.PropID_MeteringMode, EDSDKLib.EDSDK.PropID_ExposureCompensation,
                    EDSDKLib.EDSDK.PropID_ImageQuality, EDSDKLib.EDSDK.PropID_AvailableShots,
                    EDSDKLib.EDSDK.PropID_BatteryLevel, EDSDKLib.EDSDK.PropID_TempStatus,
                    EDSDKLib.EDSDK.PropID_PictureStyle, EDSDKLib.EDSDK.PropID_FixedMovie,
                    EDSDKLib.EDSDK.PropID_MirrorUpSetting, EDSDKLib.EDSDK.PropID_MirrorLockUpState
                };
                foreach (var p in props)
                {
                    if (err == EDSDKLib.EDSDK.EDS_ERR_OK) err = GetProperty(p);
                }
                return err;
            }

            EDSDKLib.EDSDK.EdsDataType dataType;
            int dataSize;
            err = EDSDKLib.EDSDK.EdsGetPropertySize(_model.Camera, propertyID, 0, out dataType, out dataSize);

            if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                if (dataType == EDSDKLib.EDSDK.EdsDataType.UInt32 || dataType == EDSDKLib.EDSDK.EdsDataType.Int32)
                {
                    uint data;
                    err = EDSDKLib.EDSDK.EdsGetPropertyData(_model.Camera, propertyID, 0, out data);
                    if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
                    {
                        if (dataType == EDSDKLib.EDSDK.EdsDataType.UInt32)
                            _model.SetPropertyUInt32(propertyID, data);
                        else
                            _model.SetPropertyInt32(propertyID, data);
                    }
                }
                else if (dataType == EDSDKLib.EDSDK.EdsDataType.String)
                {
                    string str;
                    err = EDSDKLib.EDSDK.EdsGetPropertyData(_model.Camera, propertyID, 0, out str);
                    if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
                        _model.SetPropertyString(propertyID, str);
                }
                else if (dataType == EDSDKLib.EDSDK.EdsDataType.FocusInfo)
                {
                    EDSDKLib.EDSDK.EdsFocusInfo info;
                    err = EDSDKLib.EDSDK.EdsGetPropertyData(_model.Camera, propertyID, 0, out info);
                    if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
                        _model.SetPropertyFocusInfo(propertyID, info);
                }
                else if (dataType == EDSDKLib.EDSDK.EdsDataType.ByteBlock)
                {
                    byte[] data = new byte[dataSize];
                    err = EDSDKLib.EDSDK.EdsGetPropertyData(_model.Camera, propertyID, 0, out data);
                    if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
                        _model.SetPropertyByteBlock(propertyID, data);
                }
            }
            else if (dataType == EDSDKLib.EDSDK.EdsDataType.FocusInfo || err == EDSDKLib.EDSDK.EDS_ERR_PROPERTIES_UNAVAILABLE)
            {
                _model.FocusInfo = default;
                err = EDSDKLib.EDSDK.EDS_ERR_OK;
            }

            if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
                _model.NotifyObservers(new CameraEvent(CameraEvent.Type.PROPERTY_CHANGED, (IntPtr)propertyID));

            return err;
        }
    }
}
