using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace HttpMonitor.Tests
{
    public class LoggerInjectionTests
    {
        private sealed class ThrowingLogger : IHttpMonitorLogger
        {
            public void Info(string message) => throw new InvalidOperationException("boom");

            public void Warning(string message) => throw new InvalidOperationException("boom");

            public void Error(string message) => throw new InvalidOperationException("boom");
        }

        private static void ThrowingSubscriber(HttpRecord record) => throw new InvalidOperationException("subscriber failed");

        [TearDown]
        public void RestoreDefaults()
        {
            HttpMonitorLog.Logger = null;
            HttpMonitorSession.Current.RecordAdded -= ThrowingSubscriber;
        }

        [Test]
        public void DefaultLogger_IsUnityDebugLogger_AndNullRestoresIt()
        {
            Assert.IsInstanceOf<UnityDebugLogger>(HttpMonitorLog.Logger);

            HttpMonitorLog.Logger = new CapturingLogger();
            Assert.IsInstanceOf<CapturingLogger>(HttpMonitorLog.Logger);

            HttpMonitorLog.Logger = null;
            Assert.IsInstanceOf<UnityDebugLogger>(HttpMonitorLog.Logger);
        }

        [Test]
        public void SubscriberThatThrows_IsReportedToInjectedLogger_AndDoesNotPropagate()
        {
            var logger = new CapturingLogger();
            HttpMonitorLog.Logger = logger;

            var session = new HttpMonitorSession(4);
            session.RecordAdded += ThrowingSubscriber;

            var record = session.Begin(HttpClientKind.Manual, "GET", "https://example.com/", null);

            Assert.NotNull(record);
            Assert.AreEqual(1, session.Count);
            Assert.That(logger.Warnings, Has.Count.EqualTo(1));
            Assert.That(logger.Warnings[0], Does.Contain("RecordAdded").And.Contain("subscriber failed"));
            Assert.That(logger.Infos, Is.Empty);
            Assert.That(logger.Errors, Is.Empty);
        }

        [Test]
        public void LoggerThatThrows_IsReportedToUnityConsole_AndDoesNotPropagate()
        {
            HttpMonitorLog.Logger = new ThrowingLogger();
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[HttpMonitor\] the injected ThrowingLogger threw"));

            var session = new HttpMonitorSession(4);
            session.RecordAdded += ThrowingSubscriber;

            var record = session.Begin(HttpClientKind.Manual, "GET", "https://example.com/", null);

            Assert.NotNull(record);
        }
    }
}
