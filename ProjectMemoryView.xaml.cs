using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MultiLLMProjectAssistant.UI.Models;
using MultiLLMProjectAssistant.UI.Services;

namespace MultiLLMProjectAssistant.UI.Views
{
    public partial class ProjectMemoryView : UserControl
    {
        private readonly IMemoryService _memoryService;
        private readonly ObservableCollection<MemoryNote> _filteredItems = new();
        private MemoryNote? _selected;
        private int _defaultTopK = 5;

        public ProjectMemoryView()
        {
            InitializeComponent();

            var projectPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MultiLLMProjectAssistant");

            Directory.CreateDirectory(projectPath);

            var storage = new FileStorageService(projectPath);
            _memoryService = new MemoryService(storage);

            RefreshList();
            DefaultTopKText.Text = _defaultTopK.ToString();
        }

        private void RefreshList()
        {
            var query = (MemorySearchTextBox?.Text ?? "").Trim();
            var selectedTags = GetSelectedTagFilters();

            List<MemoryNote> items;

            if (!string.IsNullOrWhiteSpace(query))
                items = _memoryService.SearchNotes(query);
            else
                items = _memoryService.GetAllNotes();

            if (selectedTags.Count > 0)
                items = items.Where(i =>
                    selectedTags.All(tag =>
                        i.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
                    .ToList();

            _filteredItems.Clear();
            foreach (var item in items.OrderByDescending(i => i.UpdatedAt))
                _filteredItems.Add(item);

            MemoryListBox.ItemsSource = _filteredItems;
            MemoryListBox.ItemTemplate = BuildItemTemplate();

            if (_selected != null)
            {
                var match = _filteredItems.FirstOrDefault(i => i.Id == _selected.Id);
                if (match != null)
                    MemoryListBox.SelectedItem = match;
            }
        }

        private List<string> GetSelectedTagFilters()
        {
            var tags = new List<string>();
            if (TagApiToggle?.IsChecked == true) tags.Add("api");
            if (TagArchitectureToggle?.IsChecked == true) tags.Add("architecture");
            if (TagDatabaseToggle?.IsChecked == true) tags.Add("database");
            return tags;
        }

        private void LoadDetails(MemoryNote? note)
        {
            _selected = note;
            if (note == null)
            {
                TitleTextBox.Text = "";
                TagsTextBox.Text = "";
                TimestampTextBox.Text = "";
                ContentTextBox.Text = "";
                return;
            }

            TitleTextBox.Text = note.Title;
            TagsTextBox.Text = string.Join(", ", note.Tags);
            TimestampTextBox.Text = note.UpdatedAt.ToString("yyyy-MM-dd HH:mm");
            ContentTextBox.Text = note.Content;
        }

        private void AddMemory_Click(object sender, RoutedEventArgs e)
        {
            MemoryListBox.SelectedItem = null;
            LoadDetails(new MemoryNote());
        }

        private void SaveMemory_Click(object sender, RoutedEventArgs e)
        {
            var title = (TitleTextBox.Text ?? "").Trim();
            var content = (ContentTextBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(content))
                return;

            var tags = (TagsTextBox.Text ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (_selected == null || string.IsNullOrEmpty(_selected.Id))
            {
                _memoryService.AddNote(title, content, tags);
            }
            else
            {
                _memoryService.EditNote(_selected.Id, title, content, tags);
            }

            RefreshList();

            var saved = _memoryService.GetAllNotes()
                .FirstOrDefault(n => n.Title == title && n.Content == content);
            LoadDetails(saved);
        }

        private void DeleteMemory_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null || string.IsNullOrEmpty(_selected.Id)) return;
            _memoryService.DeleteNote(_selected.Id);
            LoadDetails(null);
            RefreshList();
        }

        private void MemoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MemoryListBox.SelectedItem is MemoryNote note)
                LoadDetails(note);
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

        private DataTemplate BuildItemTemplate()
        {
            var template = new DataTemplate(typeof(MemoryNote));
            var sp = new FrameworkElementFactory(typeof(StackPanel));
            sp.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);

            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetBinding(TextBlock.TextProperty, new Binding("Title"));
            title.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            title.SetValue(TextBlock.FontSizeProperty, 14.0);
            title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            title.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            sp.AppendChild(title);

            var content = new FrameworkElementFactory(typeof(TextBlock));
            content.SetBinding(TextBlock.TextProperty, new Binding("Content"));
            content.SetValue(TextBlock.ForegroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D0D0D0")));
            content.SetValue(TextBlock.FontSizeProperty, 12.0);
            content.SetValue(TextBlock.MarginProperty, new Thickness(0, 6, 0, 0));
            content.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            content.SetValue(TextBlock.MaxHeightProperty, 40.0);
            sp.AppendChild(content);

            var meta = new FrameworkElementFactory(typeof(DockPanel));
            meta.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 8, 0, 0));

            var ts = new FrameworkElementFactory(typeof(TextBlock));
            ts.SetBinding(TextBlock.TextProperty,
                new Binding("UpdatedAt") { StringFormat = "yyyy-MM-dd HH:mm" });
            ts.SetValue(TextBlock.ForegroundProperty,
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A0A0A0")));
            ts.SetValue(TextBlock.FontSizeProperty, 11.0);
            ts.SetValue(DockPanel.DockProperty, Dock.Right);
            meta.AppendChild(ts);

            sp.AppendChild(meta);
            template.VisualTree = sp;
            return template;
        }
    }
}