using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MultiLLMProjectAssistant.UI.Views
{
    public partial class ProjectSelectionView : UserControl
    {
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

        private sealed class ProjectItem : INotifyPropertyChanged
        {
            private string _name = "";
            private string _description = "";
            private string _status = "Active";
            private DateTimeOffset _updatedAt = DateTimeOffset.Now;
            private int _requests = 0;
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
                set { _status = value; OnChanged(nameof(Status)); }
            }

            public DateTimeOffset UpdatedAt
            {
                get => _updatedAt;
                set { _updatedAt = value; OnChanged(nameof(UpdatedAt)); OnChanged(nameof(MetaLine)); }
            }

            public int RequestsCount
            {
                get => _requests;
                set { _requests = value; OnChanged(nameof(RequestsCount)); OnChanged(nameof(MetaLine)); }
            }

            public string Health
            {
                get => _health;
                set { _health = value; OnChanged(nameof(Health)); OnChanged(nameof(HealthLine)); OnChanged(nameof(HealthColor)); }
            }

            public string MetaLine => $"Last modified: {UpdatedAt:yyyy-MM-dd} • Requests: {RequestsCount}";
            public string HealthLine => $"Status: {Health}";
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

        private readonly ObservableCollection<ProjectItem> _projects = new();
        private readonly string _projectsPath;
        private readonly string _currentPath;
        private readonly string _logPath;
        private readonly string _settingsPath;

        public ProjectSelectionView()
        {
            InitializeComponent();
            _projectsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MultiLLMProjectAssistant",
                "projects.json");
            _currentPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MultiLLMProjectAssistant",
                "current_project.json");
            _logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MultiLLMProjectAssistant",
                "requests_log.json");
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MultiLLMProjectAssistant",
                "settings.json");

            LoadProjects();
            ProjectsListBox.ItemsSource = _projects;
        }

        private void LoadProjects()
        {
            try
            {
                if (!File.Exists(_projectsPath))
                {
                    SeedDemo();
                    SaveProjects();
                    RecomputeProjectHealthAndCounts();
                    return;
                }

                var json = File.ReadAllText(_projectsPath);
                var store = JsonSerializer.Deserialize<ProjectStore>(json) ?? new ProjectStore();
                _projects.Clear();
                foreach (var p in store.Projects.OrderByDescending(x => x.UpdatedAt))
                    _projects.Add(p);

                if (store.CurrentProjectId.HasValue)
                {
                    var selected = _projects.FirstOrDefault(p => p.Id == store.CurrentProjectId.Value);
                    if (selected != null)
                        ProjectsListBox.SelectedItem = selected;
                }

                RecomputeProjectHealthAndCounts();
            }
            catch
            {
                SeedDemo();
                RecomputeProjectHealthAndCounts();
            }
        }

        private void SaveProjects(Guid? currentId = null)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_projectsPath)!);
                var store = new ProjectStore
                {
                    CurrentProjectId = currentId ?? (ProjectsListBox.SelectedItem as ProjectItem)?.Id,
                    Projects = _projects.ToArray()
                };
                File.WriteAllText(_projectsPath, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // ignore MVP
            }
        }

        private void SeedDemo()
        {
            _projects.Clear();
            _projects.Add(new ProjectItem
            {
                Name = "NIT6150 Multi-LLM",
                Description = "Desktop app build with WPF. Multi-provider request workflows and project memory.",
                Status = "Active",
                Health = "Healthy",
                UpdatedAt = DateTimeOffset.Now,
                RequestsCount = 38
            });
            _projects.Add(new ProjectItem
            {
                Name = "Web Security Audit",
                Description = "Pen-test notes, findings, and remediation plan with traceable JSON logs.",
                Status = "Paused",
                Health = "Needs attention",
                UpdatedAt = DateTimeOffset.Now.AddDays(-2),
                RequestsCount = 12
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
            SaveProjects(project.Id);
            PersistCurrentProject(project.Id);
            RecomputeProjectHealthAndCounts();
        }

        private void ProjectsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProjectsListBox.SelectedItem is not ProjectItem p) return;
            PersistCurrentProject(p.Id);
            SaveProjects(p.Id);
            RecomputeProjectHealthAndCounts();
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

        private void RecomputeProjectHealthAndCounts()
        {
            var logs = LoadLogs();
            var keys = LoadApiKeyProviders();

            foreach (var p in _projects)
            {
                var plogs = logs
                    .Where(l => l.ProjectId.HasValue && l.ProjectId.Value == p.Id)
                    .OrderByDescending(l => l.Timestamp)
                    .ToList();

                p.RequestsCount = plogs.Count;
                if (plogs.Count > 0)
                    p.UpdatedAt = plogs[0].Timestamp;

                // Health rules (local, no backend):
                // - Needs attention if recent error/rate limit spikes OR missing key for last-used provider.
                // - Otherwise Healthy.
                var recent5 = plogs.Take(5).ToList();
                var recent10 = plogs.Take(10).ToList();

                var hasRecentFailure = recent5.Any(l => !string.Equals(l.Status, "Success", StringComparison.OrdinalIgnoreCase));
                var rateLimits = recent10.Count(l => string.Equals(l.Status, "Rate Limit", StringComparison.OrdinalIgnoreCase) || l.StatusCode == 429);

                var lastProvider = plogs.FirstOrDefault()?.Provider ?? "";
                var missingKeyForLastProvider =
                    !string.IsNullOrWhiteSpace(lastProvider) &&
                    !keys.Contains(lastProvider, StringComparer.OrdinalIgnoreCase);

                if (hasRecentFailure || rateLimits >= 2 || missingKeyForLastProvider)
                    p.Health = "Needs attention";
                else
                    p.Health = "Healthy";
            }

            // Persist back so badges remain consistent across sessions.
            SaveProjects((ProjectsListBox.SelectedItem as ProjectItem)?.Id);
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
                    .Where(k => !string.IsNullOrWhiteSpace(k.Provider) && !string.IsNullOrWhiteSpace(k.EncryptedValue))
                    .Select(k => k.Provider.Trim())
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
