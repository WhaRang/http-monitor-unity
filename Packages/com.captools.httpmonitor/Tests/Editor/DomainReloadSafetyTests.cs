using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HttpMonitor.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace HttpMonitor.Tests.Editor
{
    /// <summary>
    /// A domain reload serializes the private fields of UnityEngine.Objects, EditorWindows included,
    /// whether or not they carry [SerializeField]. A transient flag ("refresh already scheduled",
    /// "already subscribed") that is set just before a reload therefore comes back set, while the
    /// callback or subscription it guarded is gone. That bug left the traffic window deaf to new
    /// requests after pressing Play.
    ///
    /// The rule this test enforces: every private instance field of a serializable type, on every
    /// EditorWindow and ScriptableObject in the Editor assembly, declares its intent. Either
    /// [SerializeField] (it is meant to survive) or [NonSerialized] (it is not).
    /// </summary>
    public class DomainReloadSafetyTests
    {
        private static IEnumerable<Type> PersistentTypes()
        {
            return typeof(HttpMonitorWindow).Assembly.GetTypes()
                .Where(t => !t.IsAbstract && (typeof(EditorWindow).IsAssignableFrom(t) || typeof(ScriptableObject).IsAssignableFrom(t)));
        }

        /// <summary>Would Unity's hot-reload serializer pick this field type up?</summary>
        private static bool IsUnitySerializable(Type type)
        {
            if (type.IsPrimitive || type == typeof(string) || type.IsEnum)
                return true;

            if (type.IsArray)
                return IsUnitySerializable(type.GetElementType());

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                return IsUnitySerializable(type.GetGenericArguments()[0]);

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                return true;

            return type.IsSerializable && !typeof(Delegate).IsAssignableFrom(type);
        }

        [Test]
        public void EveryPersistentType_DeclaresIntent_ForEachSerializablePrivateField()
        {
            var offenders = new List<string>();

            foreach (var type in PersistentTypes())
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsInitOnly || field.Name.Contains("k__BackingField") || !IsUnitySerializable(field.FieldType))
                        continue;

                    var declared = field.IsDefined(typeof(SerializeField), false) || field.IsNotSerialized;

                    if (!declared)
                        offenders.Add($"{type.Name}.{field.Name} ({field.FieldType.Name})");
                }
            }

            Assert.IsEmpty(offenders,
                "These fields survive a domain reload by accident. Mark each [SerializeField] if it should persist or [NonSerialized] if it is transient:\n  "
                + string.Join("\n  ", offenders));
        }

        [Test]
        public void TheCheck_SeesTheWindowsItIsMeantToGuard()
        {
            var names = PersistentTypes().Select(t => t.Name).ToList();

            Assert.Contains(nameof(HttpMonitorWindow), names);
            Assert.Contains(nameof(DetailWindow), names);
            Assert.Contains(nameof(RequestComposerWindow), names);
            Assert.Contains(nameof(EditorRecordStore), names);
        }

        [Test]
        public void TheSerializableRule_MatchesWhatUnityDoes()
        {
            Assert.IsTrue(IsUnitySerializable(typeof(bool)));
            Assert.IsTrue(IsUnitySerializable(typeof(string)));
            Assert.IsTrue(IsUnitySerializable(typeof(List<EditorRecord>)), "a list of a [Serializable] class");
            Assert.IsTrue(IsUnitySerializable(typeof(Texture2D)));
            Assert.IsFalse(IsUnitySerializable(typeof(RecordQuery)), "a plain class without [Serializable] is skipped by Unity");
            Assert.IsFalse(IsUnitySerializable(typeof(Action)));
            Assert.IsFalse(IsUnitySerializable(typeof(UnityEngine.UIElements.Label)), "visual elements are not serialized");
        }
    }
}
