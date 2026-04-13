using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace MultiLLMProjectAssistant.UI.Views
{
    public partial class FileManagementView : UserControl
    {
        private static readonly Guid DemoNit6150ProjectId = Guid.Parse("73878590-e89f-4a50-a68b-cc3a4a983103");

        private sealed class FileItem : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;

            public Guid? ProjectId { get; set; }
            public string Path { get; set; } = "";
            public string Name { get; set; } = "";
            public string Type { get; set; } = "";
            public long SizeBytes { get; set; }
            public DateTimeOffset Timestamp { get; set; }
            public string Md5 { get; set; } = "";

            private bool _isAttached;
            public bool IsAttached
            {
                get => _isAttached;
                set
                {
                    if (_isAttached == value) return;
                    _isAttached = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAttached)));
                }
            }

            [JsonIgnore]
            public string SizeDisplay => FormatSize(SizeBytes);

            [JsonIgnore]
            public string TimestampDisplay => Timestamp.ToString("yyyy-MM-dd HH:mm");

            [JsonIgnore]
            public string Md5Short => Md5.Length > 10 ? $"{Md5.Substring(0, 5)}...{Md5.Substring(Md5.Length - 2)}" : Md5;

            private static string FormatSize(long bytes)
            {
                if (bytes < 1024) return $"{bytes} B";
                var kb = bytes / 1024d;
                if (kb < 1024) return $"{kb:0.#} KB";
                var mb = kb / 1024d;
                if (mb < 1024) return $"{mb:0.#} MB";
                var gb = mb / 1024d;
                return $"{gb:0.#} GB";
            }
        }

        private sealed class FilesStore
        {
            public FileItem[] Files { get; set; } = Array.Empty<FileItem>();
        }

        private readonly ObservableCollection<FileItem> _files = new();
        private ICollectionView? _filesView;
        private bool _isInitialized;
        private readonly string _filesPath;
        private readonly string _currentProjectPath;
        private readonly Guid? _currentProjectId;

        public FileManagementView()
        {
            InitializeComponent();

            var dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MultiLLMProjectAssistant");
            _filesPath = Path.Combine(dataFolder, "files.json");
            _currentProjectPath = Path.Combine(dataFolder, "current_project.json");
            _currentProjectId = LoadCurrentProjectId();

            _filesView = CollectionViewSource.GetDefaultView(_files);
            _filesView.Filter = FilterFiles;
            FilesListView.ItemsSource = _filesView;

            LoadFiles();
            _isInitialized = true;
            RefreshFilters();
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

        private void LoadFiles()
        {
            _files.Clear();

            var store = LoadStore();
            var currentProjectFiles = store.Files
                .Where(f => f.ProjectId == _currentProjectId)
                .OrderByDescending(f => f.Timestamp)
                .ToList();

            if (currentProjectFiles.Count == 0 && _currentProjectId == DemoNit6150ProjectId)
            {
                SeedDemoFiles();
                SaveFiles();
                return;
            }

            foreach (var file in currentProjectFiles)
            {
                HookFileItem(file);
                _files.Add(file);
            }
        }

        private FilesStore LoadStore()
        {
            try
            {
                if (!File.Exists(_filesPath))
                    return new FilesStore();

                var json = File.ReadAllText(_filesPath);
                return JsonSerializer.Deserialize<FilesStore>(json) ?? new FilesStore();
            }
            catch
            {
                return new FilesStore();
            }
        }

        private void SaveFiles()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filesPath)!);
                var existing = LoadStore();
                var otherProjects = existing.Files
                    .Where(f => f.ProjectId != _currentProjectId)
                    .ToList();

                var currentProjectFiles = _files.Select(CloneForSave).ToList();
                var store = new FilesStore
                {
                    Files = otherProjects.Concat(currentProjectFiles).ToArray()
                };

                File.WriteAllText(_filesPath, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // keep UI usable even if saving fails
            }
        }

        private void SeedDemoFiles()
        {
            AddFileItem(new FileItem
            {
                ProjectId = _currentProjectId,
                Name = "security_audit_report.pdf",
                Type = ".pdf",
                SizeBytes = 1_200_000,
                Timestamp = DateTimeOffset.Now.AddDays(-2),
                Md5 = "8f4e2d91d0a1b2c3d4e5f6a7b8c9a1",
                IsAttached = true
            });
            AddFileItem(new FileItem
            {
                ProjectId = _currentProjectId,
                Name = "system_requirements.txt",
                Type = ".txt",
                SizeBytes = 45_000,
                Timestamp = DateTimeOffset.Now.AddDays(-1),
                Md5 = "c4b91aa22bb33cc44dd55ee66ff77d2",
                IsAttached = false
            });
        }

        private bool FilterFiles(object obj)
        {
            if (obj is not FileItem file) return false;

            var query = (FileSearchTextBox.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(query) &&
                !file.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !file.Type.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !file.Md5.Contains(query, StringComparison.OrdinalIgnoreCase))
                return false;

            var selected = (TypeFilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All types";
            if (selected == "All types") return true;
            if (selected == ".pdf") return file.Type.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
            if (selected == ".json") return file.Type.Equals(".json", StringComparison.OrdinalIgnoreCase);
            if (selected == ".txt / .md") return file.Type.Equals(".txt", StringComparison.OrdinalIgnoreCase) || file.Type.Equals(".md", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private void RefreshFilters()
        {
            if (!_isInitialized || _filesView == null) return;
            _filesView.Refresh();
        }

        private void FileSearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshFilters();

        private void TypeFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshFilters();

        private void FilesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DeleteSelectedFileButton.IsEnabled = FilesListView.SelectedItem is FileItem;

            if (FilesListView.SelectedItem is not FileItem item)
            {
                PreviewTextBlock.Text = "";
                return;
            }

            PreviewTextBlock.Text = BuildPreview(item);
        }

        private string BuildPreview(FileItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Path) && File.Exists(item.Path))
            {
                var ext = item.Type.ToLowerInvariant();
                if (ext is ".txt" or ".md" or ".json" or ".csv" or ".log" or ".html")
                {
                    try
                    {
                        var text = File.ReadAllText(item.Path);
                        if (text.Length > 6000) text = text.Substring(0, 6000) + "\n...";
                        return text;
                    }
                    catch (Exception ex)
                    {
                        return $"(Preview error) {ex.Message}";
                    }
                }

                return $"Preview not available for {item.Type}. (Text preview supported for .txt/.md/.json/.csv/.log/.html)";
            }

            return $"(Workspace file) {item.Name}\n\nType: {item.Type}\nSize: {item.SizeDisplay}\nTimestamp: {item.TimestampDisplay}\nMD5: {item.Md5}\n\nImport a real file to preview its contents here.";
        }

        private void ImportFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Import files"
            };

            if (dlg.ShowDialog() != true) return;
            AddFiles(dlg.FileNames);
        }

        private void DeleteSelectedFile_Click(object sender, RoutedEventArgs e)
        {
            if (FilesListView.SelectedItem is not FileItem item) return;

            _files.Remove(item);
            SaveFiles();
            RefreshFilters();
            PreviewTextBlock.Text = "";
            DeleteSelectedFileButton.IsEnabled = false;
        }

        private void FilesPanel_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void FilesPanel_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            AddFiles(paths);
        }

        private void AddFiles(string[] paths)
        {
            var added = false;

            foreach (var path in paths.Where(File.Exists))
            {
                if (_files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var name = System.IO.Path.GetFileName(path);
                var ext = System.IO.Path.GetExtension(path);
                var info = new FileInfo(path);

                AddFileItem(new FileItem
                {
                    ProjectId = _currentProjectId,
                    Path = path,
                    Name = name,
                    Type = string.IsNullOrWhiteSpace(ext) ? "(none)" : ext,
                    SizeBytes = info.Length,
                    Timestamp = info.LastWriteTime,
                    Md5 = ComputeMd5(path),
                    IsAttached = false
                });

                added = true;
            }

            if (added)
            {
                SaveFiles();
                RefreshFilters();
            }
        }

        private void AddFileItem(FileItem item)
        {
            HookFileItem(item);
            _files.Add(item);
        }

        private void HookFileItem(FileItem item)
        {
            item.PropertyChanged -= FileItem_PropertyChanged;
            item.PropertyChanged += FileItem_PropertyChanged;
        }

        private void FileItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileItem.IsAttached))
                SaveFiles();
        }

        private static FileItem CloneForSave(FileItem item)
        {
            return new FileItem
            {
                ProjectId = item.ProjectId,
                Path = item.Path,
                Name = item.Name,
                Type = item.Type,
                SizeBytes = item.SizeBytes,
                Timestamp = item.Timestamp,
                Md5 = item.Md5,
                IsAttached = item.IsAttached
            };
        }

        private static string ComputeMd5(string filePath)
        {
            try
            {
                using var md5 = MD5.Create();
                using var stream = File.OpenRead(filePath);
                var hash = md5.ComputeHash(stream);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
            catch
            {
                return "";
            }
        }
    }
}
