using System.Windows;
using System.Windows.Input;

namespace MultiLLMProjectAssistant.UI.Views
{
    public partial class CreateProjectWindow : Window
    {
        public string ProjectName => (NameTextBox.Text ?? "").Trim();
        public string ProjectDescription => (DescriptionTextBox.Text ?? "").Trim();

        public CreateProjectWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => NameTextBox.Focus();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ProjectName))
                return;

            DialogResult = true;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
    }
}

