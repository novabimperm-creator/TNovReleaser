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

        internal FileSelectionWindow(IList<FileItem> files, ClientReleaseService.ClientInfo client)
        {
            InitializeComponent();
            _files = files;
            FilesGrid.ItemsSource = _files;

            // Клиент выпускается, только если сборка новее опубликованной: клиенты не ставят
            // сборку старше установленной, а старые (до 2.6.0) — не той же версии.
            ReleaseClientCheckBox.Content = client.Describe();
            ReleaseClientCheckBox.IsEnabled = client.CanRelease;
        }

        public bool ReleaseClient
        {
            get { return ReleaseClientCheckBox.IsChecked == true; }
        }

        public bool Release2022
        {
            get { return Release2022CheckBox.IsChecked == true; }
        }

        public bool Release2027
        {
            get { return Release2027CheckBox.IsChecked == true; }
        }

        public bool DuplicateToShare
        {
            get { return DuplicateToShareCheckBox.IsChecked == true; }
        }

        public bool PublishPersonal
        {
            get { return PublishPersonalCheckBox.IsChecked == true; }
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            FilesGrid.CommitEdit(DataGridEditingUnit.Row, true);
            FilesGrid.CommitEdit();

            var selectedFiles = _files.Where(file => file.Selected).ToList();
            if (selectedFiles.Count == 0 && !ReleaseClient)
            {
                MessageBox.Show(
                    "Выберите хотя бы один файл или клиент.",
                    "Проверка данных",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (selectedFiles.Count > 0 && !Release2022 && !Release2027)
            {
                MessageBox.Show(
                    "Выберите хотя бы одну версию: 2022 или 2027.",
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

            var released = selectedFiles.Select(file => file.FileName).ToList();
            if (ReleaseClient)
            {
                released.Add("TNovClient.dll");
            }

            var uncommitted = GitStatusService.Check(released);
            if (uncommitted.Count > 0)
            {
                var gitAnswer = MessageBox.Show(
                    "В репозиториях выпускаемых модулей есть изменения не на GitHub — " +
                    "выпуск будет собран из кода, которого нет в репозитории:\n\n" +
                    GitStatusService.Describe(uncommitted) +
                    "\nЗакоммитьте и отправьте изменения, пересоберите и выпустите заново.\n\nВыпустить всё равно?",
                    "Незакоммиченные изменения",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (gitAnswer != MessageBoxResult.Yes)
                {
                    return;
                }
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

            var check = selectedFiles.Count > 0 ? DependencyCheckService.Check(_files, targets) : new DependencyCheckResult();
            if (check.Problems.Count > 0)
            {
                var hints = new List<string>();
                if (check.ReleaseTogether.Count > 0)
                {
                    hints.Add("Отметьте для выпуска вместе (их версия в build совместима): " + string.Join(", ", check.ReleaseTogether));
                }

                if (check.BrokenSelected.Count > 0)
                {
                    hints.Add("В " + string.Join(", ", check.BrokenSelected) + " удалено или изменено то, что используют другие модули. " +
                              "Лучше вернуть это в коде с [Obsolete], чтобы не пересобирать зависимые модули.");
                }

                var answer = MessageBox.Show(
                    "После выпуска модули будут несовместимы — " +
                    "у пользователей команды или весь плагин упадут (MissingMethodException / TypeLoadException):\n\n" +
                    string.Join("\n", check.Problems.Take(30)) +
                    (check.Problems.Count > 30 ? string.Format("\n… и ещё {0}", check.Problems.Count - 30) : string.Empty) +
                    (hints.Count > 0 ? "\n\n" + string.Join("\n\n", hints) : string.Empty) +
                    "\n\nВыпустить всё равно?",
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
