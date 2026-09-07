using System;
using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace HttpMonitor.Editor
{
    /// <summary>
    /// The row of filter controls under the toolbar. Binds to a <see cref="RecordQuery"/> and raises
    /// <see cref="Changed"/> whenever the user touches anything; the window re-applies the query.
    /// </summary>
    internal sealed class FilterBar : VisualElement
    {
        private const string AnyMethod = "Any method";

        private readonly RecordQuery _query;
        private readonly ToolbarSearchField _search;
        private readonly ToolbarToggle _errors;
        private readonly ToolbarToggle _automatic;
        private readonly ToolbarToggle _manual;
        private readonly ToolbarToggle _unityWebRequest;
        private readonly ToolbarToggle _httpClient;
        private readonly ToolbarToggle _custom;
        private readonly DropdownField _method;
        private readonly ToolbarButton _hostChip;
        private readonly ToolbarButton _clear;

        public event Action Changed;

        public FilterBar(RecordQuery query)
        {
            _query = query;
            name = "hm-filterbar";
            AddToClassList("hm-filterbar");

            var toolbar = new Toolbar();
            toolbar.AddToClassList("hm-filterbar-toolbar");

            _search = new ToolbarSearchField { name = "hm-search", tooltip = "Filter by URL, method, status, or any header name or value. Ctrl+F focuses, Esc clears." };
            _search.AddToClassList("hm-search");
            _search.RegisterValueChangedCallback(e =>
            {
                _query.Text = e.newValue ?? string.Empty;
                Changed?.Invoke();
            });
            _search.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape)
                {
                    _search.value = string.Empty;
                    e.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            toolbar.Add(_search);

            _errors = Chip(toolbar, "Errors", "Only failed, aborted and 4xx/5xx requests", v => _query.ErrorsOnly = v);
            _errors.AddToClassList("hm-chip--errors");

            toolbar.Add(Separator());
            _automatic = Chip(toolbar, "A", "Show requests captured automatically by weaving", v => _query.ShowAutomatic = v);
            _automatic.AddToClassList("hm-chip--automatic");
            _manual = Chip(toolbar, "M", "Show requests captured through the manual API", v => _query.ShowManual = v);
            _manual.AddToClassList("hm-chip--manual");

            toolbar.Add(Separator());
            _unityWebRequest = Chip(toolbar, "UWR", "Show UnityWebRequest traffic", v => _query.ShowUnityWebRequest = v);
            _httpClient = Chip(toolbar, "HttpClient", "Show System.Net.Http.HttpClient traffic", v => _query.ShowHttpClient = v);
            _custom = Chip(toolbar, "Custom", "Show requests recorded for custom clients", v => _query.ShowCustom = v);

            toolbar.Add(Separator());
            _method = new DropdownField(new List<string> { AnyMethod }, 0) { tooltip = "Only this HTTP method" };
            _method.AddToClassList("hm-method-dropdown");
            _method.RegisterValueChangedCallback(e =>
            {
                _query.Method = e.newValue == AnyMethod ? string.Empty : e.newValue;
                Changed?.Invoke();
            });
            toolbar.Add(_method);

            _hostChip = new ToolbarButton(() => { _query.Host = string.Empty; Changed?.Invoke(); }) { tooltip = "Click to remove the host filter" };
            _hostChip.AddToClassList("hm-host-chip");
            toolbar.Add(_hostChip);

            var spacer = new VisualElement();
            spacer.AddToClassList("hm-toolbar-spacer");
            toolbar.Add(spacer);

            _clear = new ToolbarButton(() => { _query.ClearFilters(); Changed?.Invoke(); }) { text = "Clear filters", tooltip = "Show everything again" };
            toolbar.Add(_clear);

            Add(toolbar);
            SyncFromQuery();
        }

        /// <summary>Puts the keyboard caret in the search box (Ctrl+F).</summary>
        public void FocusSearch()
        {
            _search.Q<TextField>()?.Focus();
        }

        /// <summary>Offers the methods that actually occurred, so the dropdown never lists dead options.</summary>
        public void SetAvailableMethods(IEnumerable<string> methods)
        {
            var choices = new List<string> { AnyMethod };
            choices.AddRange(methods);

            if (!string.IsNullOrEmpty(_query.Method) && !choices.Contains(_query.Method))
                choices.Add(_query.Method);

            _method.choices = choices;
            _method.SetValueWithoutNotify(string.IsNullOrEmpty(_query.Method) ? AnyMethod : _query.Method);
        }

        /// <summary>Refreshes every control from the query (after Clear, host filter from the context menu, ...).</summary>
        public void SyncFromQuery()
        {
            _search.SetValueWithoutNotify(_query.Text);
            _errors.SetValueWithoutNotify(_query.ErrorsOnly);
            _automatic.SetValueWithoutNotify(_query.ShowAutomatic);
            _manual.SetValueWithoutNotify(_query.ShowManual);
            _unityWebRequest.SetValueWithoutNotify(_query.ShowUnityWebRequest);
            _httpClient.SetValueWithoutNotify(_query.ShowHttpClient);
            _custom.SetValueWithoutNotify(_query.ShowCustom);
            _method.SetValueWithoutNotify(string.IsNullOrEmpty(_query.Method) ? AnyMethod : _query.Method);

            var hasHost = !string.IsNullOrEmpty(_query.Host);
            _hostChip.text = hasHost ? "host: " + _query.Host + "  ✕" : string.Empty;
            _hostChip.style.display = hasHost ? DisplayStyle.Flex : DisplayStyle.None;

            _clear.SetEnabled(_query.IsFiltering);
        }

        private ToolbarToggle Chip(VisualElement parent, string text, string tooltip, Action<bool> apply)
        {
            var chip = new ToolbarToggle { text = text, tooltip = tooltip };
            chip.AddToClassList("hm-chip");
            chip.RegisterValueChangedCallback(e =>
            {
                apply(e.newValue);
                Changed?.Invoke();
            });
            parent.Add(chip);

            return chip;
        }

        private static VisualElement Separator()
        {
            var separator = new VisualElement();
            separator.AddToClassList("hm-filter-separator");

            return separator;
        }
    }
}
