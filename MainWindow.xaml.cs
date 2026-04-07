using System;
using System.Windows;
using System.Windows.Input;
using MultiLLMProjectAssistant.UI.Views;

namespace MultiLLMProjectAssistant.UI
{
    public static class NavState
    {
        public static readonly DependencyProperty IsSelectedProperty =
            DependencyProperty.RegisterAttached(
                "IsSelected",
                typeof(bool),
                typeof(NavState),
                new PropertyMetadata(false));

        public static bool GetIsSelected(DependencyObject obj) => (bool)obj.GetValue(IsSelectedProperty);
        public static void SetIsSelected(DependencyObject obj, bool value) => obj.SetValue(IsSelectedProperty, value);
    }

    public partial class MainWindow : Window
    {
        private bool issidebarcollapsed = false;

        public bool IsSidebarExpanded
        {
            get { return (bool)GetValue(IsSidebarExpandedProperty); }
            set { SetValue(IsSidebarExpandedProperty, value); }
        }

        public static readonly DependencyProperty IsSidebarExpandedProperty =
            DependencyProperty.Register(nameof(IsSidebarExpanded), typeof(bool), typeof(MainWindow), new PropertyMetadata(true));

        public MainWindow()
        {
            InitializeComponent();
            // Ensure initial sidebar toggle placement matches expanded state
            IsSidebarExpanded = true;
            if (SidebarToggleButton != null)
            {
                SidebarToggleButton.HorizontalAlignment = HorizontalAlignment.Right;
                SidebarToggleButton.Margin = new Thickness(0, 8, 0, 0);
                SidebarToggleButton.Content = "❮";
            }
            NavigateToRequestBuilder();
        }

        public void NavigateToRequestBuilder()
        {
            maincontentarea.Content = new RequestBuilderView();
            SetActiveNav(NavRequestBuilderButton);
        }

        public void NavigateToProjectSelection()
        {
            maincontentarea.Content = new ProjectSelectionView();
            SetActiveNav(NavHomeButton);
        }

        public void NavigateToRequestLog(Guid? projectId = null)
        {
            maincontentarea.Content = new RequestLogView(projectId);
            SetActiveNav(NavRequestLogButton);
        }

        private void SetActiveNav(System.Windows.Controls.Button? button)
        {
            if (button == null) return;
            // Persistent selection: stays highlighted until next nav click.
            NavState.SetIsSelected(NavHomeButton, false);
            NavState.SetIsSelected(NavRequestBuilderButton, false);
            NavState.SetIsSelected(NavProjectMemoryButton, false);
            NavState.SetIsSelected(NavFilesButton, false);
            NavState.SetIsSelected(NavTemplatesButton, false);
            NavState.SetIsSelected(NavRequestLogButton, false);
            NavState.SetIsSelected(NavSettingsButton, false);

            NavState.SetIsSelected(button, true);
        }

        private void togglesidebarclick(object sender, RoutedEventArgs e)
        {
            issidebarcollapsed = !issidebarcollapsed;
            
            // Slim icon-rail when collapsed (still enough to show icons)
            double targetwidth = issidebarcollapsed ? 80 : 320;
            sidebarcolumn.Width = new GridLength(targetwidth);
            apptitletext.Visibility = issidebarcollapsed ? Visibility.Collapsed : Visibility.Visible;

            // Sidebar visual state used by templates (labels + tooltips)
            IsSidebarExpanded = !issidebarcollapsed;

            // Toggle chevron direction
            if (SidebarToggleButton != null)
            {
                SidebarToggleButton.HorizontalAlignment = issidebarcollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Right;
                // keep both chevrons on the same vertical level
                SidebarToggleButton.Margin = new Thickness(0, 8, 0, 0);
                SidebarToggleButton.Content = issidebarcollapsed ? "❯" : "❮";
            }
        }

        private void showrequestbuilderclick(object sender, RoutedEventArgs e)
        {
            NavigateToRequestBuilder();
        }

        private void showprojectmemoryclick(object sender, RoutedEventArgs e)
        {
            maincontentarea.Content = new ProjectMemoryView();
            SetActiveNav(NavProjectMemoryButton);
        }

        private void showfilesclick(object sender, RoutedEventArgs e)
        {
            maincontentarea.Content = new FileManagementView();
            SetActiveNav(NavFilesButton);
        }

        private void showprojectselectionclick(object sender, RoutedEventArgs e)
        {
            NavigateToProjectSelection();
        }

        private void showtemplatesclick(object sender, RoutedEventArgs e)
        {
            maincontentarea.Content = new TaskTemplatesView();
            SetActiveNav(NavTemplatesButton);
        }

        private void showsettingsclick(object sender, RoutedEventArgs e)
        {
            maincontentarea.Content = new SettingsAndApiKeysView();
            SetActiveNav(NavSettingsButton);
        }

        private void showrequestlogclick(object sender, RoutedEventArgs e)
        {
            NavigateToRequestLog();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            try { DragMove(); } catch { /* ignore */ }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
    }
}
