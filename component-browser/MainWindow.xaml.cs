using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace ComponentBrowser
{
    /// <summary>
    /// The browser's single window: pick a release zip, browse its components, drill into a
    /// PME, see its detail. All the real logic lives in <see cref="ReleaseZipLoader"/>,
    /// <see cref="AssemblyInspector"/>, <see cref="ReadmeDocParser"/>, and
    /// <see cref="ComponentCatalogBuilder"/> so it can be unit tested without WPF - this
    /// code-behind only wires events to those services and binds the results.
    /// </summary>
    public partial class MainWindow : Window
    {
        private LoadedRelease _currentRelease;
        private readonly ObservableCollection<ComponentListItem> _components = new ObservableCollection<ComponentListItem>();
        private readonly ObservableCollection<PmeListItem> _pmes = new ObservableCollection<PmeListItem>();

        public MainWindow()
        {
            InitializeComponent();
            ComponentsListBox.ItemsSource = _components;
            PmesListBox.ItemsSource = _pmes;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Release zip (*.zip)|*.zip|All files (*.*)|*.*",
                Title = "Select an AwesomeRpaUtils release zip"
            };
            if (dialog.ShowDialog() == true)
                ZipPathTextBox.Text = dialog.FileName;
        }

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            string path = ZipPathTextBox.Text;
            try
            {
                _currentRelease?.Dispose();
                _currentRelease = null;

                _currentRelease = ReleaseZipLoader.Load(path);
                var catalog = ComponentCatalogBuilder.Build(_currentRelease);

                _components.Clear();
                _pmes.Clear();
                ClearDetail();

                foreach (var entry in catalog)
                    _components.Add(new ComponentListItem(entry));

                StatusText.Text = catalog.Count == 0
                    ? $"Loaded '{Path.GetFileName(path)}', but found no Component-derived types."
                    : $"Loaded {catalog.Count} component(s) from '{Path.GetFileName(path)}'.";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Failed to load: " + ex.Message;
            }
        }

        private void ComponentsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _pmes.Clear();
            ClearDetail();
            if (ComponentsListBox.SelectedItem is ComponentListItem item)
            {
                foreach (var detail in item.Entry.Details)
                    _pmes.Add(new PmeListItem(detail));
            }
        }

        private void PmesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PmesListBox.SelectedItem is PmeListItem item)
            {
                var detail = item.Detail;
                SignatureText.Text = detail.Pme.Signature;
                CategoryText.Text = string.IsNullOrEmpty(detail.Pme.Category) ? "(none)" : detail.Pme.Category;
                DescriptionText.Text = string.IsNullOrEmpty(detail.Description) ? "(no description found)" : detail.Description;
                WorkedExampleText.Text = string.IsNullOrEmpty(detail.WorkedExamplePath) ? "(none)" : detail.WorkedExamplePath;
                NotesText.Text = string.IsNullOrEmpty(detail.NotesAndCaveats) ? "(none)" : detail.NotesAndCaveats;
            }
            else
            {
                ClearDetail();
            }
        }

        private void ClearDetail()
        {
            SignatureText.Text = string.Empty;
            CategoryText.Text = string.Empty;
            DescriptionText.Text = string.Empty;
            WorkedExampleText.Text = string.Empty;
            NotesText.Text = string.Empty;
        }

        protected override void OnClosed(EventArgs e)
        {
            _currentRelease?.Dispose();
            base.OnClosed(e);
        }
    }

    /// <summary>List-box row wrapper for a loaded component.</summary>
    public class ComponentListItem
    {
        public ComponentCatalogEntry Entry { get; }
        public string DisplayName => Entry.Component.AssemblyName;
        public ComponentListItem(ComponentCatalogEntry entry) => Entry = entry;
    }

    /// <summary>List-box row wrapper for a single PME.</summary>
    public class PmeListItem
    {
        public PmeDetail Detail { get; }
        public string DisplayName => $"[{Detail.Pme.Kind}] {Detail.Pme.Name}";
        public PmeListItem(PmeDetail detail) => Detail = detail;
    }
}
