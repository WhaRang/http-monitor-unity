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
        [SerializeField] private EditorRecordBuffer _buffer = new EditorRecordBuffer();
        [SerializeField] private bool _preserveLog;
        [SerializeField] private bool _isRecording = true;

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
