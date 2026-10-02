using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// Project Settings ▸ HTTP Monitor. Edits the <see cref="HttpMonitorSettings"/> asset through a
    /// SerializedObject so undo and prefab-style dirtying work; weaving changes show a recompile
    /// banner, capture changes apply live. Editor-window limits (per machine, not per project) sit
    /// at the bottom and bind to the Editor store.
    /// </summary>
    internal static class HttpMonitorSettingsProvider
    {
        public const string Path = "Project/HTTP Monitor";

        private const string StyleSheetPath = "Packages/com.captools.httpmonitor/Editor/UI/HttpMonitorWindow.uss";

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider(Path, SettingsScope.Project)
            {
                label = "HTTP Monitor",
                keywords = new HashSet<string>(new[] { "http", "network", "request", "monitor", "weaving", "redact", "mock", "capture" }),
                activateHandler = (_, root) => Build(root),
            };
        }

        public static void Open()
        {
            SettingsService.OpenProjectSettings(Path);
        }

        private static void Build(VisualElement root)
        {
            root.Clear();
            root.AddToClassList("hm-settings");

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);

            if (styleSheet != null)
                root.styleSheets.Add(styleSheet);

            var title = new Label("HTTP Monitor");
            title.AddToClassList("hm-settings-title");
            root.Add(title);

            var settings = HttpMonitorSettingsEditor.Find();

            if (settings == null)
                BuildCreatePrompt(root);
            else
                BuildEditor(root, settings);

            BuildEditorWindowSection(root);
        }

        private static void BuildCreatePrompt(VisualElement root)
        {
            var box = new VisualElement();
            box.AddToClassList("hm-settings-box");

            var text = new Label("This project has no HTTP Monitor settings asset, so the defaults apply: automatic capture in the Editor and development builds, bodies capped at 1 MB, Authorization and cookie headers redacted.\n\nCreate the asset to change any of that. It lives in Assets, ships inside builds, and is meant to be committed.");
            text.AddToClassList("hm-settings-text");
            box.Add(text);

            var create = new Button(() =>
            {
                HttpMonitorSettingsEditor.GetOrCreate();
                Build(root);
            }) { text = "Create settings asset", tooltip = HttpMonitorSettings.DefaultAssetPath };
            create.AddToClassList("hm-settings-button");
            box.Add(create);

            root.Add(box);
        }

        private static void BuildEditor(VisualElement root, HttpMonitorSettings settings)
        {
            var serialized = new SerializedObject(settings);

            var location = new Label("Asset: " + AssetDatabase.GetAssetPath(settings)) { tooltip = "Click to select" };
            location.AddToClassList("hm-settings-location");
            location.RegisterCallback<ClickEvent>(_ => { Selection.activeObject = settings; EditorGUIUtility.PingObject(settings); });
            root.Add(location);

            // ---- weaving: compile-time, needs a recompile banner
            var weaving = Section(root, "Automatic capture (weaving)",
                "Call sites are rewritten when scripts compile. Changes here apply on the next compilation.");

            var banner = new VisualElement();
            banner.AddToClassList("hm-settings-banner");
            banner.style.display = DisplayStyle.None;
            var bannerText = new Label("Weaving settings changed. Scripts must recompile for them to apply.");
            bannerText.AddToClassList("hm-settings-banner-text");
            banner.Add(bannerText);
            var recompile = new Button(() =>
            {
                banner.style.display = DisplayStyle.None;
                HttpMonitorSettingsEditor.RequestRecompile();
            }) { text = "Recompile now" };
            banner.Add(recompile);
            weaving.Add(banner);

            foreach (var name in new[] { nameof(HttpMonitorSettings.WeavingEnabled), nameof(HttpMonitorSettings.WeaveReleaseBuilds), nameof(HttpMonitorSettings.ExcludedAssemblies) })
            {
                var field = new PropertyField(serialized.FindProperty(name));
                field.RegisterValueChangeCallback(_ =>
                {
                    serialized.ApplyModifiedProperties();

                    if (HttpMonitorSettingsEditor.WriteWeaverMirror(settings))
                        banner.style.display = DisplayStyle.Flex;
                });
                weaving.Add(field);
            }

            var defineNote = new Label("The HTTP_MONITOR_DISABLE scripting define turns weaving off for a build target regardless of these settings.");
            defineNote.AddToClassList("hm-settings-note");
            weaving.Add(defineNote);

            // ---- capture: live
            var bodies = Section(root, "Bodies", "Applied immediately to new requests.");

            foreach (var name in new[] { nameof(HttpMonitorSettings.CaptureBodies), nameof(HttpMonitorSettings.MaxBodyKilobytes), nameof(HttpMonitorSettings.MaxTotalBodyMegabytes), nameof(HttpMonitorSettings.BufferUnknownLengthResponses) })
                bodies.Add(LiveField(serialized, settings, name));

            var privacy = Section(root, "Privacy", "Redaction happens at record time; secrets are never stored, exported, or shown.");

            foreach (var name in new[] { nameof(HttpMonitorSettings.RedactedHeaders), nameof(HttpMonitorSettings.RedactedValue) })
                privacy.Add(LiveField(serialized, settings, name));

            var reset = new Button(() =>
            {
                Undo.RecordObject(settings, "Reset HTTP Monitor settings");
                settings.ResetToDefaults();
                EditorUtility.SetDirty(settings);
                serialized.Update();

                if (HttpMonitorSettingsEditor.WriteWeaverMirror(settings))
                    banner.style.display = DisplayStyle.Flex;
            }) { text = "Reset to defaults", tooltip = "Weaving on, release builds not woven, bodies on and capped at 1 MB / 64 MB, the four standard redacted headers" };
            reset.AddToClassList("hm-settings-button");
            root.Add(reset);

            root.Bind(serialized);
        }

        private static PropertyField LiveField(SerializedObject serialized, HttpMonitorSettings settings, string name)
        {
            var field = new PropertyField(serialized.FindProperty(name));
            field.RegisterValueChangeCallback(_ =>
            {
                serialized.ApplyModifiedProperties();
                settings.ApplyTo(HttpMonitorSession.Current.Options);
            });

            return field;
        }

        private static void BuildEditorWindowSection(VisualElement root)
        {
            var store = EditorRecordStore.instance;
            var section = Section(root, "Editor window (this machine)",
                "How much the HTTP Monitor window keeps across domain reloads. Stored per machine, not in the asset.");

            var capacity = new IntegerField("Records kept") { value = store.Buffer.Capacity, tooltip = "Oldest records leave the window past this count" };
            capacity.RegisterValueChangedCallback(e => store.Buffer.Capacity = Mathf.Max(1, e.newValue));
            section.Add(capacity);

            var budget = new IntegerField("Body budget (MB)") { value = (int)(store.Buffer.MaxTotalBodyBytes / (1024 * 1024)), tooltip = "Bodies kept in the window; saved to disk before every reload, so keep it modest" };
            budget.RegisterValueChangedCallback(e => store.Buffer.MaxTotalBodyBytes = Mathf.Max(0, e.newValue) * 1024L * 1024L);
            section.Add(budget);
        }

        private static VisualElement Section(VisualElement root, string title, string description)
        {
            var section = new VisualElement();
            section.AddToClassList("hm-settings-section");

            var heading = new Label(title);
            heading.AddToClassList("hm-settings-heading");
            section.Add(heading);

            if (!string.IsNullOrEmpty(description))
            {
                var text = new Label(description);
                text.AddToClassList("hm-settings-note");
                section.Add(text);
            }

            root.Add(section);

            return section;
        }
    }
}
