using UnityEditor;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// What happens when the user clicks Replay: send straight away, ask first, or open the composer
    /// because a secret is missing. The decision is a pure function so it is tested without dialogs.
    /// </summary>
    internal static class ReplayController
    {
        private const string ConfirmPrefKey = "HttpMonitor.Replay.ConfirmUnsafe";

        internal enum Decision
        {
            Send,
            Confirm,
            OpenComposer,
        }

        /// <summary>Ask before re-sending a non-idempotent method. Default on; the dialog offers "don't ask again".</summary>
        public static bool ConfirmUnsafe
        {
            get => EditorPrefs.GetBool(ConfirmPrefKey, true);
            set => EditorPrefs.SetBool(ConfirmPrefKey, value);
        }

        internal static Decision Decide(ReplayRequest request, bool confirmUnsafe)
        {
            if (request.HasRedactedHeaders)
                return Decision.OpenComposer;

            return !request.IsSafeMethod && confirmUnsafe ? Decision.Confirm : Decision.Send;
        }

        /// <summary>One-click replay of a captured record.</summary>
        public static void Replay(EditorRecord record)
        {
            if (record == null)
                return;

            var request = ReplayRequest.From(record, HttpMonitorSession.Current.Options.RedactedValue);
            ReplaySecrets.Fill(request);

            switch (Decide(request, ConfirmUnsafe))
            {
                case Decision.OpenComposer:
                    RequestComposerWindow.Open(request);

                    return;
                case Decision.Confirm:
                    if (!Confirm(request))
                        return;

                    break;
            }

            Send(request);
        }

        /// <summary>Opens the composer pre-filled from a record.</summary>
        public static void EditAndResend(EditorRecord record)
        {
            if (record == null)
                return;

            var request = ReplayRequest.From(record, HttpMonitorSession.Current.Options.RedactedValue);
            ReplaySecrets.Fill(request);
            RequestComposerWindow.Open(request);
        }

        /// <summary>Sends and asks the main window to follow the new record.</summary>
        public static ReplayService.Handle Send(ReplayRequest request)
        {
            var handle = ReplayService.Default.Send(request);
            HttpMonitorWindow.Instance?.SelectRuntime(handle.Record);

            return handle;
        }

        /// <summary>The "are you sure" for non-idempotent methods. Returns true to proceed.</summary>
        public static bool Confirm(ReplayRequest request)
        {
            var choice = EditorUtility.DisplayDialogComplex(
                $"Replay {request.Method.ToUpperInvariant()}?",
                $"Re-sending a {request.Method.ToUpperInvariant()} may repeat its effect on the server (a purchase, a score submission, a delete).\n\n{request.Url}",
                "Send", "Cancel", "Send and don't ask again");

            if (choice == 2)
                ConfirmUnsafe = false;

            return choice != 1;
        }
    }
}
