using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Photobooth.Camera.Commands;
using Serilog;

namespace Photobooth.Camera
{
    public class CameraService : IDisposable, IObserver
    {
        private CameraModel? _model;
        private CommandProcessor? _processor;
        private GCHandle _selfHandle;

        private EDSDKLib.EDSDK.EdsPropertyEventHandler? _propHandler;
        private EDSDKLib.EDSDK.EdsObjectEventHandler? _objHandler;
        private EDSDKLib.EDSDK.EdsStateEventHandler? _stateHandler;

        public event EventHandler<BitmapSource>? EvfFrameReady;
        public event EventHandler? CameraDisconnected;
        public event EventHandler<string>? Error;

        private bool _sdkInitialized;
        private IntPtr _cameraRef;

        // Set before calling TakePictureAsync so downloads go to the right folder
        public string SessionDirectory { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Photobooth");

        // Resolves when a download completes, carrying the saved file path
        private TaskCompletionSource<string>? _pendingDownload;

        // --- Lifecycle -----------------------------------------------------------

        public bool Initialize()
        {
            Log.Information("Initializing EDSDK");

            uint err = EDSDKLib.EDSDK.EdsInitializeSDK();
            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                Log.Error("EdsInitializeSDK failed 0x{Error:X8}", err);
                return false;
            }
            _sdkInitialized = true;

            IntPtr cameraList;
            err = EDSDKLib.EDSDK.EdsGetCameraList(out cameraList);
            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                Log.Error("EdsGetCameraList failed 0x{Error:X8}", err);
                return false;
            }

            int count;
            EDSDKLib.EDSDK.EdsGetChildCount(cameraList, out count);
            Log.Information("Cameras detected: {Count}", count);

            if (count == 0)
            {
                EDSDKLib.EDSDK.EdsRelease(cameraList);
                Log.Warning("No camera found");
                return false;
            }

            err = EDSDKLib.EDSDK.EdsGetChildAtIndex(cameraList, 0, out _cameraRef);
            EDSDKLib.EDSDK.EdsRelease(cameraList);
            if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                Log.Error("EdsGetChildAtIndex failed 0x{Error:X8}", err);
                return false;
            }

            _model = new CameraModel(_cameraRef);
            IObserver self = this;
            _model.Add(ref self);

            _selfHandle = GCHandle.Alloc(this);
            IntPtr ptr = GCHandle.ToIntPtr(_selfHandle);

            _propHandler  = HandlePropertyEvent;
            _objHandler   = HandleObjectEvent;
            _stateHandler = HandleStateEvent;

            EDSDKLib.EDSDK.EdsSetPropertyEventHandler(_cameraRef, EDSDKLib.EDSDK.PropertyEvent_All, _propHandler, ptr);
            EDSDKLib.EDSDK.EdsSetObjectEventHandler(_cameraRef, EDSDKLib.EDSDK.ObjectEvent_All, _objHandler, ptr);
            EDSDKLib.EDSDK.EdsSetCameraStateEventHandler(_cameraRef, EDSDKLib.EDSDK.StateEvent_All, _stateHandler, ptr);

            _processor = new CommandProcessor();
            _processor.Start();
            _processor.PostCommand(new OpenSessionCommand(ref _model));
            _processor.PostCommand(new GetPropertyCommand(ref _model, EDSDKLib.EDSDK.PropID_ProductName));

            GC.KeepAlive(_propHandler);
            GC.KeepAlive(_objHandler);
            GC.KeepAlive(_stateHandler);

