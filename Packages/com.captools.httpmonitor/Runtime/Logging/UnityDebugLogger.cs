using UnityEngine;

namespace HttpMonitor
{
    /// <summary>
    /// Default <see cref="IHttpMonitorLogger"/>: writes to the Unity console with a "[HttpMonitor]" prefix.
    /// </summary>
    public sealed class UnityDebugLogger : IHttpMonitorLogger
    {
        private const string Prefix = "[HttpMonitor] ";

        public void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        public void Warning(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public void Error(string message)
        {
            Debug.LogError(Prefix + message);
        }
    }
}
