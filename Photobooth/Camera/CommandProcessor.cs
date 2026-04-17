using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Photobooth.Camera.Commands;

namespace Photobooth.Camera
{
    internal class CommandProcessor
    {
        private readonly ConcurrentQueue<Command> _queue = new();
        private bool _running;
        private Task? _task;

        public void Start()
        {
            _running = true;
            _task = Task.Run(() =>
            {
                while (_running)
                {
                    Thread.Sleep(1);
                    if (_queue.TryDequeue(out var cmd))
                    {
                        if (!cmd.Execute())
                        {
                            Thread.Sleep(500);
                            _queue.Enqueue(cmd);
                        }
                    }
                }
            });
        }

        public void Stop()
        {
            _running = false;
            try { _task?.Wait(); } catch { }
            _task?.Dispose();
        }

        public void PostCommand(Command command) => _queue.Enqueue(command);
    }
}
