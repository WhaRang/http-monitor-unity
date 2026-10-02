using System.Text;
using HttpMonitor.Editor;
using NUnit.Framework;

namespace HttpMonitor.Tests.Editor
{
    public class JsonFormatterTests
    {
        private static string Format(string json)
        {
            Assert.IsTrue(JsonFormatter.TryFormat(json, out var formatted), "expected valid JSON: " + json);

            return formatted;
        }

        [Test]
        public void Object_AndArray_AreIndentedTwoSpaces()
        {
            Assert.AreEqual(
                "{\n  \"a\": 1,\n  \"b\": [\n    true,\n    null,\n    \"x\"\n  ],\n  \"c\": {\n    \"d\": -1.5e3\n  }\n}",
                Format("{\"a\":1,\"b\":[true,null,\"x\"],\"c\":{\"d\":-1.5e3}}"));
        }

        [Test]
        public void EmptyContainers_StayOnOneLine()
        {
            Assert.AreEqual("{\n  \"a\": {},\n  \"b\": []\n}", Format("{ \"a\" : { } , \"b\" : [ ] }"));
            Assert.AreEqual("[]", Format("[]"));
        }

        [Test]
        public void Strings_KeepEscapes_AndColonsInsideThem()
        {
            Assert.AreEqual("{\n  \"k:e\\\"y\": \"v\\nalue\\u00e9\"\n}", Format("{\"k:e\\\"y\":\"v\\nalue\\u00e9\"}"));
        }

        [Test]
        public void ScalarsAtTopLevel_AreValid()
        {
            Assert.AreEqual("42", Format(" 42 "));
            Assert.AreEqual("\"s\"", Format("\"s\""));
            Assert.AreEqual("null", Format("null"));
        }

        [Test]
        public void Malformed_ReturnsFalse()
        {
            foreach (var bad in new[] { "", "   ", "{", "{\"a\":}", "[1,]", "{\"a\":1}x", "{a:1}", "nul", "1.", "-", "[\"unterminated]", "{\"a\":1,}" })
                Assert.IsFalse(JsonFormatter.TryFormat(bad, out _), "should reject: " + bad);
        }

        [Test]
        public void LooksLikeJson_ChecksTheFirstNonSpaceCharacter()
        {
            Assert.IsTrue(JsonFormatter.LooksLikeJson("  {\"a\":1}"));
            Assert.IsTrue(JsonFormatter.LooksLikeJson("[1]"));
            Assert.IsFalse(JsonFormatter.LooksLikeJson("<html>"));
            Assert.IsFalse(JsonFormatter.LooksLikeJson("42"));
            Assert.IsFalse(JsonFormatter.LooksLikeJson(""));
        }
    }

    public class MarkupFormatterTests
    {
        private static string Format(string markup)
        {
            Assert.IsTrue(MarkupFormatter.TryFormat(markup, out var formatted), "expected well-formed markup: " + markup);

            return formatted;
        }

        [Test]
        public void NestedTags_GetOneLineEach_WithIndent()
        {
            Assert.AreEqual(
                "<html>\n  <body>\n    <div class=\"a\">\n      <p>\n        Hello\n      </p>\n    </div>\n  </body>\n</html>",
                Format("<html><body><div class=\"a\"><p>Hello</p></div></body></html>"));
        }

        [Test]
        public void VoidAndSelfClosingElements_DoNotNest()
        {
            Assert.AreEqual(
                "<div>\n  <br>\n  <img src=\"x.png\">\n  <input type=\"text\" />\n  text\n</div>",
                Format("<div><br><img src=\"x.png\"><input type=\"text\" />text</div>"));
        }

        [Test]
        public void ScriptAndStyle_KeepTheirLines_UnderAUniformIndent()
        {
            Assert.AreEqual(
                "<html>\n  <script>\n    if (a < b) { x(); }\n      indented();\n  </script>\n</html>",
                Format("<html><script>\nif (a < b) { x(); }\n  indented();\n</script></html>"));
        }

