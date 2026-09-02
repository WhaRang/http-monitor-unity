using System;

namespace HttpMonitor
{
    /// <summary>
    /// How a record entered the session. Flags: a request the weaver captured and user code also
    /// handed to the manual API carries both bits and shows up under both filters in the window.
    /// </summary>
    [Flags]
    public enum HttpCaptureSource
    {
        None = 0,

        /// <summary>Captured by a call site the weaver rewrote at build time. No user code involved.</summary>
        Woven = 1,

        /// <summary>Handed to the SDK explicitly through the manual capture API.</summary>
        Manual = 2,
    }
}
