using System;
using System.Collections.Generic;

namespace HttpMonitor
{
    /// <summary>
    /// Holds captured records in a fixed-capacity ring buffer (oldest evicted first) and publishes
    /// changes through events. Redaction and body limits from <see cref="Options"/> are applied
    /// here, at record time, so nothing sensitive or oversized is ever stored.
    ///
    /// Safe to use from any thread; events fire on the thread that captured the change, and a
    /// throwing subscriber is reported through <see cref="HttpMonitorLog"/> instead of propagating
    /// into the SDK or the game.
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
        private long _storedBodyBytes;
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

        public HttpMonitorOptions Options { get; } = new HttpMonitorOptions();

        public int Capacity => _ring.Length;

        public int Count
        {
            get
            {
                lock (_gate)
                    return _count;
            }
        }

        /// <summary>Bytes of request and response bodies currently stored.</summary>
        public long StoredBodyBytes
        {
            get
            {
                lock (_gate)
                    return _storedBodyBytes;
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
                _storedBodyBytes = 0;
            }

            Raise(Cleared, nameof(Cleared));
        }

        internal HttpRecord Begin(HttpClientKind client, HttpCaptureSource source, string method, string url, IReadOnlyList<HttpHeader> requestHeaders)
        {
            return Begin(client, source, method, url, requestHeaders, null);
        }

        internal HttpRecord Begin(HttpClientKind client, HttpCaptureSource source, string method, string url,
            IReadOnlyList<HttpHeader> requestHeaders, byte[] requestBody)
        {
            var headers = Redact(requestHeaders);
            var body = LimitBody(requestBody, out var truncated);
            HttpRecord record;

            lock (_gate)
            {
                record = new HttpRecord(++_lastId, client, source, method, url, headers, body, truncated);

                if (_count == _ring.Length)
                    EvictOldest();

                _ring[(_head + _count) % _ring.Length] = record;
                _count++;
                _storedBodyBytes += record.StoredBodyBytes;

                EvictOverBudget(record);
            }

            Raise(RecordAdded, record, nameof(RecordAdded));

            return record;
        }

        internal void Finish(HttpRecord record, HttpRecordOutcome outcome)
        {
            outcome.ResponseHeaders = Redact(outcome.ResponseHeaders);
            outcome.ResponseBody = LimitBody(outcome.ResponseBody, out outcome.ResponseBodyTruncated);

            lock (_gate)
            {
                record.Finish(outcome);

                // A record evicted while still pending is no longer ours to account for.
                if (Contains(record))
                {
                    if (outcome.ResponseBody != null)
                        _storedBodyBytes += outcome.ResponseBody.Length;

                    EvictOverBudget(record);
                }
            }

            Raise(RecordUpdated, record, nameof(RecordUpdated));
        }

        private bool Contains(HttpRecord record)
        {
            for (var i = 0; i < _count; i++)
            {
                if (ReferenceEquals(_ring[(_head + i) % _ring.Length], record))
                    return true;
            }

            return false;
        }

        private void EvictOldest()
        {
            var oldest = _ring[_head];

            if (oldest != null)
                _storedBodyBytes -= oldest.StoredBodyBytes;

            _ring[_head] = null;
            _head = (_head + 1) % _ring.Length;
            _count--;
        }

        /// <summary>Evicts from the oldest end until stored bodies fit the budget, never evicting <paramref name="keep"/>.</summary>
        private void EvictOverBudget(HttpRecord keep)
        {
            while (_count > 1 && _storedBodyBytes > Options.MaxTotalBodyBytes && !ReferenceEquals(_ring[_head], keep))
                EvictOldest();
        }

        private IReadOnlyList<HttpHeader> Redact(IReadOnlyList<HttpHeader> headers)
        {
            if (headers == null || headers.Count == 0)
                return headers;

            List<HttpHeader> copy = null;

            for (var i = 0; i < headers.Count; i++)
            {
                if (!Options.IsRedacted(headers[i].Name))
                    continue;

                if (copy == null)
                    copy = new List<HttpHeader>(headers);

                copy[i] = new HttpHeader(headers[i].Name, Options.RedactedValue);
            }

            return copy ?? headers;
        }

        /// <summary>Always returns a private copy, capped at <see cref="HttpMonitorOptions.MaxBodyBytes"/>.</summary>
        private byte[] LimitBody(byte[] body, out bool truncated)
        {
            truncated = false;

            if (body == null || !Options.CaptureBodies)
                return null;

            var max = Math.Max(0, Options.MaxBodyBytes);
            var length = Math.Min(body.Length, max);
            truncated = body.Length > length;

            var copy = new byte[length];
            Array.Copy(body, copy, length);

            return copy;
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
