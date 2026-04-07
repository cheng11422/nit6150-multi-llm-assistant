using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using MultiLLMProjectAssistant.UI;

namespace MultiLLMProjectAssistant.UI.Views
{
    public partial class RequestBuilderView : UserControl
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

        private sealed class RequestContextStore
        {
            public string? LastProvider { get; set; }
            public RequestContextSnapshot[] Snapshots { get; set; } = Array.Empty<RequestContextSnapshot>();
        }

        private sealed class TemplateItem
        {
            public string Name { get; set; } = "";
        }

        private sealed class FilesStore
        {
            public FileItem[] Files { get; set; } = Array.Empty<FileItem>();
        }

        private sealed class FileItem
        {
            public Guid? ProjectId { get; set; }
            public string Path { get; set; } = "";
            public string Name { get; set; } = "";
            public bool IsAttached { get; set; }
        }

        private sealed class ProjectStore
        {
            public ProjectItem[] Projects { get; set; } = Array.Empty<ProjectItem>();
        }

        private sealed class ProjectItem
        {
            public Guid Id { get; set; }
            public string Name { get; set; } = "";
        }

        private sealed class ProjectMemoryStore
        {
            public ProjectMemoryBucket[] Projects { get; set; } = Array.Empty<ProjectMemoryBucket>();
        }

        private sealed class ProjectMemoryBucket
        {
            public Guid? ProjectId { get; set; }
            public int DefaultTopK { get; set; } = 5;
            public MemoryItem[] Items { get; set; } = Array.Empty<MemoryItem>();
        }

        private sealed class MemoryItem
        {
            public string Title { get; set; } = "";
            public string Content { get; set; } = "";
            public List<string> Tags { get; set; } = new();
            public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        }

        private int _topKValue = 5;
        private readonly string _contextFilePath;
        private readonly string _logFilePath;
        private readonly string _currentProjectPath;
        private readonly string _projectsPath;
        private readonly string _filesPath;
        private readonly string _templatesPath;
        private readonly string _memoryPath;
        private readonly LLMConnector _llmConnector = new();
        private readonly DispatcherTimer _draftSaveTimer;
        private bool _isRestoringContext;
        private bool _isViewReady;
        private string _activeProvider = "";
        private Guid? _currentProjectId;
        private string _currentProjectName = "No project selected";

        public RequestBuilderView()
        {
            InitializeComponent();

            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MultiLLMProjectAssistant");

            _contextFilePath = Path.Combine(dataFolder, "request_context.json");
            _logFilePath = Path.Combine(dataFolder, "requests_log.json");
            _currentProjectPath = Path.Combine(dataFolder, "current_project.json");
            _projectsPath = Path.Combine(dataFolder, "projects.json");
            _filesPath = Path.Combine(dataFolder, "files.json");
            _templatesPath = Path.Combine(dataFolder, "templates.json");
            _memoryPath = Path.Combine(dataFolder, "project_memory.json");
            _draftSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _draftSaveTimer.Tick += DraftSaveTimer_Tick;

            _isViewReady = true;
            LoadCurrentProjectContext();
            LoadTemplateOptions();
            LoadInitialContext();
        }

        private void IncreaseTopK_Click(object sender, RoutedEventArgs e)
        {
            if (_topKValue >= 10) return;
            _topKValue++;
            TopKText.Text = _topKValue.ToString();
            SaveSnapshot(BuildSnapshot());
        }

        private void DecreaseTopK_Click(object sender, RoutedEventArgs e)
        {
            if (_topKValue <= 1) return;
            _topKValue--;
            TopKText.Text = _topKValue.ToString();
            SaveSnapshot(BuildSnapshot());
        }

