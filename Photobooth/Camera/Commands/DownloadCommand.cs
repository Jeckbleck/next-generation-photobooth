using System;
using System.IO;
using Serilog;

namespace Photobooth.Camera.Commands
{
    internal class DownloadCommand : Command
    {
        private IntPtr _directoryItem;
        private readonly string _destinationDir;
        private readonly Action<string>? _onComplete;

        public DownloadCommand(ref CameraModel model, ref IntPtr inRef, string destinationDir, Action<string>? onComplete)
            : base(ref model)
        {
            _directoryItem = inRef;
            _destinationDir = destinationDir;
            _onComplete = onComplete;
        }

        ~DownloadCommand()
        {
            if (_directoryItem != IntPtr.Zero)
            {
                EDSDKLib.EDSDK.EdsRelease(_directoryItem);
                _directoryItem = IntPtr.Zero;
            }
        }

        public override bool Execute()
        {
            if (!_model.CanDownloadImage) return true;

            uint err;
            IntPtr stream = IntPtr.Zero;

            EDSDKLib.EDSDK.EdsDirectoryItemInfo dirItemInfo;
            err = EDSDKLib.EDSDK.EdsGetDirectoryItemInfo(_directoryItem, out dirItemInfo);

            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                _model.NotifyObservers(new CameraEvent(CameraEvent.Type.ERROR, (IntPtr)err));
                return true;
            }

            _model.NotifyObservers(new CameraEvent(CameraEvent.Type.DOWNLOAD_START, IntPtr.Zero));

            string destPath = Path.Combine(_destinationDir, dirItemInfo.szFileName);
            Log.Debug("Downloading photo to {Path}", destPath);

            err = EDSDKLib.EDSDK.EdsCreateFileStream(destPath,
                EDSDKLib.EDSDK.EdsFileCreateDisposition.CreateAlways,
                EDSDKLib.EDSDK.EdsAccess.ReadWrite, out stream);

            if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
                err = EDSDKLib.EDSDK.EdsSetProgressCallback(stream, ProgressCallback,
                    EDSDKLib.EDSDK.EdsProgressOption.Periodically, stream);

            if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
                err = EDSDKLib.EDSDK.EdsDownload(_directoryItem, dirItemInfo.Size, stream);

            if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
                err = EDSDKLib.EDSDK.EdsDownloadComplete(_directoryItem);

            if (_directoryItem != IntPtr.Zero)
            {
                EDSDKLib.EDSDK.EdsRelease(_directoryItem);
                _directoryItem = IntPtr.Zero;
            }

            if (stream != IntPtr.Zero)
            {
                EDSDKLib.EDSDK.EdsRelease(stream);
                stream = IntPtr.Zero;
            }

            if (err == EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                Log.Information("Photo saved: {Path}", destPath);
                _model.NotifyObservers(new CameraEvent(CameraEvent.Type.DOWNLOAD_COMPLETE, IntPtr.Zero));
                _onComplete?.Invoke(destPath);
            }
            else
            {
                _model.NotifyObservers(new CameraEvent(CameraEvent.Type.ERROR, (IntPtr)err));
            }

            return true;
        }

        private uint ProgressCallback(uint inPercent, IntPtr inContext, ref bool outCancel)
        {
            _model.NotifyObservers(new CameraEvent(CameraEvent.Type.PROGRESS, (IntPtr)inPercent));
            return 0;
        }
    }
}
