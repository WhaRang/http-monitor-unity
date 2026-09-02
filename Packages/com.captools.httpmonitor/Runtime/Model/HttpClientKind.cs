namespace HttpMonitor
{
    /// <summary>Which client produced a record.</summary>
    public enum HttpClientKind
    {
        UnityWebRequest,
        HttpClient,

        /// <summary>Anything else, described through the manual capture API (Best HTTP, a custom socket client, ...).</summary>
        Custom,
    }
}
