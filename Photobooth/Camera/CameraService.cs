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
        public event EventHandler<uint>? CameraPropertyChanged;
        public event EventHandler? DeviceBusy;

        private bool _sdkInitialized;
        private IntPtr _cameraRef;

        public bool IsConnected { get; private set; }
        public string? ModelName => _model?.ModelName;
        public int RotationDegrees { get; set; }

        public string SessionDirectory { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Photobooth");

        internal TaskCompletionSource<string>? _pendingDownload;
        private TaskCompletionSource<string>? _pendingWarmup;
        private volatile bool _discardNextDownload;

        // --- Lifecycle -----------------------------------------------------------

        public bool Initialize()
        {
            Log.Information("Initializing EDSDK");

            // Tear down any previous session so Initialize() is safe to call again (e.g. reconnect)
            TearDownCameraSession();

            if (!_sdkInitialized)
            {
                uint err = EDSDKLib.EDSDK.EdsInitializeSDK();
                if (err != EDSDKLib.EDSDK.EDS_ERR_OK)
                {
                    Log.Error("EdsInitializeSDK failed 0x{Error:X8}", err);
                    IsConnected = false;
                    return false;
                }
                _sdkInitialized = true;
            }

            IntPtr cameraList;
            uint listErr = EDSDKLib.EDSDK.EdsGetCameraList(out cameraList);
            if (listErr != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                Log.Error("EdsGetCameraList failed 0x{Error:X8}", listErr);
                IsConnected = false;
                return false;
            }

            int count;
            EDSDKLib.EDSDK.EdsGetChildCount(cameraList, out count);
            Log.Information("Cameras detected: {Count}", count);

            if (count == 0)
            {
                EDSDKLib.EDSDK.EdsRelease(cameraList);
                Log.Warning("No camera found");
                IsConnected = false;
                return false;
            }

            uint getErr = EDSDKLib.EDSDK.EdsGetChildAtIndex(cameraList, 0, out _cameraRef);
            EDSDKLib.EDSDK.EdsRelease(cameraList);
            if (getErr != EDSDKLib.EDSDK.EDS_ERR_OK)
            {
                Log.Error("EdsGetChildAtIndex failed 0x{Error:X8}", getErr);
                IsConnected = false;
                return false;
            }

            _model = new CameraModel(_cameraRef);
            IObserver self = this;
            _model.Add(ref self);

            if (!_selfHandle.IsAllocated)
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

            IsConnected = true;
            Log.Information("Camera session opened successfully");
            return true;
        }

        private void TearDownCameraSession()
        {
            if (_cameraRef == IntPtr.Zero) return;

            _processor?.Stop();
            _processor = null;

            EDSDKLib.EDSDK.EdsRelease(_cameraRef);
            _cameraRef = IntPtr.Zero;
            _model = null;
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

        public void StartEvfAf()
        {
            if (_model == null || _processor == null) return;
            _processor.PostCommand(new EvfAfCommand(ref _model, start: true));
        }

        public void StopEvfAf()
        {
            if (_model == null || _processor == null) return;
            _processor.PostCommand(new EvfAfCommand(ref _model, start: false));
        }

        public void RequestEvfFrame()
        {
            if (_model == null || _processor == null) return;
            _processor.PostCommand(new DownloadEvfCommand(ref _model));
        }

        // --- Capture -------------------------------------------------------------

        public async Task<string> TakePictureAsync(CancellationToken ct = default)
        {
            if (_model == null || _processor == null)
                throw new InvalidOperationException("Camera not initialized");

            Directory.CreateDirectory(SessionDirectory);

            _pendingDownload = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

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

        public async Task FireWarmupShotAsync(CancellationToken ct = default)
        {
            if (_model == null || _processor == null) return;

            _pendingWarmup = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _discardNextDownload = true;
            Log.Information("Firing non-AF warmup shot to restore EVF");
            _processor.PostCommand(new WarmupShutterCommand(ref _model));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var linked  = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try
            {
                using (linked.Token.Register(() => _pendingWarmup.TrySetCanceled()))
                {
                    string path = await _pendingWarmup.Task;
                    try { File.Delete(path); } catch { }
                }
            }
            catch (OperationCanceledException)
            {
                _discardNextDownload = false;
            }
        }

        // --- Settings API --------------------------------------------------------

        public void SetProperty(uint propertyID, uint value)
        {
            if (_model == null || _processor == null) return;
            _processor.PostCommand(new SetPropertyCommand(ref _model, propertyID, value));
        }

        private static readonly TimeSpan PropertyConfirmTimeout = TimeSpan.FromSeconds(6);

        // Applies a property and waits for the camera to confirm the change via
        // CameraPropertyChanged, rather than assuming success as soon as the
        // command is queued. Resolves false if no matching event arrives before
        // the timeout (or ct is cancelled) — e.g. the camera disconnected mid-apply.
        public async Task<bool> SetPropertyAsync(uint propertyId, uint value, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnPropertyChanged(object? sender, uint changedId)
            {
                if (changedId == propertyId) tcs.TrySetResult(true);
            }

            CameraPropertyChanged += OnPropertyChanged;
            try
            {
                SetProperty(propertyId, value);

                using var timeoutCts = new CancellationTokenSource(PropertyConfirmTimeout);
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                using (linked.Token.Register(() => tcs.TrySetResult(false)))
                {
                    return await tcs.Task;
                }
            }
            finally
            {
                CameraPropertyChanged -= OnPropertyChanged;
            }
        }

        public uint? GetPropertyValue(uint propertyID)
        {
            if (_model == null) return null;
            uint v = _model.GetPropertyValue(propertyID);
            return v == 0xffffffff ? null : v;
        }

        public int[]? GetPropertyDesc(uint propertyID)
        {
            if (_model == null) return null;
            return _model.PropertyDescs.TryGetValue(propertyID, out var desc) ? desc : null;
        }

        public void RequestPropertyDescs()
        {
            if (_model == null || _processor == null) return;
            foreach (uint propId in new[]
            {
                EDSDKLib.EDSDK.PropID_ISOSpeed,
                EDSDKLib.EDSDK.PropID_Tv,
                EDSDKLib.EDSDK.PropID_Av,
                EDSDKLib.EDSDK.PropID_WhiteBalance,
                EDSDKLib.EDSDK.PropID_ImageQuality,
                EDSDKLib.EDSDK.PropID_MeteringMode,
            })
            {
                _processor.PostCommand(new GetPropertyDescCommand(ref _model, propId));
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
                    DeviceBusy?.Invoke(this, EventArgs.Empty);
                    break;

                case CameraEvent.Type.PROPERTY_CHANGED:
                case CameraEvent.Type.PROPERTY_DESC_CHANGED:
                    CameraPropertyChanged?.Invoke(this, (uint)e.Arg);
                    break;

                case CameraEvent.Type.ERROR:
                    Log.Error("Camera error 0x{Error:X8}", e.Arg);
                    _pendingDownload?.TrySetException(
                        new InvalidOperationException($"EDSDK error 0x{e.Arg:X8}"));
                    Error?.Invoke(this, $"EDSDK error 0x{e.Arg:X8}");
                    break;
            }
        }

        internal void OnPhotoSaved(string path)
        {
            if (_model != null) _model.IsCapturing = false;
            if (_discardNextDownload)
            {
                _discardNextDownload = false;
                Log.Information("Warmup shot downloaded — discarding");
                _pendingWarmup?.TrySetResult(path);
                return;
            }
            Log.Information("Photo download complete: {Path}", path);
            _pendingDownload?.TrySetResult(path);
        }

        // --- SDK event callbacks -------------------------------------------------

        internal uint HandleObjectEvent(uint inEvent, IntPtr inRef, IntPtr inContext)
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

        internal uint HandlePropertyEvent(uint inEvent, uint inPropertyID, uint inParam, IntPtr inContext)
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

        internal uint HandleStateEvent(uint inEvent, uint inParameter, IntPtr inContext)
        {
            if (inEvent == EDSDKLib.EDSDK.StateEvent_Shutdown)
            {
                Log.Warning("Camera disconnected (StateEvent_Shutdown)");
                IsConnected = false;

                // If a capture is in-flight, cancel it immediately rather than waiting 30 s
                if (_model != null) _model.IsCapturing = false;
                _pendingDownload?.TrySetCanceled();

                CameraDisconnected?.Invoke(this, EventArgs.Empty);
            }
            return EDSDKLib.EDSDK.EDS_ERR_OK;
        }

        // --- EVF frame decoding --------------------------------------------------

        private void HandleEvfData(IntPtr dataSetPtr)
        {
            if (dataSetPtr == IntPtr.Zero) return;

            var dataset = Marshal.PtrToStructure<EvfDataSet>(dataSetPtr);
            if (dataset.Stream == IntPtr.Zero) return;

            try
            {
                EDSDKLib.EDSDK.EdsGetPointer(dataset.Stream, out IntPtr pointer);
                EDSDKLib.EDSDK.EdsGetLength(dataset.Stream, out ulong length);

                byte[] buffer = new byte[length];
                Marshal.Copy(pointer, buffer, 0, (int)length);

                var frame = EvfDecoder.Decode(buffer, RotationDegrees);
                if (frame != null)
                    EvfFrameReady?.Invoke(this, frame);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to process EVF frame");
            }
        }

        // --- Cleanup -------------------------------------------------------------

        public void Dispose()
        {
            Log.Information("Closing camera session");
            TearDownCameraSession();

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
