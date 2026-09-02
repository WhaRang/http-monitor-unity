using System.Collections.Generic;

namespace HttpMonitor.Tests
{
    /// <summary>Test double for <see cref="IHttpMonitorLogger"/> that keeps every message.</summary>
    internal sealed class CapturingLogger : IHttpMonitorLogger
    {
        public List<string> Infos { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();

        public void Info(string message) => Infos.Add(message);

        public void Warning(string message) => Warnings.Add(message);

        public void Error(string message) => Errors.Add(message);
    }
}
