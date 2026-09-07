using System;
using System.Collections.Generic;
using UnityEngine;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// Ordered, capped list of <see cref="EditorRecord"/>s (oldest first) with the same eviction
    /// rules as the runtime session: a record count cap and a total body budget. Main thread only.
    /// Serializable so it can live inside a ScriptableSingleton and survive domain reloads.
    /// </summary>
    [Serializable]
    public sealed class EditorRecordBuffer
    {
        public const int DefaultCapacity = 1000;
        public const long DefaultMaxTotalBodyBytes = 16L * 1024 * 1024;

        [SerializeField] private List<EditorRecord> _records = new List<EditorRecord>();
        [SerializeField] private long _nextId = 1;
        [SerializeField] private int _capacity = DefaultCapacity;
        [SerializeField] private long _maxTotalBodyBytes = DefaultMaxTotalBodyBytes;

        [NonSerialized] private Dictionary<HttpRecord, EditorRecord> _byRuntime;
        [NonSerialized] private long _storedBodyBytes;
        [NonSerialized] private bool _storedBodyBytesKnown;

        /// <summary>Fired after any mutation: add, update, clear, eviction.</summary>
        public event Action Changed;

        public event Action<EditorRecord> RecordAdded;
        public event Action<EditorRecord> RecordUpdated;

        public IReadOnlyList<EditorRecord> Records => _records;

        public int Count => _records.Count;

        public int Capacity
        {
            get => _capacity;
            set
            {
                _capacity = Math.Max(1, value);
                EvictToLimits(null);
                Changed?.Invoke();
            }
        }

        public long MaxTotalBodyBytes
        {
            get => _maxTotalBodyBytes;
            set
            {
                _maxTotalBodyBytes = Math.Max(0, value);
                EvictToLimits(null);
                Changed?.Invoke();
            }
        }

        public long StoredBodyBytes
        {
            get
            {
                EnsureBodyBytes();

                return _storedBodyBytes;
            }
        }

        /// <summary>Copies a runtime record in. If it already finished, the copy is finished too.</summary>
        public EditorRecord Add(HttpRecord runtime)
        {
            EnsureBodyBytes();

            var record = EditorRecord.From(runtime, _nextId++);
            _records.Add(record);
            ByRuntime[runtime] = record;
            _storedBodyBytes += record.StoredBodyBytes;

            EvictToLimits(record);
            RecordAdded?.Invoke(record);
            Changed?.Invoke();

            return record;
        }

        /// <summary>Refreshes the copy of a runtime record; null when it was evicted or never added.</summary>
        public EditorRecord Update(HttpRecord runtime)
        {
            if (!ByRuntime.TryGetValue(runtime, out var record))
                return null;

            EnsureBodyBytes();
            _storedBodyBytes -= record.StoredBodyBytes;
            record.CopyOutcome(runtime);
            _storedBodyBytes += record.StoredBodyBytes;

            EvictToLimits(record);
            RecordUpdated?.Invoke(record);
            Changed?.Invoke();

            return record;
        }

        public EditorRecord FindById(long id)
        {
            for (var i = _records.Count - 1; i >= 0; i--)
            {
                if (_records[i].Id == id)
                    return _records[i];
            }

            return null;
        }

        public void Clear()
        {
            _records.Clear();
            ByRuntime.Clear();
            _storedBodyBytes = 0;
            _storedBodyBytesKnown = true;
            Changed?.Invoke();
        }

        /// <summary>
        /// After a domain reload the runtime objects are gone: drop the links and settle any record
        /// still waiting for a response that will never be observed.
        /// </summary>
        public void OnDomainReloaded(string reasonForPending)
        {
            ByRuntime.Clear();
            var changed = false;

            foreach (var record in _records)
            {
                record.Runtime = null;

                if (record.IsFinished)
                    continue;

                record.MarkIncomplete(reasonForPending);
                changed = true;
            }

            if (changed)
                Changed?.Invoke();
        }

        private Dictionary<HttpRecord, EditorRecord> ByRuntime => _byRuntime ?? (_byRuntime = new Dictionary<HttpRecord, EditorRecord>());

        private void EnsureBodyBytes()
        {
            if (_storedBodyBytesKnown)
                return;

            _storedBodyBytes = 0;

            foreach (var record in _records)
                _storedBodyBytes += record.StoredBodyBytes;

            _storedBodyBytesKnown = true;
        }

        /// <summary>Evicts from the oldest end: over capacity always, over the body budget unless the oldest is <paramref name="keep"/>.</summary>
        private void EvictToLimits(EditorRecord keep)
        {
            EnsureBodyBytes();

            while (_records.Count > _capacity)
                EvictOldest();

            while (_records.Count > 1 && _storedBodyBytes > _maxTotalBodyBytes && !ReferenceEquals(_records[0], keep))
                EvictOldest();
        }

        private void EvictOldest()
        {
            var oldest = _records[0];
            _records.RemoveAt(0);
            _storedBodyBytes -= oldest.StoredBodyBytes;

            if (oldest.Runtime != null)
                ByRuntime.Remove(oldest.Runtime);
        }
    }
}
