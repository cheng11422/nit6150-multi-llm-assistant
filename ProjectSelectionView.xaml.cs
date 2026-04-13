using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MultiLLMProjectAssistant.UI;

namespace MultiLLMProjectAssistant.UI.Views
{
    public partial class ProjectSelectionView : UserControl
    {
        private static readonly Guid DemoNit6150ProjectId = Guid.Parse("73878590-e89f-4a50-a68b-cc3a4a983103");
        private static readonly Guid DemoWebSecurityProjectId = Guid.Parse("d86b6bfc-b9df-4b6c-a978-5ae573d0f196");

        private sealed class LogEntry
        {
            public Guid Id { get; set; }
            public Guid? ProjectId { get; set; }
            public DateTimeOffset Timestamp { get; set; }
            public string Provider { get; set; } = "";
            public string Status { get; set; } = "Success";
            public int? StatusCode { get; set; }
        }

        private sealed class SettingsModel
        {
            public ApiKeyItem[] ApiKeys { get; set; } = Array.Empty<ApiKeyItem>();
        }

        private sealed class ApiKeyItem
        {
            public string Provider { get; set; } = "";
            public string EncryptedValue { get; set; } = "";
        }

        private sealed class FileItem
        {
            public Guid? ProjectId { get; set; }
        }

        private sealed class FilesStore
        {
            public FileItem[] Files { get; set; } = Array.Empty<FileItem>();
        }

        private sealed class MemoryItem
        {
            public Guid Id { get; set; }
        }

        private sealed class ProjectMemoryBucket
        {
            public Guid? ProjectId { get; set; }
            public int DefaultTopK { get; set; } = 5;
            public MemoryItem[] Items { get; set; } = Array.Empty<MemoryItem>();
        }

        private sealed class ProjectMemoryStore
        {
            public ProjectMemoryBucket[] Projects { get; set; } = Array.Empty<ProjectMemoryBucket>();
        }

        private sealed class ProjectItem : INotifyPropertyChanged
        {
            private string _name = "";
            private string _description = "";
            private string _status = "Active";
            private DateTimeOffset _updatedAt = DateTimeOffset.Now;
            private int _requests;
            private string _health = "Healthy";

            public Guid Id { get; set; } = Guid.NewGuid();

            public string Name
            {
                get => _name;
                set { _name = value; OnChanged(nameof(Name)); }
            }

            public string Description
            {
                get => _description;
                set { _description = value; OnChanged(nameof(Description)); }
            }

            public string Status
            {
                get => _status;
                set
                {
                    _status = value;
                    OnChanged(nameof(Status));
                    OnChanged(nameof(StatusActionLabel));
                }
            }

            public DateTimeOffset UpdatedAt
            {
                get => _updatedAt;
                set
                {
                    _updatedAt = value;
                    OnChanged(nameof(UpdatedAt));
                    OnChanged(nameof(MetaLine));
                }
            }

            public int RequestsCount
            {
                get => _requests;
                set
                {
                    _requests = value;
                    OnChanged(nameof(RequestsCount));
                    OnChanged(nameof(MetaLine));
                }
            }

            public string Health
            {
                get => _health;
                set
                {
                    _health = value;
                    OnChanged(nameof(Health));
                    OnChanged(nameof(HealthLine));
                    OnChanged(nameof(HealthColor));
                }
            }

            [JsonIgnore]
            public string MetaLine => $"Last modified: {UpdatedAt:yyyy-MM-dd} - Requests: {RequestsCount}";

            [JsonIgnore]
            public string HealthLine => $"Status: {Health}";

            [JsonIgnore]
            public string StatusActionLabel => string.Equals(Status, "Paused", StringComparison.OrdinalIgnoreCase)
                ? "Set Active"
                : "Pause Project";

            [JsonIgnore]
            public Brush HealthColor => Health switch
            {
                "Healthy" => (Brush)new BrushConverter().ConvertFromString("#8FDF8F")!,
                "Needs attention" => (Brush)new BrushConverter().ConvertFromString("#DF8F8F")!,
                _ => (Brush)new BrushConverter().ConvertFromString("#A0A0A0")!
            };

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private sealed class ProjectStore
        {
            public Guid? CurrentProjectId { get; set; }
            public ProjectItem[] Projects { get; set; } = Array.Empty<ProjectItem>();
        }

