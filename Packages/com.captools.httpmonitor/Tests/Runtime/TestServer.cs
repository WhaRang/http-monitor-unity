using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace HttpMonitor.Tests
{
    /// <summary>
    /// In-process HTTP server so the suite never touches the internet. One shared instance per
    /// test domain, started on first use, on a free loopback port.
    ///
    /// Routes:
    ///   /echo            200, application/json, echoes the request body (or {"echo":true}); every
    ///                    request header comes back as X-Echo-{Name}
    ///   /status/{code}   that status with a short text body
    ///   /delay/{ms}      200 after sleeping
    ///   /cookies         200 with two Set-Cookie headers
    ///   /chunked         200, text/plain, chunked transfer, no Content-Length
    ///   /bytes/{n}       200, application/octet-stream, n deterministic bytes
    /// </summary>
    internal sealed class TestServer : IDisposable
    {
        private static readonly object Gate = new object();
        private static TestServer _shared;

        private readonly HttpListener _listener = new HttpListener();
        private readonly Thread _thread;

        public string BaseUrl { get; }

        public static TestServer Shared
        {
            get
            {
                lock (Gate)
                    return _shared ?? (_shared = new TestServer());
            }
        }

        /// <summary>A URL on a port nobody listens on, for connection-refused tests.</summary>
        public static string UnreachableUrl => "http://127.0.0.1:1/";

        private TestServer()
        {
            var port = FreePort();
            BaseUrl = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();

            _thread = new Thread(Loop) { IsBackground = true, Name = "HttpMonitor.TestServer" };
            _thread.Start();

            AppDomain.CurrentDomain.DomainUnload += (_, __) => Dispose();
        }

        public string Url(string pathAndQuery)
        {
            return BaseUrl + pathAndQuery.TrimStart('/');
        }

        public void Dispose()
        {
            try
            {
                _listener.Close();
            }
            catch
            {
                // Shutting down; nothing to report to.
            }
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            return port;
        }

        private void Loop()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = _listener.GetContext();
                }
                catch
                {
                    return; // listener closed
                }

                ThreadPool.QueueUserWorkItem(_ => Handle(context));
            }
        }

        private static void Handle(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                var path = request.Url.AbsolutePath;
                var body = ReadAll(request.InputStream);

                if (path == "/echo")
                {
                    foreach (var name in request.Headers.AllKeys)
                        response.AddHeader("X-Echo-" + name, request.Headers[name]);

                    response.ContentType = "application/json";
                    Write(response, 200, body.Length > 0 ? body : Encoding.UTF8.GetBytes("{\"echo\":true}"));
                }
                else if (path.StartsWith("/status/", StringComparison.Ordinal))
                {
                    var code = int.Parse(path.Substring("/status/".Length));
                    response.ContentType = "text/plain";
                    Write(response, code, Encoding.UTF8.GetBytes("status " + code));
                }
                else if (path.StartsWith("/delay/", StringComparison.Ordinal))
                {
                    Thread.Sleep(int.Parse(path.Substring("/delay/".Length)));
                    response.ContentType = "text/plain";
                    Write(response, 200, Encoding.UTF8.GetBytes("delayed"));
                }
                else if (path == "/cookies")
                {
                    response.AppendHeader("Set-Cookie", "first=1; Path=/");
                    response.AppendHeader("Set-Cookie", "second=2; Path=/");
                    response.ContentType = "text/plain";
                    Write(response, 200, Encoding.UTF8.GetBytes("cookies"));
                }
                else if (path == "/chunked")
                {
                    response.StatusCode = 200;
                    response.ContentType = "text/plain";
                    response.SendChunked = true;
                    var part = Encoding.UTF8.GetBytes("chunk-one;");
                    response.OutputStream.Write(part, 0, part.Length);
                    response.OutputStream.Flush();
                    Thread.Sleep(20);
                    part = Encoding.UTF8.GetBytes("chunk-two");
                    response.OutputStream.Write(part, 0, part.Length);
                }
                else if (path.StartsWith("/bytes/", StringComparison.Ordinal))
                {
                    var count = int.Parse(path.Substring("/bytes/".Length));
                    var bytes = new byte[count];

                    for (var i = 0; i < count; i++)
                        bytes[i] = (byte)(i % 251);

                    response.ContentType = "application/octet-stream";
                    Write(response, 200, bytes);
                }
                else
                {
                    Write(response, 404, Encoding.UTF8.GetBytes("no route " + path));
                }
            }
            catch (Exception)
            {
                try
                {
                    response.StatusCode = 500;
                }
                catch
                {
                    // headers already sent
                }
            }
            finally
            {
                try
                {
                    response.Close();
                }
                catch
                {
                    // client went away
                }
            }
        }

        private static void Write(HttpListenerResponse response, int status, byte[] bytes)
        {
            response.StatusCode = status;
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }

        private static byte[] ReadAll(Stream stream)
        {
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);

                return buffer.ToArray();
            }
        }
    }
}
