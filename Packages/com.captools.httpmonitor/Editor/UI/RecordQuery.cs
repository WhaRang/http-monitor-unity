using System;
using System.Collections.Generic;

namespace HttpMonitor.Editor
{
    /// <summary>Which column the list is sorted by. <see cref="Arrival"/> is the natural, unsorted order.</summary>
    internal enum SortColumn
    {
        Arrival,
        Method,
        Status,
        Name,
        Host,
        Type,
        Size,
        Time,
        Started,
        Source,
    }

    /// <summary>
    /// The filter and sort state of the window, and the pure function that applies it to a record
    /// list. No UI types, so it is unit-testable; the window binds controls to it and calls
    /// <see cref="Apply"/> whenever either side changes.
    /// </summary>
    internal sealed class RecordQuery
    {
        public string Text = string.Empty;
        public bool ErrorsOnly;
        public bool ShowAutomatic = true;
        public bool ShowManual = true;
        public bool ShowUnityWebRequest = true;
        public bool ShowHttpClient = true;
        public bool ShowCustom = true;
        public string Method = string.Empty;
        public string Host = string.Empty;

        public SortColumn SortBy = SortColumn.Arrival;
        public bool SortDescending;

        public bool IsFiltering =>
            !string.IsNullOrEmpty(Text) || ErrorsOnly || !ShowAutomatic || !ShowManual
            || !ShowUnityWebRequest || !ShowHttpClient || !ShowCustom
            || !string.IsNullOrEmpty(Method) || !string.IsNullOrEmpty(Host);

        public void ClearFilters()
        {
            Text = string.Empty;
            ErrorsOnly = false;
            ShowAutomatic = ShowManual = true;
            ShowUnityWebRequest = ShowHttpClient = ShowCustom = true;
            Method = string.Empty;
            Host = string.Empty;
        }

        /// <summary>A short description of the active filters for the status bar; empty when none.</summary>
        public string Describe()
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(Text)) parts.Add($"\"{Text}\"");
            if (ErrorsOnly) parts.Add("errors");
            if (ShowAutomatic != ShowManual) parts.Add(ShowAutomatic ? "automatic" : "manual");
            if (!string.IsNullOrEmpty(Method)) parts.Add(Method);
            if (!string.IsNullOrEmpty(Host)) parts.Add("host " + Host);

            var clients = new List<string>();
            if (ShowUnityWebRequest) clients.Add("UWR");
            if (ShowHttpClient) clients.Add("HttpClient");
            if (ShowCustom) clients.Add("custom");
            if (clients.Count < 3) parts.Add(string.Join("+", clients));

            return string.Join(", ", parts);
        }

        /// <summary>Filter and sort, for the table.</summary>
        public List<EditorRecord> Apply(IReadOnlyList<EditorRecord> records)
        {
            var result = Filter(records);

            if (SortBy != SortColumn.Arrival)
                Sort(result);

            return result;
        }

        /// <summary>Filter only, in arrival order, for views that must not follow the table's sort (the timeline).</summary>
        public List<EditorRecord> Filter(IReadOnlyList<EditorRecord> records)
        {
            var result = new List<EditorRecord>(records.Count);

            foreach (var record in records)
            {
                if (Matches(record))
                    result.Add(record);
            }

            return result;
        }

        public bool Matches(EditorRecord record)
        {
            if (ErrorsOnly && !(record.IsFinished && record.IsError))
                return false;

            var automatic = (record.Source & HttpCaptureSource.Woven) != 0;
            var manual = (record.Source & HttpCaptureSource.Manual) != 0;

            if (!(automatic && ShowAutomatic) && !(manual && ShowManual))
                return false;

            switch (record.Client)
            {
                case HttpClientKind.UnityWebRequest when !ShowUnityWebRequest:
                case HttpClientKind.HttpClient when !ShowHttpClient:
                case HttpClientKind.Custom when !ShowCustom:
                    return false;
            }

            if (!string.IsNullOrEmpty(Method) && !string.Equals(record.Method, Method, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrEmpty(Host) && !string.Equals(RecordFormat.Host(record.Url), Host, StringComparison.OrdinalIgnoreCase))
                return false;

            return string.IsNullOrEmpty(Text) || MatchesText(record, Text);
        }

        /// <summary>Case-insensitive substring over URL, method, status code, state, and header names and values.</summary>
        private static bool MatchesText(EditorRecord record, string text)
        {
            if (Contains(record.Url, text) || Contains(record.Method, text))
                return true;

            if (record.StatusCode != 0 && record.StatusCode.ToString().StartsWith(text, StringComparison.Ordinal))
                return true;

            if (Contains(RecordFormat.StatusText(record), text) || Contains(record.State.ToString(), text))
                return true;

            return ContainsHeader(record.RequestHeaders, text) || ContainsHeader(record.ResponseHeaders, text);
        }

        private static bool ContainsHeader(EditorHeader[] headers, string text)
        {
            if (headers == null)
                return false;

            foreach (var header in headers)
            {
                if (Contains(header.Name, text) || Contains(header.Value, text))
                    return true;
            }

            return false;
        }

        private static bool Contains(string haystack, string needle)
        {
            return haystack != null && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Sort(List<EditorRecord> records)
        {
            var comparison = Comparison(SortBy);
            var descending = SortDescending;

            // Stable: arrival order (ascending id) always breaks ties, whichever direction is chosen.
            records.Sort((a, b) =>
            {
                var c = comparison(a, b);

                if (descending)
                    c = -c;

                return c != 0 ? c : a.Id.CompareTo(b.Id);
            });
        }

        private static Comparison<EditorRecord> Comparison(SortColumn column)
        {
            switch (column)
            {
                case SortColumn.Method: return (a, b) => string.Compare(a.Method, b.Method, StringComparison.OrdinalIgnoreCase);
                case SortColumn.Status: return (a, b) => StatusRank(a).CompareTo(StatusRank(b));
                case SortColumn.Name: return (a, b) => string.Compare(RecordFormat.Name(a.Url), RecordFormat.Name(b.Url), StringComparison.OrdinalIgnoreCase);
                case SortColumn.Host: return (a, b) => string.Compare(RecordFormat.Host(a.Url), RecordFormat.Host(b.Url), StringComparison.OrdinalIgnoreCase);
                case SortColumn.Type: return (a, b) => string.Compare(RecordFormat.ShortType(a), RecordFormat.ShortType(b), StringComparison.OrdinalIgnoreCase);
                case SortColumn.Size: return (a, b) => a.DownloadedBytes.CompareTo(b.DownloadedBytes);
                case SortColumn.Time: return (a, b) => a.DurationMs.CompareTo(b.DurationMs);
                case SortColumn.Started: return (a, b) => a.StartedAtUtcTicks.CompareTo(b.StartedAtUtcTicks);
                case SortColumn.Source: return (a, b) => ((int)a.Source).CompareTo((int)b.Source);
                default: return (a, b) => 0;
            }
        }

        /// <summary>Pending first, then status codes, then transport failures, so "sort by status" groups sensibly.</summary>
        private static long StatusRank(EditorRecord record)
        {
            switch (record.State)
            {
                case HttpRecordState.Pending: return 0;
                case HttpRecordState.Completed: return record.StatusCode;
                case HttpRecordState.Aborted: return 1000;
                case HttpRecordState.Incomplete: return 1001;
                default: return 1002; // Failed
            }
        }
    }
}