        [Test]
        public void PreAndTextarea_AreEmittedExactly_WhitespaceIsContent()
        {
            Assert.AreEqual(
                "<html>\n  <pre>\n    keep   this\n  and this\n  </pre>\n  <textarea>\n x \n  </textarea>\n</html>",
                Format("<html><pre>    keep   this\n  and this</pre><textarea> x </textarea></html>"));
        }

        [Test]
        public void DoctypeCommentsAndXmlDeclarations_PassThrough()
        {
            Assert.AreEqual(
                "<!DOCTYPE html>\n<!-- a > b -->\n<html>\n  <p>\n    x\n  </p>\n</html>",
                Format("<!DOCTYPE html><!-- a > b --><html><p>x</p></html>"));

            // Tags are copied verbatim: the self-closing "/>" is not normalised to " />".
            Assert.AreEqual(
                "<?xml version=\"1.0\"?>\n<root>\n  <item id=\"1\"/>\n  <item>\n    <![CDATA[ raw < stuff ]]>\n  </item>\n</root>",
                Format("<?xml version=\"1.0\"?><root><item id=\"1\"/><item><![CDATA[ raw < stuff ]]></item></root>"));
        }

        [Test]
        public void AttributesWithAngleBrackets_InQuotes_AreFine()
        {
            Assert.AreEqual("<a title=\"x > y\">\n  link\n</a>", Format("<a title=\"x > y\">link</a>"));
        }

        [Test]
        public void ExistingWhitespace_IsNormalised()
        {
            Assert.AreEqual("<ul>\n  <li>\n    one\n  </li>\n  <li>\n    two\n  </li>\n</ul>", Format("<ul>\n  <li>one</li>\n\n  <li>two</li>\n</ul>\n"));
        }

        [Test]
        public void RealPage_WithStyleAndScript_AndUnquotedAttributes_Formats()
        {
            // example.com as served in 2026: unquoted attribute values, a style block, an empty script element.
            const string page = "<!doctype html><html lang=en><head><meta charset=utf-8><meta name=viewport content=\"width=device-width,initial-scale=1\"><title>Example Domain</title><style>html{color-scheme:light dark;background:light-dark(#eee,#222)}body{font:16px/1.6 system-ui,sans-serif;max-width:26em;margin:auto;padding:25vh 2em 2em;text-align:center}</style></head><body><p>This domain is for use in documentation examples without needing permission. This is not a service; avoid relying on it for testing and monitoring purposes.</p><script src=/s.js></script></body></html>";

            Assert.AreEqual(
                "<!doctype html>\n" +
                "<html lang=en>\n" +
                "  <head>\n" +
                "    <meta charset=utf-8>\n" +
                "    <meta name=viewport content=\"width=device-width,initial-scale=1\">\n" +
                "    <title>\n" +
                "      Example Domain\n" +
                "    </title>\n" +
                "    <style>\n" +
                "      html{color-scheme:light dark;background:light-dark(#eee,#222)}body{font:16px/1.6 system-ui,sans-serif;max-width:26em;margin:auto;padding:25vh 2em 2em;text-align:center}\n" +
                "    </style>\n" +
                "  </head>\n" +
                "  <body>\n" +
                "    <p>\n" +
                "      This domain is for use in documentation examples without needing permission. This is not a service; avoid relying on it for testing and monitoring purposes.\n" +
                "    </p>\n" +
                "    <script src=/s.js>\n" +
                "    </script>\n" +
                "  </body>\n" +
                "</html>",
                Format(page));
        }

        [Test]
        public void Unbalanced_ReturnsFalse_SoTheViewerShowsRaw()
        {
            foreach (var bad in new[] { "<div><p></div>", "<div>", "</div>", "<div>a<b</div>", "plain text", "", "<script>never closed", "<div></span></div>" })
                Assert.IsFalse(MarkupFormatter.TryFormat(bad, out _), "should reject: " + bad);
        }
    }

    public class BodyKindTests
    {
        private static BodyKind Detect(string text, string contentType = null) => BodyKindDetector.Detect(Encoding.UTF8.GetBytes(text), contentType);

