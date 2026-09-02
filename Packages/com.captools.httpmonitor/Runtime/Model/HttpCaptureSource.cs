namespace HttpMonitor
{
    /// <summary>How a record entered the session. Orthogonal to <see cref="HttpClientKind"/>.</summary>
    public enum HttpCaptureSource
    {
        /// <summary>Captured by a call site the weaver rewrote at build time. No user code involved.</summary>
        Woven,

        /// <summary>Handed to the SDK explicitly through the manual capture API.</summary>
        Manual,
    }
}
