using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TNovReleaser.Models;
using TNovReleaser.Services;

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

        public bool Release2022
        {
            get { return Release2022CheckBox.IsChecked == true; }
        }

        public bool Release2027
        {
            get { return Release2027CheckBox.IsChecked == true; }
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            FilesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            FilesGrid.CommitEdit();

            if (!Release2022 && !Release2027)
            {
                MessageBox.Show(
                    "Выберите хотя бы одну версию: 2022 или 2027.",
                    "Проверка данных",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

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

            var targets = new List<ReleaseTarget>();
            if (Release2022)
            {
                targets.Add(AppConfig.Target2022);
            }

            if (Release2027)
            {
                targets.Add(AppConfig.Target2027);
            }

            var missing = DependencyCheckService.FindMissingTypes(_files, targets);
            if (missing.Count > 0)
            {
                var answer = MessageBox.Show(
                    "Выбранные файлы используют типы, которых нет в сборках, лежащих в actual, — " +
                    "у пользователей плагин может не запуститься:\n\n" +
                    string.Join("\n", missing) +
                    "\n\nОтметьте эти сборки для выпуска. Выпустить без них всё равно?",
                    "Несовпадение версий",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            DialogResult = true;
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