        private sealed class ProjectStats
        {
            public int RequestsCount { get; set; }
            public DateTimeOffset? LastRequestAt { get; set; }
            public string LastProvider { get; set; } = "";
            public int RecentFailureCount { get; set; }
            public int RecentRateLimitCount { get; set; }
        }

        private readonly ObservableCollection<ProjectItem> _projects = new();
        private readonly ICollectionView _projectsView;
        private readonly string _projectsPath;
        private readonly string _currentPath;
        private readonly string _logPath;
        private readonly string _settingsPath;
        private readonly string _filesPath;
        private readonly string _memoryPath;

        public ProjectSelectionView()
        {
            InitializeComponent();
            _projectsPath = AppDataPaths.GetDataFile("projects.json");
            _currentPath = AppDataPaths.GetDataFile("current_project.json");
            _logPath = AppDataPaths.GetDataFile("requests_log.json");
            _settingsPath = AppDataPaths.GetDataFile("settings.json");
            _filesPath = AppDataPaths.GetDataFile("files.json");
            _memoryPath = AppDataPaths.GetDataFile("project_memory.json");

            _projectsView = CollectionViewSource.GetDefaultView(_projects);
            _projectsView.Filter = FilterProject;
            ProjectsListBox.ItemsSource = _projectsView;

            LoadProjects();
        }

        private void LoadProjects()
        {
            Guid? selectedProjectId = null;

            try
            {
                if (File.Exists(_projectsPath))
                {
                    var json = File.ReadAllText(_projectsPath);
                    var store = JsonSerializer.Deserialize<ProjectStore>(json) ?? new ProjectStore();

                    _projects.Clear();
                    foreach (var project in store.Projects.OrderByDescending(x => x.UpdatedAt))
                        _projects.Add(project);

                    selectedProjectId = store.CurrentProjectId;
                }
            }
            catch
            {
                _projects.Clear();
            }

            if (!_projects.Any())
            {
                SeedDemo();
                SaveProjects();
            }

            RefreshProjectStatsAndUi();

            if (selectedProjectId.HasValue)
            {
                var selected = _projects.FirstOrDefault(p => p.Id == selectedProjectId.Value);
                if (selected != null)
                    ProjectsListBox.SelectedItem = selected;
            }

            _projectsView.Refresh();
        }

        private void SaveProjects(Guid? currentId = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_projectsPath)!);
            var store = new ProjectStore
            {
                CurrentProjectId = currentId ?? (ProjectsListBox.SelectedItem as ProjectItem)?.Id,
                Projects = _projects.ToArray()
            };

            File.WriteAllText(_projectsPath, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
        }

        private void SeedDemo()
        {
            _projects.Clear();
            _projects.Add(new ProjectItem
            {
                Id = DemoNit6150ProjectId,
                Name = "NIT6150 Multi-LLM",
                Description = "Desktop app build with WPF. Multi-provider request workflows and project memory.",
                Status = "Active",
                Health = "Healthy",
                UpdatedAt = DateTimeOffset.Now,
                RequestsCount = 0
            });
            _projects.Add(new ProjectItem
            {
                Id = DemoWebSecurityProjectId,
                Name = "Web Security Audit",
                Description = "Pen-test notes, findings, and remediation plan with traceable JSON logs.",
                Status = "Paused",
                Health = "Needs attention",
                UpdatedAt = DateTimeOffset.Now.AddDays(-2),
                RequestsCount = 0
            });
        }

        private void CreateNewProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CreateProjectWindow
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() != true) return;

            var name = dialog.ProjectName;
            if (string.IsNullOrWhiteSpace(name)) return;

            var project = new ProjectItem
            {
                Name = name,
                Description = dialog.ProjectDescription,
                Status = "Active",
                Health = "Healthy",
                UpdatedAt = DateTimeOffset.Now,
                RequestsCount = 0
            };

