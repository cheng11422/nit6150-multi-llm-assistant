using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MultiLLMProjectAssistant.UI;

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
            public string Status { get; set; } = "Success";
            public int? StatusCode { get; set; }
            public string RequestJson { get; set; } = "";
            public string RawJson { get; set; } = "";
            public string NormalizedJson { get; set; } = "";
            public string Summary { get; set; } = "";
            public string ProjectName { get; set; } = "";

            public string TimestampDisplay => Timestamp.ToString("yyyy-MM-dd HH:mm");
            public string StatusDisplay => StatusCode.HasValue ? $"{Status} ({StatusCode.Value})" : Status;
            public string DetailTitle => string.IsNullOrWhiteSpace(Provider) ? "Trace Detail" : $"Trace Detail: {Provider}";
            public string ProjectDisplay => string.IsNullOrWhiteSpace(ProjectName) ? "(No project)" : ProjectName;

            public Brush StatusForeground => Status switch
            {
                "Success" => (Brush)new BrushConverter().ConvertFromString("#8FDF8F")!,
                "Rate Limit" => (Brush)new BrushConverter().ConvertFromString("#E6D58A")!,
                _ => (Brush)new BrushConverter().ConvertFromString("#DF8F8F")!
            };

            public Brush StatusBackground => Status switch
            {
                "Success" => (Brush)new BrushConverter().ConvertFromString("#2E4A2E")!,
                "Rate Limit" => (Brush)new BrushConverter().ConvertFromString("#4A4630")!,
                _ => (Brush)new BrushConverter().ConvertFromString("#4A2E2E")!
            };
        }

        private sealed class ProjectStore
        {
            public Guid? CurrentProjectId { get; set; }
            public ProjectItem[] Projects { get; set; } = Array.Empty<ProjectItem>();
        }

        private sealed class ProjectItem
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
        }

        private sealed class ProjectFilterOption
        {
            public Guid? ProjectId { get; set; }
            public string Label { get; set; } = "";
        }

        private sealed class RequestContextStore
        {
            public string? LastProvider { get; set; }
            public RequestContextSnapshot[] Snapshots { get; set; } = Array.Empty<RequestContextSnapshot>();
        }

        private sealed class RequestContextSnapshot
        {
            public string? Provider { get; set; }
            public string? Template { get; set; }
            public int TopK { get; set; }
            public bool MemoryEnabled { get; set; }
            public string? JsonRequest { get; set; }
            public string? RawJsonResponse { get; set; }
            public string? NormalizedJsonResponse { get; set; }
            public string? SummaryResponse { get; set; }
            public string[] AttachedFiles { get; set; } = Array.Empty<string>();
            public DateTimeOffset UpdatedAt { get; set; }
        }

        private readonly ObservableCollection<LogEntry> _entries = new();
        private readonly ObservableCollection<ProjectFilterOption> _projectFilters = new();
        private ICollectionView? _view;
        private bool _isInitialized;
        private readonly string _logPath;
        private readonly string _projectsPath;
        private readonly string _currentProjectPath;
        private readonly string _contextPath;
        private readonly Guid? _requestedProjectId;

        public RequestLogView(Guid? projectId = null)
        {
            InitializeComponent();
            _requestedProjectId = projectId;
            _logPath = AppDataPaths.GetDataFile("requests_log.json");
            _projectsPath = AppDataPaths.GetDataFile("projects.json");
            _currentProjectPath = AppDataPaths.GetDataFile("current_project.json");
            _contextPath = AppDataPaths.GetDataFile("request_context.json");

            LoadLog();
            BuildProjectFilters();

            _view = CollectionViewSource.GetDefaultView(_entries);
            _view.Filter = FilterEntry;
            LogListView.ItemsSource = _view;
            ProjectFilterComboBox.ItemsSource = _projectFilters;
            ProjectFilterComboBox.DisplayMemberPath = nameof(ProjectFilterOption.Label);

            _isInitialized = true;
            SelectInitialProjectFilter();
            RefreshFilters();
            Loaded += (_, _) => UpdateColumnWidths();

            if (LogListView.Items.Count > 0)
                LogListView.SelectedIndex = 0;

            UpdateActionButtons();
        }

        private void LogListView_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateColumnWidths();

        private void UpdateColumnWidths()
        {
            if (LogListView.View is not GridView) return;
            if (TimestampColumn == null || ProviderColumn == null || StatusColumn == null || ProjectColumn == null) return;

            var w = Math.Max(0, LogListView.ActualWidth - 36);
            if (w <= 0) return;

            var project = Math.Max(150, w * 0.24);
            var provider = Math.Max(110, w * 0.16);
            var status = Math.Max(220, w * 0.28);
            var ts = Math.Max(150, w - project - provider - status);

            TimestampColumn.Width = ts;
            ProjectColumn.Width = project;
            ProviderColumn.Width = provider;
            StatusColumn.Width = status;
        }

        private void LoadLog()
        {
            try
            {
                if (!File.Exists(_logPath))
                {
                    _entries.Clear();
                    return;
                }

                var projectMap = LoadProjectMap();
                var json = File.ReadAllText(_logPath);
                var items = JsonSerializer.Deserialize<LogEntry[]>(json) ?? Array.Empty<LogEntry>();

                _entries.Clear();
                foreach (var item in items.OrderByDescending(x => x.Timestamp))
                {
                    item.ProjectName = ResolveProjectName(projectMap, item.ProjectId);
                    _entries.Add(item);
                }
            }
            catch
            {
                _entries.Clear();
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
                // ignore for now
            }
        }

        private Dictionary<Guid, string> LoadProjectMap()
        {
            try
            {
                if (File.Exists(_projectsPath))
                {
                    var json = File.ReadAllText(_projectsPath);
                    var store = JsonSerializer.Deserialize<ProjectStore>(json);
                    if (store?.Projects != null)
                    {
                        return store.Projects
                            .Where(p => p.Id != Guid.Empty && !string.IsNullOrWhiteSpace(p.Name))
                            .GroupBy(p => p.Id)
                            .ToDictionary(g => g.Key, g => g.Last().Name);
                    }
                }
            }
            catch
            {
            }

            return new Dictionary<Guid, string>();
        }

        private string ResolveProjectName(Dictionary<Guid, string> projectMap, Guid? projectId)
        {
            if (!projectId.HasValue)
                return "";

            if (projectMap.TryGetValue(projectId.Value, out var name))
                return name;

            return projectId.Value switch
            {
                var id when id == Guid.Parse("73878590-e89f-4a50-a68b-cc3a4a983103") => "NIT6150 Multi-LLM",
                var id when id == Guid.Parse("d86b6bfc-b9df-4b6c-a978-5ae573d0f196") => "Web Security Audit",
                _ => projectId.Value.ToString()
            };
        }

        private void BuildProjectFilters()
        {
            _projectFilters.Clear();
            _projectFilters.Add(new ProjectFilterOption { ProjectId = null, Label = "All projects" });

            foreach (var group in _entries
                .Where(e => e.ProjectId.HasValue)
                .GroupBy(e => e.ProjectId!.Value)
                .OrderBy(g => g.First().ProjectName))
            {
                _projectFilters.Add(new ProjectFilterOption
                {
                    ProjectId = group.Key,
                    Label = group.First().ProjectDisplay
                });
            }
        }

        private void SelectInitialProjectFilter()
        {
            var targetProjectId = _requestedProjectId ?? LoadCurrentProjectId();
            if (targetProjectId.HasValue)
            {
                var match = _projectFilters.FirstOrDefault(p => p.ProjectId == targetProjectId.Value);
                if (match != null)
                {
                    ProjectFilterComboBox.SelectedItem = match;
                    return;
                }
            }

            ProjectFilterComboBox.SelectedIndex = 0;
        }

        private Guid? LoadCurrentProjectId()
        {
            try
            {
                if (!File.Exists(_currentProjectPath)) return null;
                var json = File.ReadAllText(_currentProjectPath);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("currentProjectId", out var idElement))
                    return null;

                return idElement.ValueKind == JsonValueKind.String &&
                       Guid.TryParse(idElement.GetString(), out var parsed)
                    ? parsed
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private bool FilterEntry(object obj)
        {
            if (obj is not LogEntry entry) return false;

            var q = (LogSearchTextBox.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(q))
            {
                if (!(entry.Provider.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                      entry.StatusDisplay.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                      entry.RequestJson.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                      entry.ProjectDisplay.Contains(q, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }

            if (ProjectFilterComboBox.SelectedItem is ProjectFilterOption projectFilter &&
                projectFilter.ProjectId.HasValue &&
                entry.ProjectId != projectFilter.ProjectId)
            {
                return false;
            }

            var provider = (ProviderFilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All providers";
            if (provider != "All providers" && !string.Equals(entry.Provider, provider, StringComparison.OrdinalIgnoreCase))
                return false;

            var status = (StatusFilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All status";
            if (status != "All status" && !string.Equals(entry.Status, status, StringComparison.OrdinalIgnoreCase))
                return false;

            var date = (DateFilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All time";
            var now = DateTimeOffset.Now;
            if (date == "Today" && entry.Timestamp.Date != now.Date) return false;
            if (date == "Last 7 days" && entry.Timestamp < now.AddDays(-7)) return false;

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
                UpdateActionButtons();
                return;
            }

            RequestJsonTextBox.Text = entry.RequestJson ?? "";
            RawJsonTextBox.Text = entry.RawJson ?? "";
            NormalizedJsonTextBox.Text = entry.NormalizedJson ?? "";
            SummaryTextBox.Text = entry.Summary ?? "";
            UpdateActionButtons();
        }

        private void UpdateActionButtons()
        {
            var hasSelection = LogListView.SelectedItem is LogEntry;
            LoadRecordButton.IsEnabled = hasSelection;
            DeleteRecordButton.IsEnabled = hasSelection;
        }

        private void LoadRecord_Click(object sender, RoutedEventArgs e)
        {
            if (LogListView.SelectedItem is not LogEntry entry) return;

            var snapshot = BuildSnapshotFromEntry(entry);
            SaveSnapshot(snapshot);

            if (entry.ProjectId.HasValue)
                PersistCurrentProject(entry.ProjectId.Value);

            if (Window.GetWindow(this) is MainWindow mainWindow)
                mainWindow.NavigateToRequestBuilder();
        }

        private void DeleteRecord_Click(object sender, RoutedEventArgs e)
        {
            if (LogListView.SelectedItem is not LogEntry entry) return;

            _entries.Remove(entry);
            SaveLog();
            BuildProjectFilters();
            ProjectFilterComboBox.ItemsSource = _projectFilters;
            SelectInitialProjectFilter();
            RefreshFilters();

            if (LogListView.Items.Count > 0)
                LogListView.SelectedIndex = 0;
            else
                UpdateActionButtons();
        }

        private RequestContextSnapshot BuildSnapshotFromEntry(LogEntry entry)
        {
            string? template = null;
            int topK = 5;
            bool memoryEnabled = true;
            var attachedFiles = Array.Empty<string>();

            try
            {
                if (!string.IsNullOrWhiteSpace(entry.NormalizedJson))
                {
                    using var doc = JsonDocument.Parse(entry.NormalizedJson);
                    if (doc.RootElement.TryGetProperty("template", out var templateElement) && templateElement.ValueKind == JsonValueKind.String)
                        template = templateElement.GetString();
                    if (doc.RootElement.TryGetProperty("topK", out var topKElement) && topKElement.TryGetInt32(out var parsedTopK))
                        topK = parsedTopK;
                    if (doc.RootElement.TryGetProperty("memoryEnabled", out var memoryElement) &&
                        (memoryElement.ValueKind == JsonValueKind.True || memoryElement.ValueKind == JsonValueKind.False))
                        memoryEnabled = memoryElement.GetBoolean();
                    if (doc.RootElement.TryGetProperty("attachedFiles", out var attachedElement) && attachedElement.ValueKind == JsonValueKind.Array)
                    {
                        attachedFiles = attachedElement.EnumerateArray()
                            .Where(x => x.ValueKind == JsonValueKind.String)
                            .Select(x => x.GetString() ?? "")
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .ToArray();
                    }
                }
            }
            catch
            {
            }

            return new RequestContextSnapshot
            {
                Provider = entry.Provider,
                Template = template ?? "Code Review",
                TopK = topK,
                MemoryEnabled = memoryEnabled,
                JsonRequest = entry.RequestJson,
                RawJsonResponse = entry.RawJson,
                NormalizedJsonResponse = entry.NormalizedJson,
                SummaryResponse = entry.Summary,
                AttachedFiles = attachedFiles,
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }

        private void SaveSnapshot(RequestContextSnapshot snapshot)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_contextPath)!);
                var store = LoadContextStore();
                var snapshots = store.Snapshots
                    .Where(s => !string.Equals(s.Provider, snapshot.Provider, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                snapshots.Add(snapshot);
                store.LastProvider = snapshot.Provider;
                store.Snapshots = snapshots.OrderByDescending(s => s.UpdatedAt).ToArray();
                File.WriteAllText(_contextPath, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
            }
        }

        private RequestContextStore LoadContextStore()
        {
            try
            {
                if (!File.Exists(_contextPath))
                    return new RequestContextStore();

                var json = File.ReadAllText(_contextPath);
                return JsonSerializer.Deserialize<RequestContextStore>(json) ?? new RequestContextStore();
            }
            catch
            {
                return new RequestContextStore();
            }
        }

        private void PersistCurrentProject(Guid id)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_currentProjectPath)!);
                File.WriteAllText(_currentProjectPath, JsonSerializer.Serialize(new { currentProjectId = id }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
            }
        }
    }
}
