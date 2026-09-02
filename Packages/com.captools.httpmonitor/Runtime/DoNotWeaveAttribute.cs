using System;

namespace HttpMonitor
{
    /// <summary>
    /// Opts a whole assembly out of build-time weaving:
    /// <c>[assembly: HttpMonitor.DoNotWeave]</c>. Requests made from it are only captured through
    /// the manual API (<see cref="HttpMonitorCapture"/>).
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly)]
    public sealed class DoNotWeaveAttribute : Attribute
    {
    }
}
