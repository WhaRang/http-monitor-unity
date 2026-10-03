# Custom logger

HTTP Monitor reports its own problems (a capture that failed, a subscriber that threw) as log
messages. By default they go to the Unity console with an `[HttpMonitor]` prefix. If your project
has its own logging system, route them there.

This is about the SDK's own diagnostics. It has nothing to do with capturing traffic; that is
[manual capture](manual-capture.md).

## The contract

```csharp
namespace HttpMonitor
{
    public interface IHttpMonitorLogger
    {
        void Info(string message);
        void Warning(string message);
        void Error(string message);
    }
}
```

Messages arrive **without a prefix**, so you can add your own category or tag.

## Injecting yours

```csharp
public sealed class GameLoggerAdapter : IHttpMonitorLogger
{
    public void Info(string message) => GameLog.Info("Net", message);

    public void Warning(string message) => GameLog.Warn("Net", message);

    public void Error(string message) => GameLog.Error("Net", message);
}
```

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
private static void InstallLogger()
{
    HttpMonitorLog.Logger = new GameLoggerAdapter();
}
```

Assigning `null` restores the default.

## What to expect

- **It is quiet.** In normal operation the SDK logs nothing. It does not log each request; the
  window is for that.
- **Calls can come from any thread.** `HttpClient` traffic is captured on thread-pool threads,
  and a warning about it is raised there. Make your logger thread-safe, or queue to the main
  thread.
- **Your logger cannot break anything.** If it throws, the SDK swallows the exception and reports
  it once to the Unity console, naming your logger type. The game's request is unaffected.

## Silencing it

```csharp
public sealed class SilentLogger : IHttpMonitorLogger
{
    public void Info(string message) { }

    public void Warning(string message) { }

    public void Error(string message) { }
}
```
