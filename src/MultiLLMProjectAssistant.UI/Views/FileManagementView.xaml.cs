using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace MultiLLMProjectAssistant.UI.Views
{
    public partial class FileManagementView : UserControl
    {
        private sealed class FileItem : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;

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

            public string SizeDisplay => FormatSize(SizeBytes);
            public string TimestampDisplay => Timestamp.ToString("yyyy-MM-dd HH:mm");
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

        private readonly ObservableCollection<FileItem> _files = new();
        private ICollectionView? _filesView;
        private bool _isInitialized;

        public FileManagementView()
        {
            InitializeComponent();
            _filesView = CollectionViewSource.GetDefaultView(_files);
            _filesView.Filter = FilterFiles;
            FilesListView.ItemsSource = _filesView;

            SeedDemoFiles();
            _isInitialized = true;
            RefreshFilters();
        }

        private void SeedDemoFiles()
        {
            // demo entries (MVP)
            _files.Add(new FileItem
            {
                Name = "security_audit_report.pdf",
                Type = ".pdf",
                SizeBytes = 1_200_000,
                Timestamp = DateTimeOffset.Now.AddDays(-2),
                Md5 = "8f4e2d91d0a1b2c3d4e5f6a7b8c9a1",
                IsAttached = true
            });
            _files.Add(new FileItem
            {
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
            if (obj is not FileItem f) return false;

            var query = (FileSearchTextBox.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(query) &&
                !f.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !f.Type.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                !f.Md5.Contains(query, StringComparison.OrdinalIgnoreCase))
                return false;

            var selected = (TypeFilterComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All types";
            if (selected == "All types") return true;
            if (selected == ".pdf") return f.Type.Equals(".pdf", StringComparison.OrdinalIgnoreCase);
            if (selected == ".json") return f.Type.Equals(".json", StringComparison.OrdinalIgnoreCase);
            if (selected == ".txt / .md") return f.Type.Equals(".txt", StringComparison.OrdinalIgnoreCase) || f.Type.Equals(".md", StringComparison.OrdinalIgnoreCase);
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
            if (FilesListView.SelectedItem is not FileItem item)
            {
                PreviewTextBlock.Text = "";
                return;
            }

            PreviewTextBlock.Text = BuildPreview(item);
        }

        private string BuildPreview(FileItem item)
        {
            // Try real file if available, otherwise demo message.
            if (!string.IsNullOrWhiteSpace(item.Path) && File.Exists(item.Path))
            {
                var ext = item.Type.ToLowerInvariant();
                if (ext is ".txt" or ".md" or ".json" or ".csv" or ".log")
                {
                    try
                    {
                        var text = File.ReadAllText(item.Path);
                        if (text.Length > 6000) text = text.Substring(0, 6000) + "\n…";
                        return text;
                    }
                    catch (Exception ex)
                    {
                        return $"(Preview error) {ex.Message}";
                    }
                }

                return $"Preview not available for {item.Type}. (Text preview supported for .txt/.md/.json/.csv)";
            }

            return $"(Demo) {item.Name}\n\nType: {item.Type}\nSize: {item.SizeDisplay}\nTimestamp: {item.TimestampDisplay}\nMD5: {item.Md5}\n\nImport a real file to see its content here.";
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
            foreach (var path in paths.Where(File.Exists))
            {
                var name = System.IO.Path.GetFileName(path);
                var ext = System.IO.Path.GetExtension(path);
                var info = new FileInfo(path);

                // prevent duplicates by path
                if (_files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
                    continue;

                _files.Add(new FileItem
                {
                    Path = path,
                    Name = name,
                    Type = string.IsNullOrWhiteSpace(ext) ? "(none)" : ext,
                    SizeBytes = info.Length,
                    Timestamp = info.LastWriteTime,
                    Md5 = ComputeMd5(path),
                    IsAttached = false
                });
            }

            RefreshFilters();
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
