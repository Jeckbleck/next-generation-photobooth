using System;

namespace Photobooth.Camera
{
    public class CameraEvent
    {
        public enum Type
        {
            NONE,
            ERROR,
            DEVICE_BUSY,
            DOWNLOAD_START,
            DOWNLOAD_COMPLETE,
            EVFDATA_CHANGED,
            PROPERTY_CHANGED,
            PROPERTY_DESC_CHANGED,
            PROGRESS,
            ANGLEINFO,
            SHUT_DOWN
        }

        public CameraEvent(Type type, IntPtr arg)
        {
            EventType = type;
            Arg = arg;
        }

        public Type EventType { get; }
        public IntPtr Arg { get; }
    }
}
