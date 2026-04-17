using System.Collections.Generic;

namespace Photobooth.Camera
{
    public interface IObserver
    {
        void Update(Observable observable, CameraEvent e);
    }

    public abstract class Observable
    {
        private readonly List<IObserver> _observers = new();

        public void Add(ref IObserver observer) => _observers.Add(observer);
        public void Remove(ref IObserver observer) => _observers.Remove(observer);

        public void NotifyObservers(CameraEvent e)
        {
            foreach (var obs in _observers)
                obs.Update(this, e);
        }
    }
}