        private void AttachFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Attach files"
            };

            if (dlg.ShowDialog() != true) return;
            AddAttachments(dlg.FileNames);
            SaveSnapshot(BuildSnapshot());
        }

        private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isViewReady || _isRestoringContext) return;
            SaveSnapshot(BuildSnapshot());
        }

        private void ProjectMemoryCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (!_isViewReady || _isRestoringContext) return;
            SaveSnapshot(BuildSnapshot());
        }

        private void AddAttachments(IEnumerable<string> fileRefs)
        {
            var existing = GetAttachedFilePaths().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var fileRef in fileRefs)
            {
                if (string.IsNullOrWhiteSpace(fileRef)) continue;
                if (existing.Contains(fileRef)) continue;
                AttachmentsPanel.Children.Add(BuildAttachmentPill(fileRef));
                existing.Add(fileRef);
            }
        }

        private Border BuildAttachmentPill(string fileRef)
        {
            var fileName = SafeGetFileName(fileRef);

            var nameText = new TextBlock
            {
                Text = fileName,
                Foreground = System.Windows.Media.Brushes.LightGray,
                FontSize = 11,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 180,
                ToolTip = fileRef
            };

            var removeBtn = new Button
            {
                Content = "x",
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = fileRef,
                Style = (Style)FindResource("IconTextButton")
            };
            removeBtn.Click += RemoveAttachment_Click;

            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            inner.Children.Add(nameText);
            inner.Children.Add(removeBtn);

            return new Border
            {
                Style = (Style)FindResource("AttachmentPill"),
                Tag = fileRef,
                Child = inner
            };
        }

        private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var fileRef = btn.Tag as string;
            if (string.IsNullOrWhiteSpace(fileRef)) return;

            var toRemove = AttachmentsPanel.Children
                .OfType<Border>()
                .FirstOrDefault(b => string.Equals(b.Tag as string, fileRef, StringComparison.OrdinalIgnoreCase));

            if (toRemove != null)
                AttachmentsPanel.Children.Remove(toRemove);

            UnmarkImportedAttachment(fileRef);
            SaveSnapshot(BuildSnapshot());
        }

        private string[] GetAttachedFilePaths()
        {
            return AttachmentsPanel.Children
                .OfType<Border>()
                .Select(b => b.Tag as string)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!)
                .ToArray();
        }

        private void AttachmentsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;
            if (sv.ScrollableWidth <= 0) return;

            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        private async void SubmitRequest_Click(object sender, RoutedEventArgs e)
        {
            var snapshot = BuildSnapshot();
            SaveSnapshot(snapshot);

            RawJsonResponseTextBox.Text = string.Empty;
            NormalizedJsonResponseTextBox.Text = string.Empty;
            SummaryResponseTextBox.Text = "Sending request to provider...";

            var request = new LlmRequest
            {
                Provider = snapshot.Provider ?? "",
                Template = snapshot.Template ?? "",
                TopK = snapshot.TopK,
                MemoryEnabled = snapshot.MemoryEnabled,
                JsonRequest = snapshot.JsonRequest ?? "",
                AttachedFiles = snapshot.AttachedFiles,
                ProjectName = _currentProjectName,
                ProjectMemoryItems = snapshot.MemoryEnabled
                    ? BuildRequestMemorySnippets(snapshot.TopK, snapshot.JsonRequest ?? "")
                    : Array.Empty<string>()
            };

            var response = await _llmConnector.SendRequestAsync(request);

            RawJsonResponseTextBox.Text = response.RawJson;
            NormalizedJsonResponseTextBox.Text = response.NormalizedJson;
            SummaryResponseTextBox.Text = response.Summary;

            var updatedSnapshot = BuildSnapshot();
            SaveSnapshot(updatedSnapshot);
            AppendLogEntry(updatedSnapshot, response);
        }

        private void AppendLogEntry(RequestContextSnapshot snapshot, LlmResponse response)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);

                LogEntry[] existing = Array.Empty<LogEntry>();
                if (File.Exists(_logFilePath))
                {
                    var json = File.ReadAllText(_logFilePath);
                    existing = JsonSerializer.Deserialize<LogEntry[]>(json) ?? Array.Empty<LogEntry>();
                }

                var entry = new LogEntry
                {
                    ProjectId = _currentProjectId,
                    Timestamp = DateTimeOffset.Now,
                    Provider = snapshot.Provider ?? "",
                    Status = response.Status,
                    StatusCode = response.StatusCode,
                    RequestJson = snapshot.JsonRequest ?? "",
                    RawJson = response.RawJson,
                    NormalizedJson = response.NormalizedJson,
                    Summary = response.Summary
                };

                var merged = new LogEntry[] { entry }.Concat(existing).Take(500).ToArray();
                var outJson = JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_logFilePath, outJson);
            }
            catch
            {
                // Keep request flow usable even if log persistence fails.
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            JsonRequestTextBox.Text = string.Empty;
            RawJsonResponseTextBox.Text = string.Empty;
            NormalizedJsonResponseTextBox.Text = string.Empty;
            SummaryResponseTextBox.Text = string.Empty;
            SaveSnapshot(BuildSnapshot());
        }

        private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isViewReady || _isRestoringContext) return;

            var selectedProvider = GetSelectedProvider();
            if (string.IsNullOrWhiteSpace(selectedProvider)) return;

            if (!string.IsNullOrWhiteSpace(_activeProvider) &&
                !string.Equals(_activeProvider, selectedProvider, StringComparison.OrdinalIgnoreCase))
            {
                SaveSnapshot(BuildSnapshot(_activeProvider));
            }

            LoadContextForProvider(selectedProvider);
        }

        private void JsonRequestTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;

            e.Handled = true;
            SubmitRequest_Click(sender, new RoutedEventArgs());
        }

        private void JsonRequestTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isViewReady || _isRestoringContext) return;
            _draftSaveTimer.Stop();
            _draftSaveTimer.Start();
        }

        private void DraftSaveTimer_Tick(object? sender, EventArgs e)
        {
            _draftSaveTimer.Stop();
            if (!_isViewReady || _isRestoringContext) return;
            SaveSnapshot(BuildSnapshot());
        }

        private RequestContextSnapshot BuildSnapshot(string? providerOverride = null)
        {
            var provider = providerOverride ?? GetSelectedProvider();
            var template = GetSelectedTemplate();

            return new RequestContextSnapshot
            {
                Provider = provider,
                Template = template,
                TopK = _topKValue,
                MemoryEnabled = ProjectMemoryCheckBox.IsChecked == true,
                JsonRequest = JsonRequestTextBox.Text,
                RawJsonResponse = RawJsonResponseTextBox.Text,
                NormalizedJsonResponse = NormalizedJsonResponseTextBox.Text,
                SummaryResponse = SummaryResponseTextBox.Text,
                AttachedFiles = GetAttachedFilePaths(),
                UpdatedAt = DateTimeOffset.UtcNow
            };
        }

        private void SaveSnapshot(RequestContextSnapshot snapshot)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_contextFilePath)!);
                var store = LoadContextStore();
                var snapshots = store.Snapshots
                    .Where(s => !string.Equals(s.Provider, snapshot.Provider, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                snapshots.Add(snapshot);

                store.LastProvider = snapshot.Provider;
                store.Snapshots = snapshots
                    .OrderByDescending(s => s.UpdatedAt)
                    .ToArray();

                var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_contextFilePath, json);
            }
            catch
            {
                // UI still works without context persistence.
            }
        }

        private void LoadInitialContext()
        {
            try
            {
                var store = LoadContextStore();
                var provider = store.LastProvider;
                if (string.IsNullOrWhiteSpace(provider))
                    provider = GetSelectedProvider();

                if (!string.IsNullOrWhiteSpace(provider))
                {
                    SelectProvider(provider);
                    LoadContextForProvider(provider);
                }
            }
            catch
            {
                // ignore MVP
            }
        }

        private void LoadContextForProvider(string provider)
        {
            try
            {
                var store = LoadContextStore();
                var snapshot = store.Snapshots
                    .FirstOrDefault(s => string.Equals(s.Provider, provider, StringComparison.OrdinalIgnoreCase));

                _isRestoringContext = true;
                ApplySnapshot(snapshot, provider);
                _activeProvider = provider;
            }
            finally
            {
                _isRestoringContext = false;
            }
        }

        private void ApplySnapshot(RequestContextSnapshot? snapshot, string provider)
        {
            if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.Template))
            {
                EnsureTemplateOption(snapshot.Template);
                SelectTemplate(snapshot.Template);
            }
            else if (TemplateComboBox.Items.Count > 0 && TemplateComboBox.SelectedItem == null)
            {
                TemplateComboBox.SelectedIndex = 0;
            }

            JsonRequestTextBox.Text = snapshot?.JsonRequest ?? string.Empty;

            _topKValue = Math.Clamp(snapshot?.TopK > 0 ? snapshot.TopK : LoadDefaultTopK(), 1, 10);
            TopKText.Text = _topKValue.ToString();
            ProjectMemoryCheckBox.IsChecked = snapshot?.MemoryEnabled ?? true;

            RawJsonResponseTextBox.Text = snapshot?.RawJsonResponse ?? string.Empty;
            NormalizedJsonResponseTextBox.Text = snapshot?.NormalizedJsonResponse ?? string.Empty;
            SummaryResponseTextBox.Text = snapshot?.SummaryResponse ?? string.Empty;

            AttachmentsPanel.Children.Clear();
            var mergedAttachments = (snapshot?.AttachedFiles ?? Array.Empty<string>())
                .Concat(GetImportedAttachedFiles())
                .Distinct(StringComparer.OrdinalIgnoreCase);
            AddAttachments(mergedAttachments);
        }

        private void LoadCurrentProjectContext()
        {
            _currentProjectId = LoadCurrentProjectId();
            _currentProjectName = ResolveCurrentProjectName(_currentProjectId);
            CurrentProjectTextBlock.Text = $"Current project: {_currentProjectName}";
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

        private string ResolveCurrentProjectName(Guid? projectId)
        {
            if (!projectId.HasValue)
                return "No project selected";

            try
            {
                if (File.Exists(_projectsPath))
                {
                    var json = File.ReadAllText(_projectsPath);
                    var store = JsonSerializer.Deserialize<ProjectStore>(json);
                    var project = store?.Projects?.FirstOrDefault(p => p.Id == projectId.Value);
                    if (project != null && !string.IsNullOrWhiteSpace(project.Name))
                        return project.Name;
                }
            }
            catch
            {
            }

            return projectId.Value switch
            {
                var id when id == Guid.Parse("73878590-e89f-4a50-a68b-cc3a4a983103") => "NIT6150 Multi-LLM",
                var id when id == Guid.Parse("d86b6bfc-b9df-4b6c-a978-5ae573d0f196") => "Web Security Audit",
                _ => projectId.Value.ToString()
            };
        }

        private void LoadTemplateOptions()
        {
            var templateNames = new List<string>
            {
                "Code Review",
                "Data Processing",
                "Literature Analysis",
                "Research Summary"
            };

            try
            {
                if (File.Exists(_templatesPath))
                {
                    var json = File.ReadAllText(_templatesPath);
                    var savedTemplates = JsonSerializer.Deserialize<TemplateItem[]>(json) ?? Array.Empty<TemplateItem>();
                    foreach (var name in savedTemplates
                        .Select(t => (t.Name ?? "").Trim())
                        .Where(name => !string.IsNullOrWhiteSpace(name)))
                    {
                        if (!templateNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                            templateNames.Add(name);
                    }
                }
            }
            catch
            {
                // keep default template list
            }

            TemplateComboBox.Items.Clear();
            foreach (var name in templateNames)
                TemplateComboBox.Items.Add(new ComboBoxItem { Content = name });

            if (TemplateComboBox.Items.Count > 0)
                TemplateComboBox.SelectedIndex = 0;
        }

        private IEnumerable<string> GetImportedAttachedFiles()
        {
            try
            {
                if (!File.Exists(_filesPath))
                    return Array.Empty<string>();

                var json = File.ReadAllText(_filesPath);
                var store = JsonSerializer.Deserialize<FilesStore>(json) ?? new FilesStore();
                return store.Files
                    .Where(f => f.ProjectId == _currentProjectId && f.IsAttached)
                    .Select(GetFileReference)
                    .Where(fileRef => !string.IsNullOrWhiteSpace(fileRef))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private void UnmarkImportedAttachment(string fileRef)
        {
            try
            {
                if (!File.Exists(_filesPath)) return;

                var json = File.ReadAllText(_filesPath);
                var store = JsonSerializer.Deserialize<FilesStore>(json) ?? new FilesStore();
                var target = store.Files.FirstOrDefault(f =>
                    f.ProjectId == _currentProjectId &&
                    string.Equals(GetFileReference(f), fileRef, StringComparison.OrdinalIgnoreCase));

                if (target == null) return;
                if (!target.IsAttached) return;

                target.IsAttached = false;
                File.WriteAllText(_filesPath, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Best-effort sync only.
            }
        }

        private static string GetFileReference(FileItem item)
        {
            return !string.IsNullOrWhiteSpace(item.Path) ? item.Path : item.Name;
        }

        private int LoadDefaultTopK()
        {
            try
            {
                if (!File.Exists(_memoryPath))
                    return 5;

                var json = File.ReadAllText(_memoryPath);
                var store = JsonSerializer.Deserialize<ProjectMemoryStore>(json) ?? new ProjectMemoryStore();
                var projectMemory = store.Projects.FirstOrDefault(p => p.ProjectId == _currentProjectId);
                return Math.Clamp(projectMemory?.DefaultTopK ?? 5, 1, 10);
            }
            catch
            {
                return 5;
            }
        }

        private string[] BuildRequestMemorySnippets(int topK, string requestText)
        {
            try
            {
                if (!File.Exists(_memoryPath))
                    return Array.Empty<string>();

                var json = File.ReadAllText(_memoryPath);
                var store = JsonSerializer.Deserialize<ProjectMemoryStore>(json) ?? new ProjectMemoryStore();
                var projectMemory = store.Projects.FirstOrDefault(p => p.ProjectId == _currentProjectId);
                if (projectMemory?.Items == null || projectMemory.Items.Length == 0)
                    return Array.Empty<string>();

                var tokens = TokenizeRequestText(requestText);
                var rankedItems = projectMemory.Items
                    .Select(item => new
                    {
                        Item = item,
                        Score = ScoreMemoryItem(item, tokens)
                    })
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.Item.Timestamp)
                    .ToList();

                var selected = rankedItems
                    .Where(x => tokens.Count == 0 || x.Score > 0)
                    .Take(Math.Max(1, topK))
                    .Select(x => FormatMemorySnippet(x.Item))
                    .ToArray();

                if (selected.Length > 0)
                    return selected;

                return rankedItems
                    .Take(Math.Max(1, topK))
                    .Select(x => FormatMemorySnippet(x.Item))
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static HashSet<string> TokenizeRequestText(string requestText)
        {
            return requestText
                .Split(new[] { ' ', '\r', '\n', '\t', ',', '.', ':', ';', '{', '}', '[', ']', '"', '\'', '(', ')', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(token => token.Trim().ToLowerInvariant())
                .Where(token => token.Length >= 4)
                .Take(24)
                .ToHashSet();
        }

        private static int ScoreMemoryItem(MemoryItem item, HashSet<string> tokens)
        {
            if (tokens.Count == 0) return 0;

            var searchable = new StringBuilder();
            searchable.Append(item.Title);
            searchable.Append(' ');
            searchable.Append(item.Content);
            searchable.Append(' ');
            searchable.Append(string.Join(' ', item.Tags ?? new List<string>()));

            var haystack = searchable.ToString().ToLowerInvariant();
            return tokens.Count(token => haystack.Contains(token, StringComparison.Ordinal));
        }

        private static string FormatMemorySnippet(MemoryItem item)
        {
            var tags = item.Tags != null && item.Tags.Count > 0
                ? $"Tags: {string.Join(", ", item.Tags)}"
                : "Tags: none";

            return $"- {item.Title}\n  {tags}\n  {item.Content}";
        }

        private RequestContextStore LoadContextStore()
        {
            if (!File.Exists(_contextFilePath))
                return new RequestContextStore();

            var json = File.ReadAllText(_contextFilePath);

            try
            {
                return JsonSerializer.Deserialize<RequestContextStore>(json) ?? new RequestContextStore();
            }
            catch (JsonException)
            {
                var snapshot = JsonSerializer.Deserialize<RequestContextSnapshot>(json);
                if (snapshot == null)
                    return new RequestContextStore();

                return new RequestContextStore
                {
                    LastProvider = snapshot.Provider,
                    Snapshots = new[] { snapshot }
                };
            }
        }

        private string GetSelectedProvider()
        {
            return (ProviderComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        }

        private string GetSelectedTemplate()
        {
            return (TemplateComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        }

        private void SelectProvider(string provider)
        {
            foreach (var item in ProviderComboBox.Items)
            {
                if (item is ComboBoxItem cbi && string.Equals(cbi.Content?.ToString(), provider, StringComparison.OrdinalIgnoreCase))
                {
                    _isRestoringContext = true;
                    ProviderComboBox.SelectedItem = cbi;
                    _isRestoringContext = false;
                    return;
                }
            }
        }

        private void SelectTemplate(string template)
        {
            foreach (var item in TemplateComboBox.Items)
            {
                if (item is ComboBoxItem cbi && string.Equals(cbi.Content?.ToString(), template, StringComparison.OrdinalIgnoreCase))
                {
                    TemplateComboBox.SelectedItem = cbi;
                    return;
                }
            }
        }

        private void EnsureTemplateOption(string template)
        {
            if (string.IsNullOrWhiteSpace(template)) return;
            foreach (var item in TemplateComboBox.Items)
            {
                if (item is ComboBoxItem cbi &&
                    string.Equals(cbi.Content?.ToString(), template, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            TemplateComboBox.Items.Add(new ComboBoxItem { Content = template });
        }

        private static string SafeGetFileName(string fileRef)
        {
            try
            {
                var name = Path.GetFileName(fileRef);
                return string.IsNullOrWhiteSpace(name) ? fileRef : name;
            }
            catch
            {
                return fileRef;
            }
        }
    }
}
