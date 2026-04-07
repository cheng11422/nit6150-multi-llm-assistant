using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

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

        // Variable to hold the Top-K number
        private int _topKValue = 5;
        private readonly string _contextFilePath;
        private readonly string _logFilePath;
        private readonly string _currentProjectPath;

        public RequestBuilderView()
        {
            InitializeComponent();
            _contextFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MultiLLMProjectAssistant",
                "request_context.json");
            _logFilePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MultiLLMProjectAssistant",
                "requests_log.json");
            _currentProjectPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MultiLLMProjectAssistant",
                "current_project.json");

            LoadContextIfPresent();
        }

        // Logic to increase Top-K
        private void IncreaseTopK_Click(object sender, RoutedEventArgs e)
        {
            if (_topKValue < 10) // Prevents it from going above 10 (you can change this)
            {
                _topKValue++;
                TopKText.Text = _topKValue.ToString();
            }
        }

        // Logic to decrease Top-K
        private void DecreaseTopK_Click(object sender, RoutedEventArgs e)
        {
            if (_topKValue > 1) // Prevents it from going below 1
            {
                _topKValue--;
                TopKText.Text = _topKValue.ToString();
            }
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

        private void AddAttachments(string[] filePaths)
        {
            if (filePaths == null || filePaths.Length == 0) return;

            var existing = GetAttachedFilePaths().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var path in filePaths)
            {
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (existing.Contains(path)) continue;
                AttachmentsPanel.Children.Add(BuildAttachmentPill(path));
                existing.Add(path);
            }
        }

        private Border BuildAttachmentPill(string fullPath)
        {
            var fileName = Path.GetFileName(fullPath);

            var nameText = new TextBlock
            {
                Text = fileName,
                Foreground = System.Windows.Media.Brushes.LightGray,
                FontSize = 11,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 180,
                ToolTip = fullPath
            };

            var removeBtn = new Button
            {
                Content = "✕",
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = fullPath,
                Style = (Style)FindResource("IconTextButton")
            };
            removeBtn.Click += RemoveAttachment_Click;

            var inner = new StackPanel { Orientation = Orientation.Horizontal };
            inner.Children.Add(nameText);
            inner.Children.Add(removeBtn);

            var pill = new Border
            {
                Style = (Style)FindResource("AttachmentPill"),
                Tag = fullPath,
                Child = inner
            };

            return pill;
        }

        private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var path = btn.Tag as string;
            if (string.IsNullOrWhiteSpace(path)) return;

            var toRemove = AttachmentsPanel.Children
                .OfType<Border>()
                .FirstOrDefault(b => string.Equals(b.Tag as string, path, StringComparison.OrdinalIgnoreCase));

            if (toRemove != null)
                AttachmentsPanel.Children.Remove(toRemove);

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
            // UX: mouse wheel scrolls horizontally inside attachments strip.
            if (sender is not ScrollViewer sv) return;
            if (sv.ScrollableWidth <= 0) return;

            sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta);
            e.Handled = true;
        }

        private void SubmitRequest_Click(object sender, RoutedEventArgs e)
        {
            var snapshot = BuildSnapshot();
            // MVP behavior: persist context + show placeholder responses so the UI flow is complete.
            SaveSnapshot(snapshot);

            if (string.IsNullOrWhiteSpace(RawJsonResponseTextBox.Text))
            {
                RawJsonResponseTextBox.Text = "{\n  \"status\": \"ok\",\n  \"message\": \"(MVP) Submit wired. Hook up HTTP call to provider next.\",\n  \"provider\": \"" + (snapshot.Provider ?? "") + "\"\n}";
            }

            if (string.IsNullOrWhiteSpace(NormalizedJsonResponseTextBox.Text))
            {
                NormalizedJsonResponseTextBox.Text = "{\n  \"provider\": \"" + (snapshot.Provider ?? "") + "\",\n  \"template\": \"" + (snapshot.Template ?? "") + "\",\n  \"topK\": " + snapshot.TopK + ",\n  \"memoryEnabled\": " + snapshot.MemoryEnabled.ToString().ToLowerInvariant() + "\n}";
            }

            if (string.IsNullOrWhiteSpace(SummaryResponseTextBox.Text))
            {
                SummaryResponseTextBox.Text = "(MVP) Request captured and context saved. Switching providers will restore the JSON request and last responses from disk.";
            }

            SaveSnapshot(BuildSnapshot());

            AppendLogEntry(BuildSnapshot());
        }

        private void AppendLogEntry(RequestContextSnapshot snapshot)
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

                Guid? projectId = null;
                try
                {
                    if (File.Exists(_currentProjectPath))
                    {
                        var pj = File.ReadAllText(_currentProjectPath);
                        using var doc = JsonDocument.Parse(pj);
                        if (doc.RootElement.TryGetProperty("currentProjectId", out var idEl) &&
                            idEl.ValueKind == JsonValueKind.String &&
                            Guid.TryParse(idEl.GetString(), out var parsed))
                            projectId = parsed;
                    }
                }
                catch
                {
                    projectId = null;
                }

                var entry = new LogEntry
                {
                    ProjectId = projectId,
                    Timestamp = DateTimeOffset.Now,
                    Provider = snapshot.Provider ?? "",
                    Status = "Success",
                    StatusCode = 200,
                    RequestJson = snapshot.JsonRequest ?? "",
                    RawJson = snapshot.RawJsonResponse ?? "",
                    NormalizedJson = snapshot.NormalizedJsonResponse ?? "",
                    Summary = snapshot.SummaryResponse ?? ""
                };

                var merged = new LogEntry[] { entry }.Concat(existing).Take(500).ToArray();
                var outJson = JsonSerializer.Serialize(merged, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_logFilePath, outJson);
            }
            catch
            {
                // ignore MVP
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
            // Provider switching restores the latest saved context (MVP).
            LoadContextIfPresent();
        }

        private void JsonRequestTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;

            // Enter submits, Shift+Enter makes a newline for JSON formatting.
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;

            e.Handled = true;
            SubmitRequest_Click(sender, new RoutedEventArgs());
        }

        private RequestContextSnapshot BuildSnapshot()
        {
            var provider = (ProviderComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            var template = (TemplateComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

            return new RequestContextSnapshot
            {
                Provider = provider,
                Template = template,
                TopK = _topKValue,
                MemoryEnabled = true, // toggle can be wired later; UI currently defaults to enabled
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
                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_contextFilePath, json);
            }
            catch
            {
                // ignore in MVP; UI still functions
            }
        }

        private void LoadContextIfPresent()
        {
            try
            {
                if (!File.Exists(_contextFilePath)) return;
                var json = File.ReadAllText(_contextFilePath);
                var snapshot = JsonSerializer.Deserialize<RequestContextSnapshot>(json);
                if (snapshot == null) return;

                if (!string.IsNullOrWhiteSpace(snapshot.Provider))
                {
                    foreach (var item in ProviderComboBox.Items)
                    {
                        if (item is ComboBoxItem cbi && string.Equals(cbi.Content?.ToString(), snapshot.Provider, StringComparison.OrdinalIgnoreCase))
                        {
                            ProviderComboBox.SelectedItem = cbi;
                            break;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(snapshot.Template))
                {
                    foreach (var item in TemplateComboBox.Items)
                    {
                        if (item is ComboBoxItem cbi && string.Equals(cbi.Content?.ToString(), snapshot.Template, StringComparison.OrdinalIgnoreCase))
                        {
                            TemplateComboBox.SelectedItem = cbi;
                            break;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(snapshot.JsonRequest))
                    JsonRequestTextBox.Text = snapshot.JsonRequest;

                RawJsonResponseTextBox.Text = snapshot.RawJsonResponse ?? string.Empty;
                NormalizedJsonResponseTextBox.Text = snapshot.NormalizedJsonResponse ?? string.Empty;
                SummaryResponseTextBox.Text = snapshot.SummaryResponse ?? string.Empty;

                // Restore attachments
                AttachmentsPanel.Children.Clear();
                if (snapshot.AttachedFiles != null && snapshot.AttachedFiles.Length > 0)
                    AddAttachments(snapshot.AttachedFiles);
            }
            catch
            {
                // ignore in MVP
            }
        }
    }
}