using System.Linq;
using HttpMonitor.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace HttpMonitor.Tests.Editor
{
    public class DetailWindowTests
    {
        private static EditorRecord Record(long id)
        {
            return new EditorRecord { Id = id, Method = "GET", Url = "https://h/p/" + id, State = HttpRecordState.Completed, StatusCode = 200, RequestHeaders = new EditorHeader[0], ResponseHeaders = new EditorHeader[0] };
        }

        [TearDown]
        public void CloseAll()
        {
            foreach (var window in DetailWindow.All.ToList())
                window.Close();
        }

        [Test]
        public void Title_IsCompact()
        {
            Assert.AreEqual("HTTP Detail", DetailWindow.Title(null));
            Assert.AreEqual("GET 200 /p/1", DetailWindow.Title(Record(1)));

            var longUrl = Record(2);
            longUrl.Url = "https://h/" + new string('x', 80);
            Assert.That(DetailWindow.Title(longUrl), Does.EndWith("…").And.Length.LessThan(60));
        }

        [Test]
        public void Follower_IsSingle_AndPinnedWindowsAreMany()
        {
            var anchor = new Rect(0, 0, 800, 600);
            var first = DetailWindow.OpenFollower(null, anchor);
            var again = DetailWindow.OpenFollower(null, anchor);

            Assert.AreSame(first, again, "opening the follower twice reuses it");
            Assert.AreSame(first, DetailWindow.Follower);
            Assert.IsFalse(first.IsPinned);

            var pinnedA = DetailWindow.OpenPinned(Record(1), anchor);
            var pinnedB = DetailWindow.OpenPinned(Record(2), anchor);

            Assert.IsTrue(pinnedA.IsPinned);
            Assert.IsTrue(pinnedB.IsPinned);
            Assert.AreEqual(3, DetailWindow.All.Count());
            Assert.AreSame(first, DetailWindow.Follower, "pinned windows never count as the follower");
        }

        [Test]
        public void Unpinning_RetiresTheExistingFollower()
        {
            var anchor = new Rect(0, 0, 800, 600);
            var follower = DetailWindow.OpenFollower(null, anchor);
            var pinned = DetailWindow.OpenPinned(Record(1), anchor);

            pinned.SetPinned(false);

            Assert.IsFalse(pinned.IsPinned);
            Assert.AreSame(pinned, DetailWindow.Follower);
            Assert.IsTrue(follower == null, "the previous follower was closed");
        }

        [Test]
        public void FollowerClosed_FiresForFollowers_NotForPinned()
        {
            var anchor = new Rect(0, 0, 800, 600);
            var fired = 0;
            System.Action handler = () => fired++;
            DetailWindow.FollowerClosed += handler;

            try
            {
                DetailWindow.OpenPinned(Record(1), anchor).Close();
                Assert.AreEqual(0, fired);

                DetailWindow.OpenFollower(null, anchor).Close();
                Assert.AreEqual(1, fired);
            }
            finally
            {
                DetailWindow.FollowerClosed -= handler;
            }
        }

        [Test]
        public void MissingRecord_ShowsAnExplanation_InsteadOfThePane()
        {
            var window = DetailWindow.OpenPinned(Record(999999), new Rect(0, 0, 800, 600));

            // The record was never in the store, which is what eviction or Clear looks like after a reload.
            Assert.AreEqual(999999, window.RecordId);
            Assert.IsNull(window.Record);

            var labels = window.rootVisualElement.Query<Label>().ToList().Select(l => l.text).ToList();
            Assert.That(labels, Has.Some.Contains("#999999 is no longer available"));
        }
    }

    public class DetailPanePopOutTests
    {
        [Test]
        public void PoppedOut_ShowsTheNotice_AndTracksTheSelectionSummary()
        {
            var pane = new DetailPane();
            var record = new EditorRecord { Id = 3, Method = "POST", Url = "https://h/x", State = HttpRecordState.Completed, StatusCode = 201, RequestHeaders = new EditorHeader[0], ResponseHeaders = new EditorHeader[0] };

            pane.Show(record);
            pane.SetPoppedOut(true);

            Assert.IsTrue(pane.IsPoppedOut);
            Assert.AreEqual(DisplayStyle.Flex, pane.Q("hm-popped-out").style.display.value);
            Assert.AreEqual(DisplayStyle.None, pane.Q(className: "hm-detail-content").style.display.value);
            Assert.That(pane.Query<Label>().ToList().Select(l => l.text), Has.Some.Contains("POST 201 /x"));

            var docked = 0;
            pane.DockBackRequested += () => docked++;
            pane.Q<Button>(className: "hm-popped-out-button").SendEvent(new ClickEvent { target = pane.Q<Button>(className: "hm-popped-out-button") });

            pane.SetPoppedOut(false);
            Assert.AreEqual(DisplayStyle.None, pane.Q("hm-popped-out").style.display.value);
            Assert.AreEqual(DisplayStyle.Flex, pane.Q(className: "hm-detail-content").style.display.value);
        }

        [Test]
        public void LayoutButtons_CanBeHidden()
        {
            var pane = new DetailPane();
            pane.SetLayoutButtonsVisible(false);

            Assert.AreEqual(DisplayStyle.None, pane.Q<Button>(className: "hm-maximize").style.display.value);
        }
    }
}
