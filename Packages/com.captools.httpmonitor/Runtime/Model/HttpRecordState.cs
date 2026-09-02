namespace HttpMonitor
{
    /// <summary>Lifecycle of an <see cref="HttpRecord"/>. Every state except Pending is final.</summary>
    public enum HttpRecordState
    {
        /// <summary>Sent, no outcome yet.</summary>
        Pending,

        /// <summary>An HTTP response arrived. Any status code, 4xx and 5xx included.</summary>
        Completed,

        /// <summary>No response: DNS, connection, TLS or data-processing failure. See <see cref="HttpRecord.Error"/>.</summary>
        Failed,

        /// <summary>Disposed or aborted by the game before completion.</summary>
        Aborted,

        /// <summary>Finished, but the response could not be read (disposed through a path the weaver does not cover).</summary>
        Incomplete,
    }
}
