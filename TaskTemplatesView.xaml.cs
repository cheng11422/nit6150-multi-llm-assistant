using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MultiLLMProjectAssistant.UI.Views
{
    public partial class TaskTemplatesView : UserControl
    {
        private sealed class TemplateItem
        {
            public Guid Id { get; set; } = Guid.NewGuid();
            public string Name { get; set; } = "";
            public string Description { get; set; } = "";
            public string Content { get; set; } = "";
            public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;
            public string LastModifiedDisplay => LastModified.ToString("yyyy-MM-dd HH:mm");
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

        private readonly ObservableCollection<TemplateItem> _templates = new();
        private TemplateItem? _selected;
        private readonly string _templatesPath;
        private readonly string _requestContextPath;
        private bool _isDirty;

        public TaskTemplatesView()
        {
            InitializeComponent();
            _templatesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MultiLLMProjectAssistant",
                "templates.json");
            _requestContextPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MultiLLMProjectAssistant",
                "request_context.json");

            LoadTemplates();
            if (_templates.Count == 0)
            {
                SeedDefaults();
                SaveTemplates();
            }

            RefreshList();
            UpdateActions();
        }

        private void SeedDefaults()
        {
            _templates.Add(new TemplateItem
            {
                Name = "Code Review",
                Description = "System instruction: You are an expert Software Engineer reviewing code. Give best practices and point out vulnerabilities.",
                Content = "{\n  \"system\": \"You are an expert software engineer reviewing code.\",\n  \"task\": \"Code Review\",\n  \"instructions\": [\n    \"List best practices\",\n    \"Identify security issues\",\n    \"Suggest improvements\"\n  ]\n}",
                LastModified = DateTimeOffset.Now.AddDays(-2)
            });
            _templates.Add(new TemplateItem
            {
                Name = "Literature Analysis",
                Description = "Analyze provided documents and extract key findings, methodologies, and outcomes.",
                Content = "{\n  \"task\": \"Literature Analysis\",\n  \"output\": {\n    \"key_findings\": [],\n    \"methodologies\": [],\n    \"outcomes\": []\n  }\n}",
                LastModified = DateTimeOffset.Now.AddDays(-1)
            });
        }

        private void RefreshList()
        {
            var selectedId = _selected?.Id;
            var q = (TemplateSearchTextBox.Text ?? "").Trim();
            var items = string.IsNullOrWhiteSpace(q)
                ? _templates.OrderByDescending(t => t.LastModified)
                : _templates.Where(t =>
                        t.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        t.Description.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        t.Content.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(t => t.LastModified);

            var list = items.ToList();
            TemplatesListBox.ItemsSource = list;
            TemplatesListBox.ItemTemplate = BuildItemTemplate();

            if (selectedId.HasValue)
            {
                var match = list.FirstOrDefault(t => t.Id == selectedId.Value);
                if (match != null) TemplatesListBox.SelectedItem = match;
            }
        }

        private DataTemplate BuildItemTemplate()
        {
            var template = new DataTemplate(typeof(TemplateItem));
            var sp = new FrameworkElementFactory(typeof(StackPanel));
            sp.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);

            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name"));
            name.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.White);
            name.SetValue(TextBlock.FontSizeProperty, 14d);
            name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            sp.AppendChild(name);

            var desc = new FrameworkElementFactory(typeof(TextBlock));
            desc.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Description"));
            desc.SetValue(TextBlock.ForegroundProperty, (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#D0D0D0")!);
            desc.SetValue(TextBlock.FontSizeProperty, 12d);
            desc.SetValue(TextBlock.MarginProperty, new Thickness(0, 10, 0, 0));
            desc.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            sp.AppendChild(desc);

            var meta = new FrameworkElementFactory(typeof(TextBlock));
            meta.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("LastModifiedDisplay") { StringFormat = "Last modified: {0}" });
            meta.SetValue(TextBlock.ForegroundProperty, (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#A0A0A0")!);
            meta.SetValue(TextBlock.FontSizeProperty, 11d);
            meta.SetValue(TextBlock.MarginProperty, new Thickness(0, 12, 0, 0));
            sp.AppendChild(meta);

            template.VisualTree = sp;
            return template;
        }

        private void LoadDetails(TemplateItem? item)
        {
            _selected = item;
            _isDirty = false;
            if (item == null)
            {
                NameTextBox.Text = "";
                DescriptionTextBox.Text = "";
                ContentTextBox.Text = "";
                LastModifiedTextBox.Text = "";
                UpdateActions();
                return;
            }

            NameTextBox.Text = item.Name;
            DescriptionTextBox.Text = item.Description;
            ContentTextBox.Text = item.Content;
            LastModifiedTextBox.Text = item.LastModifiedDisplay;
            UpdateActions();
        }

        private TemplateItem BuildFromDetails()
        {
            return new TemplateItem
            {
                Id = _selected?.Id ?? Guid.NewGuid(),
                Name = (NameTextBox.Text ?? "").Trim(),
                Description = (DescriptionTextBox.Text ?? "").Trim(),
                Content = (ContentTextBox.Text ?? "").Trim(),
                LastModified = DateTimeOffset.Now
            };
        }

        private void LoadTemplates()
        {
            try
            {
                if (!File.Exists(_templatesPath)) return;
                var json = File.ReadAllText(_templatesPath);
                var items = JsonSerializer.Deserialize<TemplateItem[]>(json);
                if (items == null) return;
                _templates.Clear();
                foreach (var t in items) _templates.Add(t);
            }
            catch
            {
                // ignore MVP
            }
        }

        private void SaveTemplates()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_templatesPath)!);
                var json = JsonSerializer.Serialize(_templates, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_templatesPath, json);
            }
            catch
            {
                // ignore MVP
            }
        }

        private void NewTemplate_Click(object sender, RoutedEventArgs e)
        {
            LoadDetails(new TemplateItem());
            NameTextBox.Focus();
        }

        private void SaveTemplate_Click(object sender, RoutedEventArgs e)
        {
            var updated = BuildFromDetails();
            if (string.IsNullOrWhiteSpace(updated.Name)) return;

            var existing = _templates.FirstOrDefault(t => t.Id == updated.Id);
            if (existing == null)
                _templates.Add(updated);
            else
            {
                existing.Name = updated.Name;
                existing.Description = updated.Description;
                existing.Content = updated.Content;
                existing.LastModified = updated.LastModified;
            }

            SaveTemplates();
            RefreshList();
            LoadDetails(_templates.FirstOrDefault(t => t.Id == updated.Id));
            _isDirty = false;
            UpdateActions();
        }

        private void DeleteTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            var existing = _templates.FirstOrDefault(t => t.Id == _selected.Id);
            if (existing != null) _templates.Remove(existing);
            SaveTemplates();
            LoadDetails(null);
            RefreshList();
            UpdateActions();
        }

        private void ApplyToRequestBuilder_Click(object sender, RoutedEventArgs e)
        {
            var current = BuildFromDetails();
            if (string.IsNullOrWhiteSpace(current.Name)) return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_requestContextPath)!);
                var store = LoadContextStore();
                var targetProvider = string.IsNullOrWhiteSpace(store.LastProvider) ? "OpenAI-GPT4" : store.LastProvider!;

                var snapshot = store.Snapshots.FirstOrDefault(s =>
                    string.Equals(s.Provider, targetProvider, StringComparison.OrdinalIgnoreCase))
                    ?? new RequestContextSnapshot
                    {
                        Provider = targetProvider,
                        TopK = 5,
                        MemoryEnabled = true
                    };

                snapshot.Template = current.Name;
                snapshot.UpdatedAt = DateTimeOffset.UtcNow;
                snapshot.JsonRequest = current.Content;
                snapshot.RawJsonResponse = "";
                snapshot.NormalizedJsonResponse = "";
                snapshot.SummaryResponse = "";

                store.LastProvider = targetProvider;
                store.Snapshots = store.Snapshots
                    .Where(s => !string.Equals(s.Provider, targetProvider, StringComparison.OrdinalIgnoreCase))
                    .Append(snapshot)
                    .OrderByDescending(s => s.UpdatedAt)
                    .ToArray();

                var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_requestContextPath, json);
            }
            catch
            {
                // ignore MVP
            }
        }

        private RequestContextStore LoadContextStore()
        {
            try
            {
                if (!File.Exists(_requestContextPath))
                    return new RequestContextStore();

                var json = File.ReadAllText(_requestContextPath);
                return JsonSerializer.Deserialize<RequestContextStore>(json) ?? new RequestContextStore();
            }
            catch (JsonException)
            {
                try
                {
                    var json = File.ReadAllText(_requestContextPath);
                    var snapshot = JsonSerializer.Deserialize<RequestContextSnapshot>(json);
                    if (snapshot == null)
                        return new RequestContextStore();

                    return new RequestContextStore
                    {
                        LastProvider = snapshot.Provider,
                        Snapshots = new[] { snapshot }
                    };
                }
                catch
                {
                    return new RequestContextStore();
                }
            }
            catch
            {
                return new RequestContextStore();
            }
        }

        private void TemplateSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshList();
        }

        private void TemplatesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TemplatesListBox.SelectedItem is TemplateItem item)
                LoadDetails(item);
        }

        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            _isDirty = true;
            UpdateActions();
        }

        private void UpdateActions()
        {
            var hasSelection = _selected != null && !string.IsNullOrWhiteSpace(_selected.Id.ToString());
            var hasName = !string.IsNullOrWhiteSpace(NameTextBox.Text);

            if (DeleteButton != null) DeleteButton.IsEnabled = hasSelection && _selected?.Id != Guid.Empty;
            if (ApplyButton != null) ApplyButton.IsEnabled = hasName;
            if (SaveButton != null) SaveButton.IsEnabled = hasName && _isDirty;
        }

        private void Root_KeyDown(object sender, KeyEventArgs e)
        {
            // Quality-of-life shortcuts for full functionality
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.S)
            {
                e.Handled = true;
                SaveTemplate_Click(sender, new RoutedEventArgs());
                return;
            }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.N)
            {
                e.Handled = true;
                NewTemplate_Click(sender, new RoutedEventArgs());
                return;
            }
            if (e.Key == Key.Delete && TemplatesListBox.IsKeyboardFocusWithin)
            {
                e.Handled = true;
                DeleteTemplate_Click(sender, new RoutedEventArgs());
            }
        }
    }
}
