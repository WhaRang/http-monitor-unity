# How it works

A tool that changes your compiled code owes you an explanation. This page says exactly what
HTTP Monitor rewrites, when, and what it leaves alone.

## The short version

When Unity compiles your scripts, the package replaces a handful of specific calls with calls to
its own methods, which record the request and then make the original call. Your source files are
never modified. Turn it off and the next compile produces your code exactly as written.

## Why rewriting, and not something simpler

`HttpClient` has an extension point: a handler you can put in front of the real one. One line
instruments a client.

`UnityWebRequest` has none. There is no global hook, no event for "a request was sent". The only
way to see a request without rewriting is to wrap every call site by hand, which is a migration,
and the first forgotten call site is the one you needed.

Rewriting at compile time gives automatic capture for both, and it works on every platform,
including IL2CPP, because it happens before Unity turns your code into native code.

## What is rewritten

Unity compiles C# into IL, a bytecode stored in each assembly. After each assembly compiles,
Unity hands it to registered post-processors. HTTP Monitor's post-processor scans it and replaces
these instructions:

| Your code | Becomes a call to |
|---|---|
| `request.SendWebRequest()` | `Interceptor.SendWebRequest(request)` |
| `request.SetRequestHeader(name, value)` | `Interceptor.SetRequestHeader(request, name, value)` |
| `request.Dispose()`, and the dispose a `using` block performs | `Interceptor.Dispose(request)` |
| `new HttpClient()` | `Interceptor.CreateHttpClient()` |
| `new HttpClient(handler)` | `Interceptor.CreateHttpClient(handler)` |
| `new HttpClient(handler, disposeHandler)` | `Interceptor.CreateHttpClient(handler, disposeHandler)` |

Each replacement takes the same inputs and produces the same output as the original, so a single
instruction changes and nothing around it moves. That is what makes it safe inside coroutines,
async methods, lambdas and try blocks: to the rest of the method, nothing happened.

Nothing else is touched. No fields are added to your classes, no methods are injected, no
attributes are needed.

## What each replacement does

**SendWebRequest**: record the method, URL, headers set so far and the upload body, then call the
real `SendWebRequest`, then listen for completion.

**SetRequestHeader**: call the real method, then remember the header. `UnityWebRequest` cannot
list its own request headers, so the only way to show them is to see them being set.

**Dispose**: if the request has finished, read the response first; then call the real `Dispose`.
This one exists because of how Unity resumes coroutines. In

```csharp
using (var request = UnityWebRequest.Get(url))
{
    yield return request.SendWebRequest();
}
```

Unity resumes the coroutine first and raises the completion event afterwards. By the time the
event arrives, the `using` block has already disposed the request and the response is gone.
Capturing at the dispose call is the only moment that always works. Whichever comes first, the
dispose or the completion event, records the response; the other does nothing.

**new HttpClient**: build the client with the monitoring handler in front of yours. Disposal
behaves as before, since disposing the wrapper disposes your handler exactly when `HttpClient`
would have.

## The promise: nothing throws

The recording code is wrapped so that a failure inside it becomes a warning, never an exception
in your game. The real call to Unity or to `HttpClient` is always made outside that wrapping, so
exceptions that your code would have seen without the package, it still sees, with the same type
and at the same place.

## What is not rewritten

- **Unity's own assemblies and packages** (`Unity.*`, `UnityEngine.*`, `UnityEditor.*`), and
  system libraries.
- **Precompiled DLLs.** Unity does not pass them through post-processors. Use
  [manual capture](manual-capture.md).
- **Assemblies you excluded** in [Settings](settings.md) or marked
  `[assembly: HttpMonitor.DoNotWeave]`.
- **Release builds**, unless you opted in.
- **Calls made by reflection**, since there is no call instruction to find.
- **Native code.** An ads or analytics SDK written in Java or Objective-C makes its own
  connections, outside C# entirely.

## When it runs, and what it costs

The post-processor runs as part of script compilation, in the Editor and for player builds alike.
It only rewrites an assembly that actually contains one of the calls above; any other assembly
is returned untouched.

At runtime, each captured request costs one record, a copy of the headers, and copies of the
bodies up to the cap. With **Record** off, the replaced methods call straight through.

## Where settings come from

The post-processor runs in a separate process that cannot read Unity assets. So the Editor writes
the three weaving settings into `ProjectSettings/HttpMonitorWeaver.cfg`, a small text file, and
the post-processor reads that. This is why a weaving setting takes effect on the next compile,
and why that file belongs in version control: a build machine with a fresh clone needs it before
the first compile.

## Checking for yourself

To confirm what a build contains, look for a reference to the runtime assembly in your compiled
code. In a development build it is there; in a release build with default settings it is not.
For a Mono build:

```
grep -c HttpMonitor.Runtime Build_Data/Managed/Assembly-CSharp.dll
```

An IL2CPP build has no managed assemblies to inspect. Check the copy Unity compiled for the
player instead, under `Library/Bee/` in the project, right after building.

To see the rewritten code itself, open the assembly from `Library/ScriptAssemblies` in ILSpy or
dnSpy and look at a method that sends a request.
