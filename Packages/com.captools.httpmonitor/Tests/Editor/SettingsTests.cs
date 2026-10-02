using System.Linq;
using HttpMonitor.CodeGen;
using HttpMonitor.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HttpMonitor.Tests.Editor
{
    public class WeaverConfigTests
    {
        [Test]
        public void Parse_ReadsEveryKey_AndIgnoresCommentsAndNoise()
        {
            var config = WeaverConfig.Parse("# comment\n\nweavingEnabled = false\r\nweaveReleaseBuilds=TRUE\nexcludedAssemblies= A ; B;;C \nunknown=1\nnoequals\n");

            Assert.IsFalse(config.WeavingEnabled);
            Assert.IsTrue(config.WeaveReleaseBuilds);
            Assert.AreEqual(new[] { "A", "B", "C" }, config.ExcludedAssemblies.ToArray());
            Assert.IsTrue(config.IsExcluded("b"), "case-insensitive");
            Assert.IsFalse(config.IsExcluded("D"));
        }

        [Test]
        public void Parse_EmptyOrNull_IsDefault()
        {
            foreach (var text in new[] { null, "", "   \n" })
            {
                var config = WeaverConfig.Parse(text);
                Assert.IsTrue(config.WeavingEnabled);
                Assert.IsFalse(config.WeaveReleaseBuilds);
                Assert.IsEmpty(config.ExcludedAssemblies);
            }
        }

        [Test]
        public void Serialize_RoundTrips()
        {
            var original = new WeaverConfig { WeavingEnabled = false, WeaveReleaseBuilds = true, ExcludedAssemblies = { "One", "Two" } };
            var parsed = WeaverConfig.Parse(original.Serialize());

            Assert.AreEqual(original.WeavingEnabled, parsed.WeavingEnabled);
            Assert.AreEqual(original.WeaveReleaseBuilds, parsed.WeaveReleaseBuilds);
            Assert.AreEqual(original.ExcludedAssemblies, parsed.ExcludedAssemblies);
            Assert.That(original.Serialize(), Does.StartWith("#"), "the file explains itself");
        }

        [Test]
        public void Load_MissingFile_IsDefault()
        {
            var config = WeaverConfig.Load(System.IO.Path.GetTempPath());

            Assert.IsTrue(config.WeavingEnabled);
        }
    }

    public class WeaveDecisionTests
    {
        private static readonly string[] Editor = { "UNITY_EDITOR", "UNITY_64" };
        private static readonly string[] DevPlayer = { "UNITY_ANDROID", "DEVELOPMENT_BUILD" };
        private static readonly string[] ReleasePlayer = { "UNITY_ANDROID" };

        [Test]
        public void SdkAndEngineAssemblies_AreNeverWoven()
        {
            foreach (var name in new[] { "HttpMonitor.Runtime", "HttpMonitor.Editor", "Unity.Burst", "UnityEngine.CoreModule", "UnityEditor.CoreModule", "System.Net.Http", "mscorlib", "netstandard", "nunit.framework", "Mono.Cecil" })
                Assert.IsFalse(WeaveDecision.ShouldWeave(name, Editor, WeaverConfig.Default), name);
        }

        [Test]
        public void UserAssemblies_AreWoven_InEditorAndDevelopmentBuilds_ByDefault()
        {
            Assert.IsTrue(WeaveDecision.ShouldWeave("Assembly-CSharp", Editor, WeaverConfig.Default));
            Assert.IsTrue(WeaveDecision.ShouldWeave("Assembly-CSharp", DevPlayer, WeaverConfig.Default));
            Assert.IsTrue(WeaveDecision.ShouldWeave("HttpMonitor.Tests", Editor, WeaverConfig.Default), "the SDK's own tests are woven on purpose");
        }

        [Test]
        public void UnknownContext_IsTreatedAsARelease_Build()
        {
            // No defines means no evidence of Editor or development build. The safe failure is the
            // visible one (capture silently off) rather than the invisible one (capture code shipped).
            Assert.IsFalse(WeaveDecision.ShouldWeave("Assembly-CSharp", null, WeaverConfig.Default));
            Assert.IsFalse(WeaveDecision.ShouldWeave("Assembly-CSharp", new string[0], WeaverConfig.Default));
            Assert.IsTrue(WeaveDecision.ShouldWeave("Assembly-CSharp", null, new WeaverConfig { WeaveReleaseBuilds = true }));
        }

        [Test]
        public void ReleaseBuilds_AreNotWoven_UnlessOptedIn()
        {
            Assert.IsFalse(WeaveDecision.ShouldWeave("Assembly-CSharp", ReleasePlayer, WeaverConfig.Default));
            Assert.IsTrue(WeaveDecision.ShouldWeave("Assembly-CSharp", ReleasePlayer, new WeaverConfig { WeaveReleaseBuilds = true }));
        }

        [Test]
        public void WeavingDisabled_ExcludedAssemblies_AndTheDefine_AllStopWeaving()
        {
            Assert.IsFalse(WeaveDecision.ShouldWeave("Assembly-CSharp", Editor, new WeaverConfig { WeavingEnabled = false }));
            Assert.IsFalse(WeaveDecision.ShouldWeave("ThirdParty", Editor, new WeaverConfig { ExcludedAssemblies = { "thirdparty" } }));
            Assert.IsTrue(WeaveDecision.ShouldWeave("ThirdParty.Other", Editor, new WeaverConfig { ExcludedAssemblies = { "ThirdParty" } }), "exact match, not prefix");
            Assert.IsFalse(WeaveDecision.ShouldWeave("Assembly-CSharp", new[] { "UNITY_EDITOR", WeaveDecision.DisableDefine }, WeaverConfig.Default));
        }

        [Test]
        public void NullConfig_MeansDefaults()
        {
            Assert.IsTrue(WeaveDecision.ShouldWeave("Assembly-CSharp", Editor, null));
            Assert.IsFalse(WeaveDecision.ShouldWeave("Assembly-CSharp", ReleasePlayer, null));
        }
    }

    public class HttpMonitorSettingsTests
    {
        [Test]
        public void ApplyTo_CopiesEveryCaptureField_AndNormalisesHeaders()
        {
            var settings = ScriptableObject.CreateInstance<HttpMonitorSettings>();

            try
            {
                settings.CaptureBodies = false;
                settings.MaxBodyKilobytes = 3;
                settings.MaxTotalBodyMegabytes = 2;
                settings.BufferUnknownLengthResponses = false;
                settings.RedactedValue = "***";
                settings.RedactedHeaders = new[] { " X-Api-Key ", "", null, "Cookie" };

                var options = new HttpMonitorOptions();
                settings.ApplyTo(options);

                Assert.IsFalse(options.CaptureBodies);
                Assert.AreEqual(3 * 1024, options.MaxBodyBytes);
                Assert.AreEqual(2L * 1024 * 1024, options.MaxTotalBodyBytes);
                Assert.IsFalse(options.BufferUnknownLengthResponses);
                Assert.AreEqual("***", options.RedactedValue);
                Assert.AreEqual(new[] { "X-Api-Key", "Cookie" }, options.RedactedHeaders.OrderBy(h => h).Reverse().ToArray());
                Assert.IsTrue(options.IsRedacted("x-api-key"));
                Assert.IsFalse(options.IsRedacted("Authorization"), "the asset's list replaces the defaults rather than adding to them");
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void Defaults_MatchTheOptionsDefaults()
        {
            var settings = ScriptableObject.CreateInstance<HttpMonitorSettings>();

            try
            {
                var fromSettings = new HttpMonitorOptions();
                settings.ApplyTo(fromSettings);
                var pristine = new HttpMonitorOptions();

                Assert.AreEqual(pristine.CaptureBodies, fromSettings.CaptureBodies);
                Assert.AreEqual(pristine.MaxBodyBytes, fromSettings.MaxBodyBytes);
                Assert.AreEqual(pristine.MaxTotalBodyBytes, fromSettings.MaxTotalBodyBytes);
                Assert.AreEqual(pristine.BufferUnknownLengthResponses, fromSettings.BufferUnknownLengthResponses);
                Assert.AreEqual(pristine.RedactedValue, fromSettings.RedactedValue);
                Assert.AreEqual(pristine.RedactedHeaders.OrderBy(h => h), fromSettings.RedactedHeaders.OrderBy(h => h));
                Assert.IsTrue(settings.WeavingEnabled);
                Assert.IsFalse(settings.WeaveReleaseBuilds);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void ResetToDefaults_OnOptions_RestoresTheFourStandardHeaders()
        {
            var options = new HttpMonitorOptions();
            options.RedactedHeaders.Clear();
            options.CaptureBodies = false;

            options.ResetToDefaults();

            Assert.IsTrue(options.CaptureBodies);
            Assert.AreEqual(4, options.RedactedHeaders.Count);
            Assert.IsTrue(options.IsRedacted("Set-Cookie"));
        }

        [Test]
        public void ToWeaverConfig_TrimsAndDropsEmptyNames()
        {
            var settings = ScriptableObject.CreateInstance<HttpMonitorSettings>();

            try
            {
                settings.WeavingEnabled = false;
                settings.ExcludedAssemblies = new[] { " A ", "", null, "B" };

                var config = HttpMonitorSettingsEditor.ToWeaverConfig(settings);

                Assert.IsFalse(config.WeavingEnabled);
                Assert.AreEqual(new[] { "A", "B" }, config.ExcludedAssemblies.ToArray());
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void SettingsProvider_IsRegistered()
        {
            var provider = HttpMonitorSettingsProvider.Create();

            Assert.AreEqual("Project/HTTP Monitor", provider.settingsPath);
            Assert.AreEqual(SettingsScope.Project, provider.scope);
            Assert.That(provider.keywords, Does.Contain("redact"));
        }
    }
}
