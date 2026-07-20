using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Photobooth.Camera.Commands;
using Serilog;

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
                        var waitMs = (DateTime.UtcNow - cmd.EnqueuedAt).TotalMilliseconds;
                        if (waitMs > 200)
                            Log.Debug("{Command} waited {WaitMs}ms in queue before executing",
                                cmd.GetType().Name, (int)waitMs);

                        if (!cmd.Execute())
                        {
                            Thread.Sleep(500);
                            cmd.EnqueuedAt = DateTime.UtcNow;
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

        public void PostCommand(Command command)
        {
            command.EnqueuedAt = DateTime.UtcNow;
            _queue.Enqueue(command);
        }
    }
}
