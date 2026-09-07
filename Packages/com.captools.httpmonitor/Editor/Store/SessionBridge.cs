using System;
using System.Collections.Concurrent;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// Moves session events onto the main thread. The runtime session fires on whichever thread
    /// captured the change (thread pool for HttpClient), so events are queued here and applied to the
    /// buffer when <see cref="Drain"/> runs from the Editor update loop.
    /// </summary>
    internal sealed class SessionBridge : IDisposable
    {
        private readonly HttpMonitorSession _session;
        private readonly EditorRecordBuffer _buffer;
        private readonly ConcurrentQueue<(bool isUpdate, HttpRecord record)> _queue = new ConcurrentQueue<(bool, HttpRecord)>();
        private bool _disposed;

        public SessionBridge(HttpMonitorSession session, EditorRecordBuffer buffer)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));

            _session.RecordAdded += OnAdded;
            _session.RecordUpdated += OnUpdated;
        }

        public int Pending => _queue.Count;

        /// <summary>Applies queued events in order. Main thread only.</summary>
        /// <returns>How many events were applied.</returns>
        public int Drain()
        {
            var applied = 0;

            while (_queue.TryDequeue(out var item))
            {
                applied++;

                try
                {
                    if (item.isUpdate)
                        _buffer.Update(item.record);
                    else
                        _buffer.Add(item.record);
                }
                catch (Exception e)
                {
                    HttpMonitorLog.Warning($"editor store failed to apply a record: {e.GetType().Name}: {e.Message}");
                }
            }

            return applied;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _session.RecordAdded -= OnAdded;
            _session.RecordUpdated -= OnUpdated;
        }

        private void OnAdded(HttpRecord record)
        {
            _queue.Enqueue((false, record));
        }

        private void OnUpdated(HttpRecord record)
        {
            _queue.Enqueue((true, record));
        }
    }
}
