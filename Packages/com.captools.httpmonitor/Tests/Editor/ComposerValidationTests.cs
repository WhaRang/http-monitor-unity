using System.Text;
using HttpMonitor.Editor;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace HttpMonitor.Tests.Editor
{
    public class ComposerValidationTests
    {
        private static ReplayRequest Valid()
        {
            return new ReplayRequest { Method = "GET", Url = "https://api.game.com/v1/x", Headers = { new EditorHeader("Accept", "*/*") } };
        }

        [Test]
        public void ValidRequest_HasNoProblem()
        {
            Assert.IsNull(RequestComposerWindow.Validate(Valid()));
        }

        [Test]
        public void Url_MustBeAbsoluteHttpOrHttps()
        {
            foreach (var url in new[] { "", "   ", "api.game.com/x", "/relative", "not a url" })
            {
                var request = Valid();
                request.Url = url;
                Assert.That(RequestComposerWindow.Validate(request), Does.Contain("absolute URL"), "'" + url + "'");
            }

            var custom = Valid();
            custom.Url = "custom://service/items/1";
            Assert.That(RequestComposerWindow.Validate(custom), Does.Contain("Only http and https").And.Contain("custom"));

            var http = Valid();
            http.Url = "http://127.0.0.1:8080/";
            Assert.IsNull(RequestComposerWindow.Validate(http));
        }

        [Test]
        public void Method_MustBeSet()
        {
            var request = Valid();
            request.Method = " ";

            Assert.That(RequestComposerWindow.Validate(request), Does.Contain("method"));
        }

        [Test]
        public void HeaderRows_MustHaveNames()
        {
            var request = Valid();
            request.Headers.Add(new EditorHeader("", "value"));

            Assert.That(RequestComposerWindow.Validate(request), Does.Contain("no name"));
        }

        [Test]
        public void RedactedHeaders_BlockSending_AndAreNamed()
        {
            var request = Valid();
            request.Headers.Add(new EditorHeader("Authorization", ""));
            request.Headers.Add(new EditorHeader("Cookie", ""));
            request.RedactedHeaderNames.Add("Authorization");
            request.RedactedHeaderNames.Add("Cookie");

            Assert.That(RequestComposerWindow.Validate(request), Does.Contain("Authorization, Cookie"));

            request.SupplyRedactedValue("Authorization", "Bearer x");
            request.SupplyRedactedValue("Cookie", "s=1");
            Assert.IsNull(RequestComposerWindow.Validate(request));
        }
    }

    public class ComposerPrefillTests
    {
        [TearDown]
        public void CloseAll()
        {
            ReplaySecrets.Forget();

            foreach (var window in UnityEngine.Resources.FindObjectsOfTypeAll<RequestComposerWindow>())
                window.Close();
        }

        [Test]
        public void UnknownMethod_SelectsCustom_AndRoundTrips()
        {
            var window = RequestComposerWindow.Open(new ReplayRequest { Method = "PROPFIND", Url = "https://dav.example.com/" });

            Assert.AreEqual("Custom…", window.rootVisualElement.Q<DropdownField>(className: "hm-composer-method").value);
            Assert.AreEqual("PROPFIND", window.rootVisualElement.Q<TextField>(className: "hm-composer-custom-method").value);
            Assert.AreEqual("PROPFIND", window.BuildRequest().Method);
        }

        [Test]
        public void Url_IsTrimmed_WhenBuilt()
        {
            var window = RequestComposerWindow.Open(new ReplayRequest { Method = "GET", Url = "https://a/" });
            window.rootVisualElement.Q<TextField>(className: "hm-composer-url").value = "  https://a/b  ";

            Assert.AreEqual("https://a/b", window.BuildRequest().Url);
        }

        [Test]
        public void TextBody_IsEncodedAsUtf8_WhenBuilt()
        {
            var window = RequestComposerWindow.Open(new ReplayRequest { Method = "POST", Url = "https://a/", Body = Encoding.UTF8.GetBytes("{\"a\":1}") });
            var body = window.rootVisualElement.Q<TextField>(className: "hm-composer-body");

            Assert.AreEqual("{\"a\":1}", body.value, "prefilled as text");

            body.value = "{\"a\":2,\"é\":true}";
            Assert.AreEqual("{\"a\":2,\"é\":true}", Encoding.UTF8.GetString(window.BuildRequest().Body));

            body.value = "";
            Assert.IsNull(window.BuildRequest().Body, "an empty editor means no body");
        }

        [Test]
        public void RemovingARedactedHeader_ClearsItFromThePendingList()
        {
            var request = new ReplayRequest
            {
                Method = "GET",
                Url = "https://a/",
                Headers = { new EditorHeader("Authorization", ""), new EditorHeader("X-Keep", "1") },
                RedactedHeaderNames = { "Authorization" },
            };
            var window = RequestComposerWindow.Open(request);

            Assert.IsTrue(window.BuildRequest().HasRedactedHeaders);

            request.Headers.RemoveAt(0);
            request.RedactedHeaderNames.Clear();

            var built = window.BuildRequest();
            Assert.IsFalse(built.HasRedactedHeaders);
            Assert.AreEqual(1, built.Headers.Count);
            Assert.IsNull(RequestComposerWindow.Validate(built));
        }
    }
}
