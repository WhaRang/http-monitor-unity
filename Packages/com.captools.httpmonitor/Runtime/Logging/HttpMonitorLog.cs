using System;
using UnityEngine;

namespace HttpMonitor
{
    /// <summary>
    /// Single entry point for SDK logging. Users inject their own sink through <see cref="Logger"/>;
    /// the SDK itself only calls the internal helpers, which never let a logger exception escape.
    /// </summary>
    public static class HttpMonitorLog
    {
        private static IHttpMonitorLogger _logger = new UnityDebugLogger();

        public static IHttpMonitorLogger Logger
        {
            get => _logger;
            set => _logger = value ?? new UnityDebugLogger();
        }

        internal static void Info(string message)
        {
            try
            {
                _logger.Info(message);
            }
            catch (Exception e)
            {
                ReportLoggerFailure(e);
            }
        }

        internal static void Warning(string message)
        {
            try
            {
                _logger.Warning(message);
            }
            catch (Exception e)
            {
                ReportLoggerFailure(e);
            }
        }

        internal static void Error(string message)
        {
            try
            {
                _logger.Error(message);
            }
            catch (Exception e)
            {
                ReportLoggerFailure(e);
            }
        }

        private static void ReportLoggerFailure(Exception e)
        {
            try
            {
                Debug.LogWarning($"[HttpMonitor] the injected {_logger.GetType().Name} threw: {e.GetType().Name}: {e.Message}");
            }
            catch
            {
                // Nothing left to report to.
            }
        }
    }
}