            Log.Information("Camera session opened successfully");
            return true;
        }

        // --- Live View -----------------------------------------------------------

        public void StartLiveView()
        {
            if (_model == null || _processor == null) return;
            Log.Information("Starting live view (EVF)");
            _model.IsEvfEnabled = true;
            _processor.PostCommand(new StartEvfCommand(ref _model));
        }

        public void StopLiveView()
        {
            if (_model == null || _processor == null) return;
            Log.Information("Stopping live view (EVF)");
            _model.IsEvfEnabled = false;
            _processor.PostCommand(new EndEvfCommand(ref _model));
        }

        public void RequestEvfFrame()
        {
            if (_model == null || _processor == null) return;
            _processor.PostCommand(new DownloadEvfCommand(ref _model));
        }

        // --- Capture -------------------------------------------------------------

        /// <summary>
        /// Triggers the shutter and waits for the photo to fully download.
        /// Returns the saved file path, or throws on timeout/error.
        /// </summary>
        public async Task<string> TakePictureAsync(CancellationToken ct = default)
        {
            if (_model == null || _processor == null)
                throw new InvalidOperationException("Camera not initialized");

            Directory.CreateDirectory(SessionDirectory);

            _pendingDownload = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Block EVF queue from re-filling so TakePictureCommand runs immediately
            _model.IsCapturing = true;
            Log.Information("Triggering shutter — waiting for download");
            _processor.PostCommand(new TakePictureCommand(ref _model));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            using (linked.Token.Register(() =>
            {
                if (_model != null) _model.IsCapturing = false;
                _pendingDownload.TrySetCanceled();
            }))
            {
                return await _pendingDownload.Task;
            }
        }

        // --- IObserver -----------------------------------------------------------

        public void Update(Observable observable, CameraEvent e)
        {
            switch (e.EventType)
            {
                case CameraEvent.Type.EVFDATA_CHANGED:
                    HandleEvfData(e.Arg);
                    break;

                case CameraEvent.Type.DOWNLOAD_START:
                    Log.Debug("Photo download started");
                    break;

                case CameraEvent.Type.DEVICE_BUSY:
                    Log.Warning("Camera reported device busy");
                    break;

                case CameraEvent.Type.SHUT_DOWN:
                    Log.Warning("Camera disconnected (StateEvent_Shutdown)");
                    CameraDisconnected?.Invoke(this, EventArgs.Empty);
                    break;

                case CameraEvent.Type.ERROR:
                    Log.Error("Camera error 0x{Error:X8}", e.Arg);
                    Error?.Invoke(this, $"EDSDK error 0x{e.Arg:X8}");
                    break;
            }
        }

        // Called by DownloadCommand on successful save
        private void OnPhotoSaved(string path)
        {
            Log.Information("Photo download complete: {Path}", path);
            if (_model != null) _model.IsCapturing = false;
            _pendingDownload?.TrySetResult(path);
        }

        // --- SDK event callbacks -------------------------------------------------

        private uint HandleObjectEvent(uint inEvent, IntPtr inRef, IntPtr inContext)
        {
            if (inEvent == EDSDKLib.EDSDK.ObjectEvent_DirItemRequestTransfer && _model != null && _processor != null)
            {
                Log.Debug("ObjectEvent_DirItemRequestTransfer — queuing download to {Dir}", SessionDirectory);
                _processor.PostCommand(new DownloadCommand(ref _model, ref inRef, SessionDirectory, OnPhotoSaved));
            }
            else if (inRef != IntPtr.Zero)
            {
                EDSDKLib.EDSDK.EdsRelease(inRef);
            }
            return EDSDKLib.EDSDK.EDS_ERR_OK;
        }

        private uint HandlePropertyEvent(uint inEvent, uint inPropertyID, uint inParam, IntPtr inContext)
        {
            if (_model == null || _processor == null) return EDSDKLib.EDSDK.EDS_ERR_OK;

            if (inEvent == EDSDKLib.EDSDK.PropertyEvent_PropertyChanged)
            {
                Log.Debug("Property changed: 0x{PropertyID:X8}", inPropertyID);
                _processor.PostCommand(new GetPropertyCommand(ref _model, inPropertyID));
            }
            else if (inEvent == EDSDKLib.EDSDK.PropertyEvent_PropertyDescChanged)
            {
                _processor.PostCommand(new GetPropertyDescCommand(ref _model, inPropertyID));
            }
            return EDSDKLib.EDSDK.EDS_ERR_OK;
        }

        private uint HandleStateEvent(uint inEvent, uint inParameter, IntPtr inContext)
        {
            if (inEvent == EDSDKLib.EDSDK.StateEvent_Shutdown)
            {
                Log.Warning("Camera state event: Shutdown");
                CameraDisconnected?.Invoke(this, EventArgs.Empty);
            }
            return EDSDKLib.EDSDK.EDS_ERR_OK;
        }

        // --- EVF frame decoding --------------------------------------------------

        private void HandleEvfData(IntPtr dataSetPtr)
        {
            if (dataSetPtr == IntPtr.Zero) return;

            try
            {
                var dataset = Marshal.PtrToStructure<EvfDataSet>(dataSetPtr);
                if (dataset.Stream == IntPtr.Zero) return;

                EDSDKLib.EDSDK.EdsGetPointer(dataset.Stream, out IntPtr pointer);
                EDSDKLib.EDSDK.EdsGetLength(dataset.Stream, out ulong length);

                byte[] buffer = new byte[length];
                Marshal.Copy(pointer, buffer, 0, (int)length);

                using var ms = new MemoryStream(buffer);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();

                EvfFrameReady?.Invoke(this, bitmap);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to decode EVF frame");
            }
        }

        // --- Cleanup -------------------------------------------------------------

        public void Dispose()
        {
            Log.Information("Closing camera session");
            _processor?.PostCommand(new CloseSessionCommand(ref _model!));
            _processor?.Stop();

            if (_cameraRef != IntPtr.Zero)
            {
                EDSDKLib.EDSDK.EdsRelease(_cameraRef);
                _cameraRef = IntPtr.Zero;
            }

            if (_selfHandle.IsAllocated)
                _selfHandle.Free();

            if (_sdkInitialized)
            {
                EDSDKLib.EDSDK.EdsTerminateSDK();
                _sdkInitialized = false;
                Log.Information("EDSDK terminated");
            }
        }
    }
}
