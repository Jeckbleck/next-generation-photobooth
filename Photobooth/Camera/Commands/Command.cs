using System;

namespace Photobooth.Camera.Commands
{
    internal abstract class Command
    {
        protected CameraModel _model;

        // Stamped by CommandProcessor.PostCommand on every (re)enqueue, so the
        // processing loop can log how long a command sat in queue before its
        // Execute() call actually started running.
        internal DateTime EnqueuedAt { get; set; }

        protected Command(ref CameraModel model) => _model = model;

        public virtual bool Execute() => true;
    }
}
