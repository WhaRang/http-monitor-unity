using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// The Editor's record store: attaches to the runtime session as soon as the Editor loads, pumps
    /// events on <c>EditorApplication.update</c>, clears on Play unless <see cref="PreserveLog"/>, and
    /// persists itself across domain reloads and Editor restarts.
    /// </summary>
    [FilePath("Library/HttpMonitor/EditorRecordStore.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class EditorRecordStore : ScriptableSingleton<EditorRecordStore>
    {
        /// <summary>
        /// The runtime's <see cref="HttpMonitorOptions"/> live in a static that resets on every domain
        /// reload; this copy is what makes edits from the options window stick. Project Settings (M3)
        /// will move it into a project asset.
        /// </summary>
        [Serializable]
        private sealed class OptionsSnapshot
        {
            public bool Set;
            public bool CaptureBodies = true;
            public int MaxBodyBytes = HttpMonitorOptions.DefaultMaxBodyBytes;
            public long MaxTotalBodyBytes = HttpMonitorOptions.DefaultMaxTotalBodyBytes;
            public bool BufferUnknownLengthResponses = true;
            public string RedactedValue = HttpMonitorOptions.DefaultRedactedValue;
            public string[] RedactedHeaders = new string[0];
        }

        [SerializeField] private EditorRecordBuffer _buffer = new EditorRecordBuffer();
        [SerializeField] private bool _preserveLog;
        [SerializeField] private bool _isRecording = true;
        [SerializeField] private OptionsSnapshot _options = new OptionsSnapshot();

        private SessionBridge _bridge;

        public EditorRecordBuffer Buffer => _buffer;

        /// <summary>Keep records across Play sessions instead of clearing when Play starts.</summary>
        public bool PreserveLog
        {
            get => _preserveLog;
            set => _preserveLog = value;
        }

        /// <summary>Mirrors <see cref="HttpMonitorSession.IsRecording"/> and re-applies it after every domain reload.</summary>
        public bool IsRecording
        {
            get => _isRecording;
            set
            {
                _isRecording = value;
                HttpMonitorSession.Current.IsRecording = value;
            }
        }

        public void Clear()
        {
            _buffer.Clear();
        }

        /// <summary>Remembers the session's current options so they survive the next domain reload.</summary>
        public void SaveOptionsFromSession()
        {
            var options = HttpMonitorSession.Current.Options;
            _options.Set = true;
            _options.CaptureBodies = options.CaptureBodies;
            _options.MaxBodyBytes = options.MaxBodyBytes;
            _options.MaxTotalBodyBytes = options.MaxTotalBodyBytes;
            _options.BufferUnknownLengthResponses = options.BufferUnknownLengthResponses;
            _options.RedactedValue = options.RedactedValue;
            _options.RedactedHeaders = new List<string>(options.RedactedHeaders).ToArray();
        }

        private void ApplyOptionsToSession()
        {
            if (!_options.Set)
                return;

            var options = HttpMonitorSession.Current.Options;
            options.CaptureBodies = _options.CaptureBodies;
            options.MaxBodyBytes = _options.MaxBodyBytes;
            options.MaxTotalBodyBytes = _options.MaxTotalBodyBytes;
            options.BufferUnknownLengthResponses = _options.BufferUnknownLengthResponses;
            options.RedactedValue = _options.RedactedValue;
            options.RedactedHeaders.Clear();

            foreach (var header in _options.RedactedHeaders)
                options.RedactedHeaders.Add(header);
        }

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            instance.Attach();
        }

        private void Attach()
        {
            if (_bridge != null)
                return;

            _buffer.OnDomainReloaded("interrupted by a domain reload");
            HttpMonitorSession.Current.IsRecording = _isRecording;
            ApplyOptionsToSession();
            _bridge = new SessionBridge(HttpMonitorSession.Current, _buffer);

            EditorApplication.update += Pump;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload += Persist;
            EditorApplication.quitting += Persist;
        }

        private void Pump()
        {
            _bridge.Drain();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode && !_preserveLog)
                _buffer.Clear();
        }

        private void Persist()
        {
            _bridge.Drain();
            Save(false);
        }
    }
}
