using System;

namespace HttpMonitor.Editor
{
    /// <summary>What a body most likely is, decided from the Content-Type first and the bytes second.</summary>
    internal enum BodyKind
    {
        Empty,
        Json,
        Markup,
        Text,
        Image,
        Binary,
    }

    internal static class BodyKindDetector
    {
        /// <summary>
        /// A declared Content-Type wins: JSON and markup types format, any other specific type is
        /// shown as text even when the bytes look like JSON (the server said text, so text; the
        /// viewer still offers Pretty when <see cref="Sniff"/> disagrees). Only a missing or generic
        /// type (octet-stream, */*) falls back to sniffing the bytes.
        /// </summary>
        public static BodyKind Detect(byte[] body, string contentType)
        {
            if (body == null || body.Length == 0)
                return BodyKind.Empty;

            var type = (contentType ?? string.Empty).ToLowerInvariant();

            if (type.StartsWith("image/", StringComparison.Ordinal) || IsImageSignature(body))
                return BodyKind.Image;

            if (!RecordFormat.LooksLikeText(body))
                return BodyKind.Binary;

            if (type.Contains("json"))
                return BodyKind.Json;

            if (type.Contains("html") || type.Contains("xml"))
                return BodyKind.Markup;

            if (IsSpecific(type))
                return BodyKind.Text;

            return Sniff(body);
        }

        /// <summary>What the first bytes look like, ignoring any declared type. Text bytes only.</summary>
        public static BodyKind Sniff(byte[] body)
        {
            if (body == null || body.Length == 0)
                return BodyKind.Empty;

            var head = System.Text.Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 64));

            if (JsonFormatter.LooksLikeJson(head))
                return BodyKind.Json;

            if (MarkupFormatter.LooksLikeMarkup(head))
                return BodyKind.Markup;

            return BodyKind.Text;
        }

        private static bool IsSpecific(string type)
        {
            return type.Length > 0 && type != "*/*" && !type.Contains("octet-stream");
        }

        private static bool IsImageSignature(byte[] b)
        {
            if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
                return true; // PNG

            if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
                return true; // JPEG

            return false;
        }
    }
}
