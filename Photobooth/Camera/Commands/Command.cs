namespace Photobooth.Camera.Commands
{
    internal abstract class Command
    {
        protected CameraModel _model;

        protected Command(ref CameraModel model) => _model = model;

        public virtual bool Execute() => true;
    }
}
