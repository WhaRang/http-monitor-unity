using System.Linq;
using System.Text;
using HttpMonitor.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace HttpMonitor.Tests.Editor
{
    public class ReplayControllerTests
    {
        [Test]
        public void Decide_OpensTheComposer_WhenASecretIsMissing()
        {
            var request = new ReplayRequest { Method = "GET", RedactedHeaderNames = { "Authorization" } };

            Assert.AreEqual(ReplayController.Decision.OpenComposer, ReplayController.Decide(request, confirmUnsafe: true));
            Assert.AreEqual(ReplayController.Decision.OpenComposer, ReplayController.Decide(request, confirmUnsafe: false));
        }

        [Test]
        public void Decide_Confirms_UnsafeMethods_OnlyWhileThePreferenceIsOn()
        {
            Assert.AreEqual(ReplayController.Decision.Confirm, ReplayController.Decide(new ReplayRequest { Method = "POST" }, confirmUnsafe: true));
            Assert.AreEqual(ReplayController.Decision.Send, ReplayController.Decide(new ReplayRequest { Method = "POST" }, confirmUnsafe: false));
            Assert.AreEqual(ReplayController.Decision.Send, ReplayController.Decide(new ReplayRequest { Method = "GET" }, confirmUnsafe: true));
            Assert.AreEqual(ReplayController.Decision.Send, ReplayController.Decide(new ReplayRequest { Method = "head" }, confirmUnsafe: true));
        }
    }

    public class ReplaySecretsTests
    {
        [SetUp]
        [TearDown]
        public void Clear()
        {
            ReplaySecrets.Forget();
        }

        [Test]
        public void Remember_IsScopedByHost_AndCaseInsensitiveOnTheName()
        {
            ReplaySecrets.Remember("api.game.com", "Authorization", "Bearer a");
            ReplaySecrets.Remember("cdn.game.com", "Authorization", "Bearer c");

            Assert.IsTrue(ReplaySecrets.TryGet("api.game.com", "authorization", out var a));
            Assert.AreEqual("Bearer a", a);
            Assert.IsTrue(ReplaySecrets.TryGet("cdn.game.com", "Authorization", out var c));
            Assert.AreEqual("Bearer c", c);
            Assert.IsFalse(ReplaySecrets.TryGet("other.game.com", "Authorization", out _));
            Assert.AreEqual(2, ReplaySecrets.Count);
        }

        [Test]
        public void Remember_EmptyValue_Forgets()
        {
            ReplaySecrets.Remember("h", "X", "v");
            ReplaySecrets.Remember("h", "X", "");

            Assert.AreEqual(0, ReplaySecrets.Count);
        }

        [Test]
        public void Fill_SuppliesWhatItKnows_AndReportsWhetherAnythingIsStillMissing()
        {
            ReplaySecrets.Remember("api.game.com", "Authorization", "Bearer a");
            var request = new ReplayRequest
            {
                Url = "https://api.game.com/x",
                Headers = { new EditorHeader("Authorization", ""), new EditorHeader("Cookie", "") },
                RedactedHeaderNames = { "Authorization", "Cookie" },
            };

            Assert.IsFalse(ReplaySecrets.Fill(request), "Cookie is still unknown");
            Assert.AreEqual("Bearer a", request.Headers[0].Value);
            Assert.AreEqual(new[] { "Cookie" }, request.RedactedHeaderNames.ToArray());

            ReplaySecrets.Remember("api.game.com", "Cookie", "s=1");
            Assert.IsTrue(ReplaySecrets.Fill(request));
            Assert.AreEqual("s=1", request.Headers[1].Value);
        }
    }

    public class ReplayBadgeTests
    {
        [Test]
        public void Replay_TakesPrecedence_InBadgeClassAndText()
        {
            var replay = new EditorRecord { Id = 9, ReplayOfId = 4, Source = HttpCaptureSource.Manual, Imported = true };

            Assert.AreEqual("R", RecordFormat.SourceBadge(replay));
            Assert.AreEqual("hm-source-badge--replay", RecordFormat.SourceClass(replay));
            Assert.AreEqual("replay of #4, sent from the Editor", RecordFormat.SourceText(replay));

            var plain = new EditorRecord { Source = HttpCaptureSource.Manual };
            Assert.AreEqual("M", RecordFormat.SourceBadge(plain));
        }

        [Test]
        public void DetailPane_ShowsTheReplayLink_OnlyForReplays()
        {
            var pane = new DetailPane();
            pane.Show(new EditorRecord { Id = 9, ReplayOfId = 4, Method = "GET", Url = "http://a/", State = HttpRecordState.Completed, StatusCode = 200 });

            var link = pane.Q<Label>(className: "hm-summary-replay-of");
            Assert.AreEqual(DisplayStyle.Flex, link.style.display.value);
            Assert.AreEqual("↻ replay of #4", link.text);

            pane.Show(new EditorRecord { Id = 10, Method = "GET", Url = "http://a/", State = HttpRecordState.Completed, StatusCode = 200 });
            Assert.AreEqual(DisplayStyle.None, link.style.display.value);

            Assert.NotNull(pane.Q<Button>(className: "hm-replay-button"), "the Replay button exists");
        }
    }

    public class RequestComposerWindowTests
    {
        [TearDown]
        public void CloseAll()
        {
            ReplaySecrets.Forget();

            foreach (var window in UnityEngine.Resources.FindObjectsOfTypeAll<RequestComposerWindow>())
                window.Close();
        }

        [Test]
        public void Open_PrefillsFromTheRequest_AndBuildsItBack()
        {
            var request = new ReplayRequest
            {
                Method = "POST",
                Url = "https://api.game.com/v1/score",
                Body = Encoding.UTF8.GetBytes("{\"score\":1}"),
                OriginalId = 12,
                FollowRedirects = false,
                TimeoutSeconds = 5,
                Headers = { new EditorHeader("Content-Type", "application/json"), new EditorHeader("Authorization", "") },
                RedactedHeaderNames = { "Authorization" },
            };

            var window = RequestComposerWindow.Open(request);

            Assert.AreEqual("Replay #12", window.titleContent.text);
            Assert.AreEqual("https://api.game.com/v1/score", window.rootVisualElement.Q<TextField>(className: "hm-composer-url").value);
            Assert.AreEqual("POST", window.rootVisualElement.Q<DropdownField>(className: "hm-composer-method").value);
            Assert.AreEqual(2, window.rootVisualElement.Query<VisualElement>(className: "hm-composer-header-row").ToList().Count);
            Assert.AreEqual(1, window.rootVisualElement.Query<Label>(className: "hm-redacted-badge").ToList().Count, "the redacted header is marked");

            var built = window.BuildRequest();
            Assert.AreEqual("POST", built.Method);
            Assert.AreEqual("{\"score\":1}", Encoding.UTF8.GetString(built.Body));
            Assert.IsFalse(built.FollowRedirects);
            Assert.AreEqual(5, built.TimeoutSeconds);
            Assert.AreEqual(12, built.OriginalId);
            Assert.IsTrue(built.HasRedactedHeaders, "nothing supplied yet");

            ReplaySecrets.Remember("api.game.com", "Authorization", "Bearer real");
            built = window.BuildRequest();
            Assert.IsFalse(built.HasRedactedHeaders);
            Assert.AreEqual("Bearer real", built.Headers.First(h => h.Name == "Authorization").Value);
            Assert.AreEqual(string.Empty, request.Headers.First(h => h.Name == "Authorization").Value, "the secret never lands in the serialized request");
        }

        [Test]
        public void Open_BinaryBody_IsKeptAsBytes_NotShownAsText()
        {
            var window = RequestComposerWindow.Open(new ReplayRequest { Method = "PUT", Url = "https://a/", Body = new byte[] { 0, 1, 2, 255 } });

            Assert.AreEqual(DisplayStyle.None, window.rootVisualElement.Q<TextField>(className: "hm-composer-body").style.display.value);
            Assert.AreEqual(new byte[] { 0, 1, 2, 255 }, window.BuildRequest().Body);
        }

        [Test]
        public void OpenEmpty_StartsWithAGetAndNoBody()
        {
            RequestComposerWindow.OpenEmpty();
            var window = UnityEngine.Resources.FindObjectsOfTypeAll<RequestComposerWindow>().Single();

            Assert.AreEqual("Compose request", window.titleContent.text);
            var built = window.BuildRequest();
            Assert.AreEqual("GET", built.Method);
            Assert.IsNull(built.Body);
            Assert.AreEqual(0, built.OriginalId);
        }
    }
}
