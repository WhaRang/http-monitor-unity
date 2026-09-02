namespace HttpMonitor
{
    /// <summary>
    /// Contract for everything the SDK logs about itself. Implement it to route SDK output into
    /// your own logging system, then assign it to <see cref="HttpMonitorLog.Logger"/>.
    /// Messages arrive without any prefix; add your own category or tag as needed.
    /// Calls may come from any thread and must not throw (exceptions are swallowed by the SDK).
    /// </summary>
    public interface IHttpMonitorLogger
    {
        public void Info(string message);

        public void Warning(string message);

        public void Error(string message);
    }
}