        [Test]
        public void ContentType_Wins()
        {
            Assert.AreEqual(BodyKind.Json, Detect("{}", "application/json; charset=utf-8"));
            Assert.AreEqual(BodyKind.Json, Detect("{}", "application/problem+json"));
            Assert.AreEqual(BodyKind.Markup, Detect("<a></a>", "text/html"));
            Assert.AreEqual(BodyKind.Markup, Detect("<a/>", "application/xml"));
            Assert.AreEqual(BodyKind.Text, Detect("{}", "text/plain"), "the header says text, so text");
        }

        [Test]
        public void WithoutContentType_OrAGenericOne_TheBytesAreSniffed()
        {
            Assert.AreEqual(BodyKind.Json, Detect("  {\"a\":1}"));
            Assert.AreEqual(BodyKind.Json, Detect("[1,2]"));
            Assert.AreEqual(BodyKind.Json, Detect("[1,2]", "application/octet-stream"));
            Assert.AreEqual(BodyKind.Json, Detect("[1,2]", "*/*"));
            Assert.AreEqual(BodyKind.Markup, Detect("<!DOCTYPE html><html></html>"));
            Assert.AreEqual(BodyKind.Text, Detect("hello"));
        }

        [Test]
        public void Sniff_IgnoresTheDeclaredType_SoTheViewerCanStillOfferPretty()
        {
            Assert.AreEqual(BodyKind.Json, BodyKindDetector.Sniff(Encoding.UTF8.GetBytes("{\"mislabelled\":true}")));
            Assert.AreEqual(BodyKind.Markup, BodyKindDetector.Sniff(Encoding.UTF8.GetBytes("<a/>")));
            Assert.AreEqual(BodyKind.Text, BodyKindDetector.Sniff(Encoding.UTF8.GetBytes("plain")));
            Assert.AreEqual(BodyKind.Empty, BodyKindDetector.Sniff(new byte[0]));
        }

        [Test]
        public void ImagesAndBinary_AreRecognised()
        {
            Assert.AreEqual(BodyKind.Image, BodyKindDetector.Detect(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, null));
            Assert.AreEqual(BodyKind.Image, BodyKindDetector.Detect(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, null));
            Assert.AreEqual(BodyKind.Image, BodyKindDetector.Detect(new byte[] { 1, 2, 3 }, "image/webp"));
            Assert.AreEqual(BodyKind.Binary, BodyKindDetector.Detect(new byte[] { 0, 1, 2, 3 }, "application/octet-stream"));
            Assert.AreEqual(BodyKind.Binary, BodyKindDetector.Detect(new byte[] { 0, 1, 2, 3 }, "application/json"), "bytes say binary whatever the header claims");
            Assert.AreEqual(BodyKind.Empty, BodyKindDetector.Detect(new byte[0], "text/plain"));
            Assert.AreEqual(BodyKind.Empty, BodyKindDetector.Detect(null, null));
        }
    }

    public class BodyViewHelperTests
    {
        [Test]
        public void LineNumbers_AreRightAligned()
        {
            Assert.AreEqual("1", BodyView.LineNumbers(1));
            Assert.AreEqual(" 1\n 2\n 3\n 4\n 5\n 6\n 7\n 8\n 9\n10", BodyView.LineNumbers(10));
            Assert.AreEqual(3, BodyView.CountLines("a\nb\nc"));
            Assert.AreEqual(1, BodyView.CountLines("no newline"));
        }

        [Test]
        public void HexPreview_FormatsOffsetBytesAndAscii()
        {
            var bytes = Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOPQ");
            bytes[0] = 0;
            var text = BodyView.HexPreview(bytes);

            Assert.That(text, Does.StartWith("00000000  00 42 43 44 45 46 47 48  49 4a 4b 4c 4d 4e 4f 50  .BCDEFGHIJKLMNOP\n"));
            Assert.That(text, Does.Contain("00000010  51 "));
            Assert.That(text, Does.EndWith("Q\n"));
        }

        [Test]
        public void HexPreview_RespectsTheCap()
        {
            var text = BodyView.HexPreview(new byte[100], 32);

            Assert.That(text, Does.Contain("… 68 more bytes"));
            Assert.AreEqual(2, text.Split('\n').Length - 1, "two full rows before the note");
        }
    }
}
