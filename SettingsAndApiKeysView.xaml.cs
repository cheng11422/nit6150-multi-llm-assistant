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
using MultiLLMProjectAssistant.UI;

namespace MultiLLMProjectAssistant.UI.Views
{
    public partial class SettingsAndApiKeysView : UserControl
    {
        private sealed class ApiKeyItem : INotifyPropertyChanged
        {
            private const string MaskValue = "****************";

            private string _provider = "";
            private string _encrypted = "";
            private string _sharedValue = "";
            private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;

            public Guid Id { get; set; } = Guid.NewGuid();

            public string Provider
            {
                get => _provider;
                set
                {
                    _provider = value;
                    OnPropertyChanged(nameof(Provider));
                    OnPropertyChanged(nameof(Title));
                }
            }

            public string EncryptedValue
            {
                get => _encrypted;
                set
                {
                    _encrypted = value;
                    OnPropertyChanged(nameof(EncryptedValue));
                }
            }

            public string SharedValue
            {
                get => _sharedValue;
                set
                {
                    _sharedValue = value;
                    OnPropertyChanged(nameof(SharedValue));
                    OnPropertyChanged(nameof(IsShared));
                    OnPropertyChanged(nameof(Subtitle));
                }
            }

            public DateTimeOffset UpdatedAt
            {
                get => _updatedAt;
                set
                {
                    _updatedAt = value;
                    OnPropertyChanged(nameof(UpdatedAt));
                    OnPropertyChanged(nameof(Subtitle));
                }
            }

            public string Title => string.IsNullOrWhiteSpace(Provider) ? "(Provider)" : Provider;
            public bool IsShared => !string.IsNullOrWhiteSpace(SharedValue);
            public string Subtitle => IsShared
                ? $"Updated {UpdatedAt:yyyy-MM-dd HH:mm} - Shared"
                : $"Updated {UpdatedAt:yyyy-MM-dd HH:mm} - Local";
            public string Masked => MaskValue;

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
            _settingsPath = AppDataPaths.GetDataFile("settings.json");

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
            if (obj is not ApiKeyItem key) return false;
            var query = (KeySearchTextBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(query)) return true;
            return key.Provider.Contains(query, StringComparison.OrdinalIgnoreCase);
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
            foreach (var key in model.ApiKeys ?? Array.Empty<ApiKeyItem>())
                _keys.Add(key);

            TimeoutSecondsTextBox.Text = model.TimeoutSeconds.ToString();
            RetryCountTextBox.Text = model.RetryCount.ToString();
            DefaultTopKTextBox.Text = model.DefaultTopK.ToString();
            StorageFolderTextBox.Text = string.IsNullOrWhiteSpace(model.StorageFolder)
                ? AppDataPaths.DataFolder
                : model.StorageFolder;

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
                // Keep the settings screen usable even if the save fails.
            }
        }

        private static int ParseInt(string? value, int fallback)
        {
            if (int.TryParse(value, out var parsed)) return parsed;
            return fallback;
        }

        private static void SetComboSelection(ComboBox combo, string value)
        {
            foreach (var item in combo.Items)
            {
                if (item is ComboBoxItem comboItem &&
                    string.Equals(comboItem.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = comboItem;
                    return;
                }
            }

            if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;
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

        private static string? TryGetStoredPlaintext(ApiKeyItem item)
        {
            if (item.IsShared)
                return item.SharedValue;

            if (string.IsNullOrWhiteSpace(item.EncryptedValue))
                return null;

            try
            {
                return DecryptDpapiFromBase64(item.EncryptedValue);
            }
            catch
            {
                return null;
            }
        }

        private void UpdateKeyActions()
        {
            DeleteKeyButton.IsEnabled = _selected != null;
            SaveKeyButton.IsEnabled = true;
        }

        private void ClearKeyEditor()
        {
            ProviderComboBox.SelectedIndex = 0;
            ApiKeyTextBox.Text = "";
            ShareKeyCheckBox.IsChecked = false;
            UpdateEncryptionStatusText();
        }

        private void LoadKeyToEditor(ApiKeyItem key)
        {
            SetComboSelection(ProviderComboBox, key.Provider);
            ApiKeyTextBox.Text = key.Masked;
            ShareKeyCheckBox.IsChecked = key.IsShared;
            UpdateEncryptionStatusText();
        }

        private void UpdateEncryptionStatusText()
        {
            var shared = ShareKeyCheckBox.IsChecked == true;
            EncryptionStatusText.Text = shared
                ? "Shared in settings.json as plain text for team access"
                : "Encrypted at rest (DPAPI, current Windows user only)";
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
            if (string.IsNullOrWhiteSpace(provider))
                return;

            var storeAsShared = ShareKeyCheckBox.IsChecked == true;
            var rawInput = (ApiKeyTextBox.Text ?? "").Trim();
            var isMaskedEditorValue = rawInput == "****************" && _selected != null;
            string? plaintext = rawInput;

            if (isMaskedEditorValue && _selected != null)
            {
                if (storeAsShared == _selected.IsShared)
                {
                    plaintext = null;
                }
                else
                {
                    plaintext = TryGetStoredPlaintext(_selected);
                    if (string.IsNullOrWhiteSpace(plaintext))
                    {
                        MessageBox.Show(
                            "Paste the API key again before switching it between local encrypted mode and shared mode.",
                            "API Key Required",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }
                }
            }

            if (_selected == null)
            {
                var newItem = new ApiKeyItem
                {
                    Provider = provider,
                    UpdatedAt = DateTimeOffset.Now
                };
                ApplyKeyStorageMode(newItem, plaintext, storeAsShared);
                _keys.Insert(0, newItem);
                KeysListBox.SelectedItem = newItem;
            }
            else
            {
                _selected.Provider = provider;
                ApplyKeyStorageMode(_selected, plaintext, storeAsShared);
                _selected.UpdatedAt = DateTimeOffset.Now;
            }

            SaveSettings();
            RefreshKeys();
            UpdateKeyActions();
        }

        private static void ApplyKeyStorageMode(ApiKeyItem item, string? plaintext, bool storeAsShared)
        {
            if (plaintext == null)
                return;

            if (storeAsShared)
            {
                item.SharedValue = plaintext;
                item.EncryptedValue = "";
            }
            else
            {
                item.EncryptedValue = string.IsNullOrWhiteSpace(plaintext) ? "" : EncryptDpapiToBase64(plaintext);
                item.SharedValue = "";
            }
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

        private void ShareKeyCheckBox_Click(object sender, RoutedEventArgs e)
        {
            UpdateEncryptionStatusText();
        }
    }
}
