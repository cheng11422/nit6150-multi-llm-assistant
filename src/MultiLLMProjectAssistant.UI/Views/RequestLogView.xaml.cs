using System;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace MultiLLMProjectAssistant.UI.Views
{
    public partial class RequestLogView : UserControl
    {
        private sealed class LogEntry
        {
            public Guid Id { get; set; } = Guid.NewGuid();
            public Guid? ProjectId { get; set; }
            public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
            public string Provider { get; set; } = "";
            public string Status { get; set; } = "Success"; // Success/Error/Rate Limit
            public int? StatusCode { get; set; }
            public string RequestJson { get; set; } = "";
            public string RawJson { get; set; } = "";
            public string NormalizedJson { get; set; } = "";
            public string Summary { get; set; } = "";

            // Slightly shorter to keep the row fully readable on smaller widths (no horizontal scroll).
            public string TimestampDisplay => Timestamp.ToString("yyyy-MM-dd HH:mm");
            public string StatusDisplay => StatusCode.HasValue ? $"{Status} ({StatusCode.Value})" : Status;
            public string DetailTitle => string.IsNullOrWhiteSpace(Provider) ? "Trace Detail" : $"Trace Detail: {Provider}";

            public Brush StatusForeground
            {
                get
                {
                    return Status switch
                    {
                        "Success" => (Brush)new BrushConverter().ConvertFromString("#8FDF8F")!,
                        "Rate Limit" => (Brush)new BrushConverter().ConvertFromString("#E6D58A")!,
                        _ => (Brush)new BrushConverter().ConvertFromString("#DF8F8F")!
                    };
                }
            }

            public Brush StatusBackground
            {
                get
                {
                    return Status switch
                    {
                        "Success" => (Brush)new BrushConverter().ConvertFromString("#2E4A2E")!,
                        "Rate Limit" => (Brush)new BrushConverter().ConvertFromString("#4A4630")!,
                        _ => (Brush)new BrushConverter().ConvertFromString("#4A2E2E")!
                    };
                }
            }
        }

        private readonly ObservableCollection<LogEntry> _entries = new();
        private ICollectionView? _view;
        private bool _isInitialized;
        private readonly string _logPath;

        public RequestLogView()
        {
            InitializeComponent();
            _logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MultiLLMProjectAssistant",
                "requests_log.json");

            LoadLog();
            _view = CollectionViewSource.GetDefaultView(_entries);
            _view.Filter = FilterEntry;
            LogListView.ItemsSource = _view;

            _isInitialized = true;
            RefreshFilters();
            Loaded += (_, _) => UpdateColumnWidths();

            if (LogListView.Items.Count > 0)
                LogListView.SelectedIndex = 0;
        }

        private void LogListView_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateColumnWidths();

        private void UpdateColumnWidths()
        {
            if (LogListView.View is not GridView gv) return;
            if (TimestampColumn == null || ProviderColumn == null || StatusColumn == null) return;

            // Available width minus a little breathing room for borders/padding/scrollbar.
            var w = Math.Max(0, LogListView.ActualWidth - 36);
            if (w <= 0) return;

            // Responsive proportions with minimums.
            // Priority: keep Status wide enough to show "Rate Limit (429)" fully.
            var status = Math.Max(270, w * 0.40);
            var provider = Math.Max(110, w * 0.18);
            var ts = Math.Max(150, w - status - provider);

            TimestampColumn.Width = ts;
            ProviderColumn.Width = provider;
            StatusColumn.Width = status;
        }

        private void LoadLog()
        {
            try
            {
                if (!File.Exists(_logPath))
                {
                    SeedDemo();
                    SaveLog();
                    return;
                }

                var json = File.ReadAllText(_logPath);
                var items = JsonSerializer.Deserialize<LogEntry[]>(json);
                _entries.Clear();
                if (items != null)
                    foreach (var i in items.OrderByDescending(x => x.Timestamp))
                        _entries.Add(i);
            }
            catch
            {
                SeedDemo();
            }
        }

        private void SaveLog()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_logPath, json);
            }
            catch
            {
                // ignore MVP
            }
        }

        private void SeedDemo()
        {
            _entries.Clear();
            _entries.Add(new LogEntry
            {
                Timestamp = DateTimeOffset.Now.AddMinutes(-30),
                Provider = "OpenAI-GPT4",
                Status = "Success",
                StatusCode = 200,
                RequestJson = "{\n  \"task\": \"Code Review\"\n}",
                RawJson = "{\n  \"id\": \"demo\",\n  \"choices\": []\n}",
                NormalizedJson = "{\n  \"provider\": \"OpenAI-GPT4\",\n  \"ok\": true\n}",
                Summary = "Demo success entry."
            });
            _entries.Add(new LogEntry
            {
                Timestamp = DateTimeOffset.Now.AddHours(-3),
                Provider = "Gemini-Pro",
                Status = "Rate Limit",
                StatusCode = 429,
                RequestJson = "{\n  \"task\": \"Literature Analysis\"\n}",
                RawJson = "{\n  \"error\": \"rate limited\"\n}",
                NormalizedJson = "{\n  \"provider\": \"Gemini-Pro\",\n  \"ok\": false,\n  \"reason\": \"rate_limit\"\n}",
                Summary = "Demo rate limit entry."
            });
        }

        private bool FilterEntry(object obj)
        {
            if (obj is not LogEntry e) return false;

            var q = (LogSearchTextBox.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(q))
            {
                if (!(e.Provider.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                      e.StatusDisplay.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                      e.RequestJson.Contains(q, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }

            var provider = (ProviderFilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All providers";
            if (provider != "All providers" && !string.Equals(e.Provider, provider, StringComparison.OrdinalIgnoreCase))
                return false;

            var status = (StatusFilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All status";
            if (status != "All status" && !string.Equals(e.Status, status, StringComparison.OrdinalIgnoreCase))
                return false;

            var date = (DateFilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All time";
            var now = DateTimeOffset.Now;
            if (date == "Today" && e.Timestamp.Date != now.Date) return false;
            if (date == "Last 7 days" && e.Timestamp < now.AddDays(-7)) return false;

            return true;
        }

        private void RefreshFilters()
        {
            if (!_isInitialized || _view == null) return;
            _view.Refresh();
        }

        private void LogFilters_Changed(object sender, EventArgs e) => RefreshFilters();

        private void LogListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LogListView.SelectedItem is not LogEntry entry)
            {
                RequestJsonTextBox.Text = "";
                RawJsonTextBox.Text = "";
                NormalizedJsonTextBox.Text = "";
                SummaryTextBox.Text = "";
                return;
            }

            RequestJsonTextBox.Text = entry.RequestJson ?? "";
            RawJsonTextBox.Text = entry.RawJson ?? "";
            NormalizedJsonTextBox.Text = entry.NormalizedJson ?? "";
            SummaryTextBox.Text = entry.Summary ?? "";
        }
    }
}