            _projects.Insert(0, project);
            ProjectsListBox.SelectedItem = project;
            PersistCurrentProject(project.Id);
            SaveProjects(project.Id);
            RefreshProjectStatsAndUi();
        }

        private void ProjectsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProjectsListBox.SelectedItem is not ProjectItem project) return;
            PersistCurrentProject(project.Id);
            SaveProjects(project.Id);
        }

        private void ProjectCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ProjectItem project)
                return;

            OpenProject(project);
            e.Handled = true;
        }

        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ProjectItem project)
                return;

            OpenProject(project);
            e.Handled = true;
        }

        private void ViewProjectRecords_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ProjectItem project)
                return;

            ProjectsListBox.SelectedItem = project;
            PersistCurrentProject(project.Id);
            SaveProjects(project.Id);

            if (Window.GetWindow(this) is MainWindow mainWindow)
                mainWindow.NavigateToRequestLog(project.Id);

            e.Handled = true;
        }

        private void ToggleProjectStatus_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ProjectItem project)
                return;

            project.Status = string.Equals(project.Status, "Paused", StringComparison.OrdinalIgnoreCase)
                ? "Active"
                : "Paused";
            project.UpdatedAt = DateTimeOffset.Now;

            ProjectsListBox.SelectedItem = project;
            PersistCurrentProject(project.Id);
            SaveProjects(project.Id);
            RefreshProjectStatsAndUi();
            e.Handled = true;
        }

        private void DeleteProject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not ProjectItem project)
                return;

            var result = MessageBox.Show(
                $"Delete project '{project.Name}' and its saved request log, imported files, and project memory from this app?",
                "Delete Project",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            DeleteProjectData(project.Id);

            var wasSelected = (ProjectsListBox.SelectedItem as ProjectItem)?.Id == project.Id;
            _projects.Remove(project);

            Guid? nextProjectId = null;
            if (_projects.Count == 0)
            {
                PersistNoCurrentProject();
            }
            else
            {
                var nextProject = wasSelected ? _projects.FirstOrDefault() : (ProjectsListBox.SelectedItem as ProjectItem);
                nextProjectId = nextProject?.Id ?? _projects.First().Id;
                ProjectsListBox.SelectedItem = _projects.FirstOrDefault(p => p.Id == nextProjectId.Value);
                PersistCurrentProject(nextProjectId.Value);
            }

            SaveProjects(nextProjectId);
            RefreshProjectStatsAndUi();
            e.Handled = true;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _projectsView.Refresh();
        }

        private bool FilterProject(object obj)
        {
            if (obj is not ProjectItem project) return false;

            var query = (SearchTextBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(query))
                return true;

            return project.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   project.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   project.Status.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   project.Health.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void PersistCurrentProject(Guid id)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_currentPath)!);
                File.WriteAllText(_currentPath, JsonSerializer.Serialize(new { currentProjectId = id }, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // ignore MVP
            }
        }

        private void PersistNoCurrentProject()
        {
            try
            {
                if (File.Exists(_currentPath))
                    File.Delete(_currentPath);
            }
            catch
            {
                // ignore MVP
            }
        }

        private void OpenProject(ProjectItem project)
        {
            ProjectsListBox.SelectedItem = project;
            ProjectsListBox.ScrollIntoView(project);
            PersistCurrentProject(project.Id);
            SaveProjects(project.Id);

            if (Window.GetWindow(this) is MainWindow mainWindow)
                mainWindow.NavigateToRequestBuilder();
        }

        private void RefreshProjectStatsAndUi()
        {
            var logs = LoadLogs();
            var statsByProject = BuildProjectStatsMap(logs);
            var apiKeyProviders = LoadApiKeyProviders();

            foreach (var project in _projects)
            {
                statsByProject.TryGetValue(project.Id, out var stats);
                stats ??= new ProjectStats();

                project.RequestsCount = stats.RequestsCount;
                if (stats.LastRequestAt.HasValue)
                    project.UpdatedAt = stats.LastRequestAt.Value;

                var missingKeyForLastProvider =
                    !string.IsNullOrWhiteSpace(stats.LastProvider) &&
                    !apiKeyProviders.Contains(stats.LastProvider, StringComparer.OrdinalIgnoreCase);

                project.Health = stats.RecentFailureCount > 0 || stats.RecentRateLimitCount >= 2 || missingKeyForLastProvider
                    ? "Needs attention"
                    : "Healthy";
            }

            UpdateQuickStats(logs);
            _projectsView.Refresh();
        }

        private Dictionary<Guid, ProjectStats> BuildProjectStatsMap(LogEntry[] logs)
        {
            var result = new Dictionary<Guid, ProjectStats>();

            foreach (var group in logs
                .Where(log => log.ProjectId.HasValue)
                .GroupBy(log => log.ProjectId!.Value))
            {
                var ordered = group
                    .OrderByDescending(log => log.Timestamp)
                    .ToList();

                var recent5 = ordered.Take(5).ToList();
                var recent10 = ordered.Take(10).ToList();

                result[group.Key] = new ProjectStats
                {
                    RequestsCount = ordered.Count,
                    LastRequestAt = ordered.FirstOrDefault()?.Timestamp,
                    LastProvider = ordered.FirstOrDefault()?.Provider ?? "",
                    RecentFailureCount = recent5.Count(log => !string.Equals(log.Status, "Success", StringComparison.OrdinalIgnoreCase)),
                    RecentRateLimitCount = recent10.Count(log =>
                        string.Equals(log.Status, "Rate Limit", StringComparison.OrdinalIgnoreCase) || log.StatusCode == 429)
                };
            }

            return result;
        }

        private void UpdateQuickStats(LogEntry[] logs)
        {
            RequestsCountText.Text = logs.Length.ToString();
            MemoryCountText.Text = LoadTotalMemoryCount().ToString();
            FilesCountText.Text = LoadTotalFilesCount().ToString();
        }

        private int LoadTotalFilesCount()
        {
            try
            {
                if (!File.Exists(_filesPath)) return 0;
                var json = File.ReadAllText(_filesPath);
                var store = JsonSerializer.Deserialize<FilesStore>(json) ?? new FilesStore();
                return store.Files.Length;
            }
            catch
            {
                return 0;
            }
        }

        private int LoadTotalMemoryCount()
        {
            try
            {
                if (!File.Exists(_memoryPath)) return 0;
                var json = File.ReadAllText(_memoryPath);
                var store = JsonSerializer.Deserialize<ProjectMemoryStore>(json) ?? new ProjectMemoryStore();
                return store.Projects.Sum(project => project.Items?.Length ?? 0);
            }
            catch
            {
                return 0;
            }
        }

        private void DeleteProjectData(Guid projectId)
        {
            DeleteLogEntries(projectId);
            DeleteFileEntries(projectId);
            DeleteMemoryEntries(projectId);
        }

        private void DeleteLogEntries(Guid projectId)
        {
            try
            {
                if (!File.Exists(_logPath)) return;

                var json = File.ReadAllText(_logPath);
                var logs = JsonSerializer.Deserialize<LogEntry[]>(json) ?? Array.Empty<LogEntry>();
                var filtered = logs.Where(log => log.ProjectId != projectId).ToArray();
                File.WriteAllText(_logPath, JsonSerializer.Serialize(filtered, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // keep deletion resilient
            }
        }

        private void DeleteFileEntries(Guid projectId)
        {
            try
            {
                if (!File.Exists(_filesPath)) return;

                var json = File.ReadAllText(_filesPath);
                var store = JsonSerializer.Deserialize<FilesStore>(json) ?? new FilesStore();
                store.Files = store.Files.Where(file => file.ProjectId != projectId).ToArray();
                File.WriteAllText(_filesPath, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // keep deletion resilient
            }
        }

        private void DeleteMemoryEntries(Guid projectId)
        {
            try
            {
                if (!File.Exists(_memoryPath)) return;

                var json = File.ReadAllText(_memoryPath);
                var store = JsonSerializer.Deserialize<ProjectMemoryStore>(json) ?? new ProjectMemoryStore();
                store.Projects = store.Projects.Where(project => project.ProjectId != projectId).ToArray();
                File.WriteAllText(_memoryPath, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // keep deletion resilient
            }
        }

        private LogEntry[] LoadLogs()
        {
            try
            {
                if (!File.Exists(_logPath)) return Array.Empty<LogEntry>();
                var json = File.ReadAllText(_logPath);
                return JsonSerializer.Deserialize<LogEntry[]>(json) ?? Array.Empty<LogEntry>();
            }
            catch
            {
                return Array.Empty<LogEntry>();
            }
        }

        private string[] LoadApiKeyProviders()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return Array.Empty<string>();
                var json = File.ReadAllText(_settingsPath);
                var model = JsonSerializer.Deserialize<SettingsModel>(json);
                if (model?.ApiKeys == null) return Array.Empty<string>();
                return model.ApiKeys
                    .Where(key => !string.IsNullOrWhiteSpace(key.Provider) && !string.IsNullOrWhiteSpace(key.EncryptedValue))
                    .Select(key => key.Provider.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
