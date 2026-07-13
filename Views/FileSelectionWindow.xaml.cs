using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TNovReleaser.Models;

namespace TNovReleaser.Views
{
    public partial class FileSelectionWindow : Window
    {
        private readonly IList<FileItem> _files;

        public FileSelectionWindow(IList<FileItem> files)
        {
            InitializeComponent();
            _files = files;
            FilesGrid.ItemsSource = _files;
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            FilesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            FilesGrid.CommitEdit();

            var selectedFiles = _files.Where(file => file.Selected).ToList();
            if (selectedFiles.Count == 0)
            {
                MessageBox.Show(
                    "Выберите хотя бы один файл.",
                    "Проверка данных",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var filesWithoutNote = selectedFiles
                .Where(file => string.IsNullOrWhiteSpace(file.ReleaseNote))
                .Select(file => file.FileName)
                .ToList();

            if (filesWithoutNote.Count > 0)
            {
                MessageBox.Show(
                    "Для всех выбранных файлов необходимо указать примечание к релизу:\n" +
                    string.Join("\n", filesWithoutNote),
                    "Проверка данных",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
