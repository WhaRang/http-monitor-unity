using System;
using System.Collections;
using System.Collections.Generic;
using HttpMonitor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;

namespace Spike.Tests.WithRef
{
    /// <summary>
    /// Unlike Spike.Tests, this assembly references HttpMonitor.Runtime on purpose: it exercises the
    /// public logging contract.
    /// </summary>
    public class LoggerInjectionTests
    {
        private sealed class CapturingLogger : IHttpMonitorLogger
        {
            public readonly List<string> Info = new List<string>();
            public readonly List<string> Warnings = new List<string>();
            public readonly List<string> Errors = new List<string>();

            void IHttpMonitorLogger.Info(string message) => Info.Add(message);

            void IHttpMonitorLogger.Warning(string message) => Warnings.Add(message);

            void IHttpMonitorLogger.Error(string message) => Errors.Add(message);
        }

        private sealed class ThrowingLogger : IHttpMonitorLogger
        {
            public void Info(string message) => throw new InvalidOperationException("boom");

            public void Warning(string message) => throw new InvalidOperationException("boom");

            public void Error(string message) => throw new InvalidOperationException("boom");
        }

        [TearDown]
        public void RestoreDefaultLogger()
        {
            HttpMonitorLog.Logger = null;
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

        [UnityTest]
        public IEnumerator InjectedLogger_ReceivesRequestAndResponseLines_WithoutPrefix()
        {
            const string url = "https://example.com/?from=logger";
            var logger = new CapturingLogger();
            HttpMonitorLog.Logger = logger;

            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();
            }

            Assert.That(logger.Info, Has.Some.EqualTo("-> GET " + url));
            Assert.That(logger.Info, Has.Some.StartsWith("<- 200 Success " + url));
            Assert.That(logger.Warnings, Is.Empty);
            Assert.That(logger.Errors, Is.Empty);
        }

        [UnityTest]
        public IEnumerator ThrowingLogger_DoesNotBreakTheRequest()
        {
            const string url = "https://example.com/?from=throwing-logger";
            HttpMonitorLog.Logger = new ThrowingLogger();
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"^\[HttpMonitor\] the injected ThrowingLogger threw"));

            using (var request = UnityWebRequest.Get(url))
            {
                yield return request.SendWebRequest();
                Assert.AreEqual(UnityWebRequest.Result.Success, request.result, request.error);
            }
        }
    }
}
