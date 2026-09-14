using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace HttpMonitor.Samples
{
    /// <summary>
    /// Tiny local HTTP server so the playground works offline. Routes: echo (JSON, mirrors the
    /// request body), html, image (a generated PNG), status/{code}, delay/{ms}, bytes/{n}, chunked.
    /// </summary>
    internal sealed class PlaygroundServer : IDisposable
    {
        private readonly HttpListener _listener = new HttpListener();
        private Thread _thread;

        public string Start()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var baseUrl = $"http://127.0.0.1:{port}/";
            _listener.Prefixes.Add(baseUrl);
            _listener.Start();
            _thread = new Thread(Loop) { IsBackground = true, Name = "HttpMonitor.Playground" };
            _thread.Start();

            return baseUrl;
        }

        public void Dispose()
        {
            try
            {
                _listener.Close();
            }
            catch
            {
                // shutting down
            }
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
                    return;
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
                var path = request.Url.AbsolutePath.TrimStart('/');
                var body = ReadAll(request.InputStream);

                if (path == "echo")
                {
                    response.ContentType = "application/json";
                    var echoed = body.Length > 0 ? Encoding.UTF8.GetString(body) : "null";
                    Write(response, 200, Encoding.UTF8.GetBytes(
                        $"{{\"method\":\"{request.HttpMethod}\",\"path\":\"{request.Url.PathAndQuery}\",\"headers\":{request.Headers.Count},\"body\":{echoed},\"server\":\"playground\"}}"));
                }
                else if (path == "html")
                {
                    response.ContentType = "text/html; charset=utf-8";
                    Write(response, 200, Encoding.UTF8.GetBytes("<!DOCTYPE html><html><head><title>Playground</title><style>body{font-family:sans-serif}</style></head><body><h1>Hello</h1><ul><li>one</li><li>two</li></ul><script>console.log(1 < 2);</script></body></html>"));
                }
                else if (path == "image")
                {
                    response.ContentType = "image/png";
                    Write(response, 200, Png());
                }
                else if (path.StartsWith("status/", StringComparison.Ordinal))
                {
                    var code = int.Parse(path.Substring(7));
                    response.ContentType = "text/plain";
                    Write(response, code, Encoding.UTF8.GetBytes("status " + code));
                }
                else if (path.StartsWith("delay/", StringComparison.Ordinal))
                {
                    Thread.Sleep(int.Parse(path.Substring(6)));
                    response.ContentType = "text/plain";
                    Write(response, 200, Encoding.UTF8.GetBytes("delayed"));
                }
                else if (path.StartsWith("bytes/", StringComparison.Ordinal))
                {
                    var count = int.Parse(path.Substring(6));
                    var bytes = new byte[count];

                    for (var i = 0; i < count; i++)
                        bytes[i] = (byte)(i % 251);

                    response.ContentType = "application/octet-stream";
                    Write(response, 200, bytes);
                }
                else if (path == "chunked")
                {
                    response.StatusCode = 200;
                    response.ContentType = "text/plain";
                    response.SendChunked = true;

                    for (var i = 0; i < 5; i++)
                    {
                        var part = Encoding.UTF8.GetBytes($"chunk {i}\n");
                        response.OutputStream.Write(part, 0, part.Length);
                        response.OutputStream.Flush();
                        Thread.Sleep(100);
                    }
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

        /// <summary>A 64×64 gradient PNG built on the main thread's behalf without touching Unity APIs.</summary>
        private static byte[] Png()
        {
            const int size = 64;
            var raw = new MemoryStream();

            for (var y = 0; y < size; y++)
            {
                raw.WriteByte(0); // filter: none

                for (var x = 0; x < size; x++)
                {
                    raw.WriteByte((byte)(x * 4));
                    raw.WriteByte((byte)(y * 4));
                    raw.WriteByte(128);
                }
            }

            var compressed = Deflate(raw.ToArray());
            var png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
            Chunk(png, "IHDR", Concat(BigEndian(size), BigEndian(size), new byte[] { 8, 2, 0, 0, 0 }));
            Chunk(png, "IDAT", compressed);
            Chunk(png, "IEND", new byte[0]);

            return png.ToArray();
        }

        private static byte[] Deflate(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                output.WriteByte(0x78);
                output.WriteByte(0x01);

                using (var deflate = new System.IO.Compression.DeflateStream(output, System.IO.Compression.CompressionLevel.Fastest, true))
                    deflate.Write(data, 0, data.Length);

                var adler = Adler32(data);
                output.Write(BigEndian((int)adler), 0, 4);

                return output.ToArray();
            }
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;

            foreach (var d in data)
            {
                a = (a + d) % 65521;
                b = (b + a) % 65521;
            }

            return (b << 16) | a;
        }

        private static void Chunk(Stream png, string type, byte[] data)
        {
            var typeBytes = Encoding.ASCII.GetBytes(type);
            png.Write(BigEndian(data.Length), 0, 4);
            png.Write(typeBytes, 0, 4);
            png.Write(data, 0, data.Length);
            png.Write(BigEndian((int)Crc32(Concat(typeBytes, data))), 0, 4);
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;

            foreach (var b in data)
            {
                crc ^= b;

                for (var i = 0; i < 8; i++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }

            return ~crc;
        }

        private static byte[] BigEndian(int value)
        {
            return new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
        }

        private static byte[] Concat(params byte[][] parts)
        {
            var total = 0;

            foreach (var part in parts)
                total += part.Length;

            var result = new byte[total];
            var offset = 0;

            foreach (var part in parts)
            {
                Buffer.BlockCopy(part, 0, result, offset, part.Length);
                offset += part.Length;
            }

            return result;
        }
    }
}
