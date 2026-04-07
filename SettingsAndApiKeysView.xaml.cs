using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace MultiLLMProjectAssistant.UI.Views
{
    public partial class SettingsAndApiKeysView : UserControl
    {
        private sealed class ApiKeyItem : INotifyPropertyChanged
        {
            private string _provider = "";
            private string _encrypted = ""; // base64
            private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;

            public Guid Id { get; set; } = Guid.NewGuid();
            public string Provider
            {
                get => _provider;
                set { _provider = value; OnPropertyChanged(nameof(Provider)); OnPropertyChanged(nameof(Title)); }
            }

            public string EncryptedValue
            {
                get => _encrypted;
                set { _encrypted = value; OnPropertyChanged(nameof(EncryptedValue)); OnPropertyChanged(nameof(Masked)); }
            }

            public DateTimeOffset UpdatedAt
            {
                get => _updatedAt;
                set { _updatedAt = value; OnPropertyChanged(nameof(UpdatedAt)); OnPropertyChanged(nameof(Subtitle)); }
            }

            public string Title => string.IsNullOrWhiteSpace(Provider) ? "(Provider)" : Provider;
            public string Subtitle => $"Updated {UpdatedAt:yyyy-MM-dd HH:mm}";
            public string Masked => "••••••••••••••••";

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private sealed class SettingsModel
        {
            public string DefaultProvider { get; set; } = "OpenAI-GPT4";
            public int TimeoutSeconds { get; set; } = 60;
            public int RetryCount { get; set; } = 2;
            public int DefaultTopK { get; set; } = 5;
            public string StorageFolder { get; set; } = "";
            public ApiKeyItem[] ApiKeys { get; set; } = Array.Empty<ApiKeyItem>();
        }

        private readonly ObservableCollection<ApiKeyItem> _keys = new();
        private ICollectionView? _keysView;
        private ApiKeyItem? _selected;
        private bool _isInitialized;
        private readonly string _settingsPath;

        public SettingsAndApiKeysView()
        {
            InitializeComponent();
            _settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MultiLLMProjectAssistant",
                "settings.json");

            BuildKeysTemplate();
            _keysView = CollectionViewSource.GetDefaultView(_keys);
            _keysView.Filter = FilterKey;
            KeysListBox.ItemsSource = _keysView;

            LoadSettings();
            _isInitialized = true;
            RefreshKeys();
            UpdateKeyActions();
        }

        private void BuildKeysTemplate()
        {
            // Card row like other lists; keeps vertical-only scrolling.
            var root = new FrameworkElementFactory(typeof(Border));
            root.SetValue(Border.BackgroundProperty, (Brush)new BrushConverter().ConvertFromString("#2D2D2D")!);
            root.SetValue(Border.BorderBrushProperty, (Brush)new BrushConverter().ConvertFromString("#444444")!);
            root.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            root.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            root.SetValue(Border.PaddingProperty, new Thickness(12, 10, 12, 10));
            root.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 8));

            var stack = new FrameworkElementFactory(typeof(StackPanel));
            stack.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
            root.AppendChild(stack);

            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetBinding(TextBlock.TextProperty, new Binding(nameof(ApiKeyItem.Title)));
            title.SetValue(TextBlock.ForegroundProperty, Brushes.White);
            title.SetValue(TextBlock.FontSizeProperty, 13.0);
            title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
            stack.AppendChild(title);

            var sub = new FrameworkElementFactory(typeof(TextBlock));
            sub.SetBinding(TextBlock.TextProperty, new Binding(nameof(ApiKeyItem.Subtitle)));
            sub.SetValue(TextBlock.ForegroundProperty, (Brush)new BrushConverter().ConvertFromString("#A0A0A0")!);
            sub.SetValue(TextBlock.FontSizeProperty, 12.0);
            sub.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 0));
            stack.AppendChild(sub);

            KeysListBox.ItemTemplate = new DataTemplate { VisualTree = root };
        }

        private bool FilterKey(object obj)
        {
            if (obj is not ApiKeyItem k) return false;
            var q = (KeySearchTextBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(q)) return true;
            return k.Provider.Contains(q, StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshKeys()
        {
            if (!_isInitialized || _keysView == null) return;
            _keysView.Refresh();
        }

        private void LoadSettings()
        {
            SettingsModel model;
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    model = JsonSerializer.Deserialize<SettingsModel>(json) ?? new SettingsModel();
                }
                else
                {
                    model = new SettingsModel();
                }
            }
            catch
            {
                model = new SettingsModel();
            }

            _keys.Clear();
            foreach (var k in model.ApiKeys ?? Array.Empty<ApiKeyItem>())
                _keys.Add(k);

            TimeoutSecondsTextBox.Text = model.TimeoutSeconds.ToString();
            RetryCountTextBox.Text = model.RetryCount.ToString();
            DefaultTopKTextBox.Text = model.DefaultTopK.ToString();
            StorageFolderTextBox.Text = model.StorageFolder ?? "";

            _selected = null;
            KeysListBox.SelectedItem = null;
            ClearKeyEditor();
        }

        private void SaveSettings()
        {
            var model = new SettingsModel
            {
                DefaultProvider = (ProviderComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "OpenAI-GPT4",
                TimeoutSeconds = ParseInt(TimeoutSecondsTextBox.Text, 60),
                RetryCount = ParseInt(RetryCountTextBox.Text, 2),
                DefaultTopK = ParseInt(DefaultTopKTextBox.Text, 5),
                StorageFolder = StorageFolderTextBox.Text ?? "",
                ApiKeys = _keys.ToArray()
            };

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
                var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch
            {
                // ignore MVP
            }
        }

        private static int ParseInt(string? value, int fallback)
        {
            if (int.TryParse(value, out var v)) return v;
            return fallback;
        }

        private static void SetComboSelection(ComboBox combo, string value)
        {
            foreach (var item in combo.Items)
            {
                if (item is ComboBoxItem cbi &&
                    string.Equals(cbi.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = cbi;
                    return;
                }
            }
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private static string EncryptDpapiToBase64(string plaintext)
        {
            var data = Encoding.UTF8.GetBytes(plaintext);
            var protectedBytes = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        private static string DecryptDpapiFromBase64(string base64)
        {
            var protectedBytes = Convert.FromBase64String(base64);
            var data = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }

        private void UpdateKeyActions()
        {
            var hasSelection = _selected != null;
            DeleteKeyButton.IsEnabled = hasSelection;
            SaveKeyButton.IsEnabled = true;
        }

        private void ClearKeyEditor()
        {
            ProviderComboBox.SelectedIndex = 0;
            ApiKeyTextBox.Text = "";
            EncryptionStatusText.Text = "Encrypted at rest (DPAPI)";
        }

        private void LoadKeyToEditor(ApiKeyItem key)
        {
            SetComboSelection(ProviderComboBox, key.Provider);
            ApiKeyTextBox.Text = key.Masked; // keep masked in UI; edit means replace
        }

        private void KeySearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshKeys();

        private void KeysListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selected = KeysListBox.SelectedItem as ApiKeyItem;
            if (_selected == null)
            {
                ClearKeyEditor();
                UpdateKeyActions();
                return;
            }

            LoadKeyToEditor(_selected);
            UpdateKeyActions();
        }

        private void AddKey_Click(object sender, RoutedEventArgs e)
        {
            KeysListBox.SelectedItem = null;
            _selected = null;
            ClearKeyEditor();
            ApiKeyTextBox.Focus();
            UpdateKeyActions();
        }

        private void SaveKey_Click(object sender, RoutedEventArgs e)
        {
            var provider = (ProviderComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(provider)) return;

            // If user didn't change the masked text, keep existing encrypted value.
            var rawInput = (ApiKeyTextBox.Text ?? "").Trim();
            var keepExisting = rawInput.StartsWith("•") && _selected != null;

            if (_selected == null)
            {
                var newItem = new ApiKeyItem
                {
                    Provider = provider,
                    EncryptedValue = keepExisting ? "" : EncryptDpapiToBase64(rawInput),
                    UpdatedAt = DateTimeOffset.Now
                };
                _keys.Insert(0, newItem);
                KeysListBox.SelectedItem = newItem;
            }
            else
            {
                _selected.Provider = provider;
                if (!keepExisting)
                    _selected.EncryptedValue = EncryptDpapiToBase64(rawInput);
                _selected.UpdatedAt = DateTimeOffset.Now;
            }

            SaveSettings();
            RefreshKeys();
            UpdateKeyActions();
        }

        private void DeleteKey_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            _keys.Remove(_selected);
            _selected = null;
            KeysListBox.SelectedItem = null;
            ClearKeyEditor();
            SaveSettings();
            RefreshKeys();
            UpdateKeyActions();
        }

        private void BrowseStorage_Click(object sender, RoutedEventArgs e)
        {
            // Lightweight fallback: select a folder by picking any file in it.
            var dlg = new OpenFileDialog
            {
                Title = "Select a folder (pick any file inside)",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() == true)
                StorageFolderTextBox.Text = Path.GetDirectoryName(dlg.FileName) ?? "";
        }

        private void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_settingsPath))
                    File.Delete(_settingsPath);
            }
            catch
            {
                // ignore
            }
            LoadSettings();
            RefreshKeys();
            UpdateKeyActions();
        }

        private void SaveAllSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }
    }
}
