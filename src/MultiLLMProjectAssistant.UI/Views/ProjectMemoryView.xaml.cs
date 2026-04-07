using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MultiLLMProjectAssistant.UI.Views
{
    public partial class ProjectMemoryView : UserControl
    {
        private sealed class MemoryItem
        {
            public Guid Id { get; set; } = Guid.NewGuid();
            public string Title { get; set; } = "";
            public string Content { get; set; } = "";
            public List<string> Tags { get; set; } = new();
            public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

            public string Preview
            {
                get
                {
                    var text = (Content ?? "").Trim();
                    if (text.Length <= 120) return text;
                    return text.Substring(0, 120) + "…";
                }
            }

            public string TagString => string.Join(", ", Tags);
        }

        private readonly ObservableCollection<MemoryItem> _allItems = new();
        private readonly ObservableCollection<MemoryItem> _filteredItems = new();
        private MemoryItem? _selected;
        private int _defaultTopK = 5;

        public ProjectMemoryView()
        {
            InitializeComponent();
            SeedDemoData();
            RefreshList();
            DefaultTopKText.Text = _defaultTopK.ToString();
        }

        private void SeedDemoData()
        {
            _allItems.Clear();
            _allItems.Add(new MemoryItem
            {
                Title = "Project Architecture Overview",
                Content = "Multi-tier system architecture with microservices pattern. Focus on scalability and maintainability.",
                Tags = new List<string> { "architecture", "documentation" },
                Timestamp = DateTimeOffset.Now.AddDays(-2)
            });
            _allItems.Add(new MemoryItem
            {
                Title = "Security Requirements",
                Content = "OWASP Top 10 compliance required. Implement JWT authentication and role-based access control.",
                Tags = new List<string> { "security", "requirements" },
                Timestamp = DateTimeOffset.Now.AddDays(-1)
            });
            _allItems.Add(new MemoryItem
            {
                Title = "Database Notes",
                Content = "Prefer migrations over manual schema changes. Track MD5 for imported SQL files. Keep connection strings out of logs.",
                Tags = new List<string> { "database" },
                Timestamp = DateTimeOffset.Now.AddHours(-6)
            });
        }

        private void RefreshList()
        {
            var query = (MemorySearchTextBox.Text ?? "").Trim();
            var selectedTags = GetSelectedTagFilters();

            IEnumerable<MemoryItem> items = _allItems;

            if (!string.IsNullOrWhiteSpace(query))
            {
                items = items.Where(i =>
                    i.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    i.Content.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    i.Tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase)));
            }

            if (selectedTags.Count > 0)
            {
                items = items.Where(i => selectedTags.All(tag => i.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)));
            }

            _filteredItems.Clear();
            foreach (var item in items.OrderByDescending(i => i.Timestamp))
                _filteredItems.Add(item);

            MemoryListBox.ItemsSource = _filteredItems;
            MemoryListBox.ItemTemplate = BuildItemTemplate();
        }

        private DataTemplate BuildItemTemplate()
        {
            // DataTemplate in code (MVP): Title, preview, tags + timestamp.
            var template = new DataTemplate(typeof(MemoryItem));

            var sp = new FrameworkElementFactory(typeof(StackPanel));
            sp.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
            sp.AppendChild(TextBlockFactory("Title", "#FFFFFF", 14, FontWeights.SemiBold));

            var preview = TextBlockFactory("Preview", "#D0D0D0", 12, FontWeights.Normal);
            preview.SetValue(TextBlock.MarginProperty, new Thickness(0, 10, 0, 0));
            sp.AppendChild(preview);

            // tags + timestamp row (dock: timestamp right)
            var meta = new FrameworkElementFactory(typeof(DockPanel));
            meta.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 12, 0, 0));
            meta.SetValue(DockPanel.LastChildFillProperty, true);

            var ts = TextBlockFactory("Timestamp", "#A0A0A0", 11, FontWeights.Normal);
            ts.SetValue(DockPanel.DockProperty, Dock.Right);
            ts.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 0, 0));
            ts.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Timestamp") { StringFormat = "yyyy-MM-dd HH:mm" });
            meta.AppendChild(ts);

            var tags = TextBlockFactory("TagString", "#A0A0A0", 11, FontWeights.Normal);
            tags.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            meta.AppendChild(tags);

            sp.AppendChild(meta);
            template.VisualTree = sp;

            return template;
        }

        private FrameworkElementFactory TextBlockFactory(string path, string colorHex, double size, FontWeight weight)
        {
            var tb = new FrameworkElementFactory(typeof(TextBlock));
            tb.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(path));
            tb.SetValue(TextBlock.ForegroundProperty, (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(colorHex)!);
            tb.SetValue(TextBlock.FontSizeProperty, size);
            tb.SetValue(TextBlock.FontWeightProperty, weight);
            tb.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            return tb;
        }

        private List<string> GetSelectedTagFilters()
        {
            var tags = new List<string>();
            if (TagApiToggle?.IsChecked == true) tags.Add("api");
            if (TagArchitectureToggle?.IsChecked == true) tags.Add("architecture");
            if (TagDatabaseToggle?.IsChecked == true) tags.Add("database");
            return tags;
        }

        private void LoadDetails(MemoryItem? item)
        {
            _selected = item;
            if (item == null)
            {
                TitleTextBox.Text = "";
                TagsTextBox.Text = "";
                TimestampTextBox.Text = "";
                ContentTextBox.Text = "";
                return;
            }

            TitleTextBox.Text = item.Title;
            TagsTextBox.Text = string.Join(", ", item.Tags);
            TimestampTextBox.Text = item.Timestamp.ToString("yyyy-MM-dd HH:mm");
            ContentTextBox.Text = item.Content;
        }

        private MemoryItem BuildFromDetails()
        {
            var tags = (TagsTextBox.Text ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new MemoryItem
            {
                Id = _selected?.Id ?? Guid.NewGuid(),
                Title = (TitleTextBox.Text ?? "").Trim(),
                Content = (ContentTextBox.Text ?? "").Trim(),
                Tags = tags,
                Timestamp = _selected?.Timestamp ?? DateTimeOffset.Now
            };
        }

        private void AddMemory_Click(object sender, RoutedEventArgs e)
        {
            LoadDetails(new MemoryItem { Timestamp = DateTimeOffset.Now });
        }

        private void SaveMemory_Click(object sender, RoutedEventArgs e)
        {
            var updated = BuildFromDetails();
            if (string.IsNullOrWhiteSpace(updated.Title) && string.IsNullOrWhiteSpace(updated.Content))
                return;

            var existing = _allItems.FirstOrDefault(i => i.Id == updated.Id);
            if (existing == null)
            {
                _allItems.Add(updated);
            }
            else
            {
                existing.Title = updated.Title;
                existing.Content = updated.Content;
                existing.Tags = updated.Tags;
                // keep timestamp stable for edits in MVP
            }

            RefreshList();
        }

        private void DeleteMemory_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            var existing = _allItems.FirstOrDefault(i => i.Id == _selected.Id);
            if (existing != null) _allItems.Remove(existing);
            LoadDetails(null);
            RefreshList();
        }

        private void MemoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MemoryListBox.SelectedItem is MemoryItem item)
                LoadDetails(item);
        }

        private void MemorySearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshList();
        }

        private void TagFilter_Changed(object sender, RoutedEventArgs e)
        {
            RefreshList();
        }

        private void IncreaseDefaultTopK_Click(object sender, RoutedEventArgs e)
        {
            if (_defaultTopK < 10) _defaultTopK++;
            DefaultTopKText.Text = _defaultTopK.ToString();
        }

        private void DecreaseDefaultTopK_Click(object sender, RoutedEventArgs e)
        {
            if (_defaultTopK > 1) _defaultTopK--;
            DefaultTopKText.Text = _defaultTopK.ToString();
        }
    }
}
