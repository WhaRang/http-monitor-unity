using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// The monospace font for bodies and the composer's body editor. Unity ships Roboto Mono in
    /// its editor resources (the Console uses it); OS fonts are the fallback, and null means
    /// "leave the default", which every caller tolerates.
    /// </summary>
    internal static class EditorFonts
    {
        private const string EditorResourcePath = "Fonts/RobotoMono/RobotoMono-Regular.ttf";
        private static readonly string[] OsFallbacks = { "Consolas", "Menlo", "DejaVu Sans Mono", "Liberation Mono", "Courier New" };

        private static Font _monospace;
        private static bool _resolved;

        public static Font Monospace
        {
            get
            {
                if (_resolved)
                    return _monospace;

                _resolved = true;

                try
                {
                    _monospace = EditorGUIUtility.Load(EditorResourcePath) as Font;

                    if (_monospace == null)
                        _monospace = Font.CreateDynamicFontFromOSFont(OsFallbacks, 12);
                }
                catch
                {
                    _monospace = null;
                }

                return _monospace;
            }
        }

        /// <summary>Applies the monospace font to an element and, through inheritance, its text children.</summary>
        public static void ApplyMonospace(VisualElement element)
        {
            var font = Monospace;

            if (font != null && element != null)
                element.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
        }
    }
}
