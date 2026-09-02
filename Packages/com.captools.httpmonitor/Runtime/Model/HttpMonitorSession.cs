using System;
using System.Collections.Generic;

namespace HttpMonitor
{
    /// <summary>
    /// Holds captured records in a fixed-capacity ring buffer (oldest evicted first) and publishes
    /// changes through events. Safe to use from any thread; events fire on the thread that captured
    /// the change, and a throwing subscriber is reported through <see cref="HttpMonitorLog"/> instead
    /// of propagating into the SDK or the game.
    /// </summary>
    public sealed class HttpMonitorSession
    {
        public const int DefaultCapacity = 1000;

        /// <summary>The session the interceptor writes to.</summary>
        public static HttpMonitorSession Current { get; } = new HttpMonitorSession(DefaultCapacity);

        private readonly object _gate = new object();
        private readonly HttpRecord[] _ring;
        private int _head;
        private int _count;
        private long _lastId;
        private volatile bool _isRecording = true;

        /// <summary>Fired right after a record is created, with <see cref="HttpRecordState.Pending"/>.</summary>
        public event Action<HttpRecord> RecordAdded;

        /// <summary>Fired exactly once per record, after it reached a final state.</summary>
        public event Action<HttpRecord> RecordUpdated;

        public event Action Cleared;

        public HttpMonitorSession(int capacity)
        {
            if (capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be at least 1.");

            _ring = new HttpRecord[capacity];
        }

        public int Capacity => _ring.Length;

        public int Count
        {
            get
            {
                lock (_gate)
                    return _count;
            }
        }

        /// <summary>When false, requests pass through untouched and nothing is recorded. Default true.</summary>
        public bool IsRecording
        {
            get => _isRecording;
            set => _isRecording = value;
        }

        /// <summary>Copy of the current records, oldest first.</summary>
        public HttpRecord[] Snapshot()
        {
            lock (_gate)
            {
                var result = new HttpRecord[_count];

                for (var i = 0; i < _count; i++)
                    result[i] = _ring[(_head + i) % _ring.Length];

                return result;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                Array.Clear(_ring, 0, _ring.Length);
                _head = 0;
                _count = 0;
            }

            Raise(Cleared, nameof(Cleared));
        }

        internal HttpRecord Begin(HttpClientKind client, string method, string url, IReadOnlyList<HttpHeader> requestHeaders)
        {
            HttpRecord record;

            lock (_gate)
            {
                record = new HttpRecord(++_lastId, client, method, url, requestHeaders);

                if (_count == _ring.Length)
                {
                    // Full: the head slot holds the oldest record; overwrite it and advance.
                    _ring[_head] = record;
                    _head = (_head + 1) % _ring.Length;
                }
                else
                {
                    _ring[(_head + _count) % _ring.Length] = record;
                    _count++;
                }
            }

            Raise(RecordAdded, record, nameof(RecordAdded));

            return record;
        }

        internal void Finish(HttpRecord record, HttpRecordOutcome outcome)
        {
            lock (_gate)
                record.Finish(outcome);

            Raise(RecordUpdated, record, nameof(RecordUpdated));
        }

        private static void Raise(Action<HttpRecord> handlers, HttpRecord record, string eventName)
        {
            if (handlers == null)
                return;

            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<HttpRecord>)handler)(record);
                }
                catch (Exception e)
                {
                    ReportSubscriberFailure(handler, eventName, e);
                }
            }
        }

        private static void Raise(Action handlers, string eventName)
        {
            if (handlers == null)
                return;

            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)handler)();
                }
                catch (Exception e)
                {
                    ReportSubscriberFailure(handler, eventName, e);
                }
            }
        }

        private static void ReportSubscriberFailure(Delegate handler, string eventName, Exception e)
        {
            var target = handler.Method.DeclaringType != null ? handler.Method.DeclaringType.Name + "." : string.Empty;

            HttpMonitorLog.Warning($"{eventName} subscriber {target}{handler.Method.Name} threw: {e.GetType().Name}: {e.Message}");
        }
    }
}
