using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using TNovReleaser.Models;

namespace TNovReleaser.Forms
{
    internal sealed class FileSelectionForm : Form
    {
        private readonly DataGridView _grid;
        private readonly BindingSource _bindingSource;
        private readonly List<FileItem> _files;

        public IReadOnlyList<FileItem> SelectedFiles => _files;

        public FileSelectionForm(IEnumerable<FileItem> files)
        {
            _files = files.ToList();

            Text = "Выбор файлов для релиза";
            StartPosition = FormStartPosition.CenterScreen;
            Width = 900;
            Height = 500;
            MinimumSize = new Size(700, 400);

            _bindingSource = new BindingSource { DataSource = _files };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                BackgroundColor = SystemColors.Window
            };

            var selectedColumn = new DataGridViewCheckBoxColumn
            {
                Name = "Selected",
                HeaderText = "Выбрать",
                DataPropertyName = "Selected",
                Width = 70,
                TrueValue = true,
                FalseValue = false
            };

            var fileNameColumn = new DataGridViewTextBoxColumn
            {
                Name = "FileName",
                HeaderText = "Файл",
                DataPropertyName = "FileName",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 50
            };

            var releaseNoteColumn = new DataGridViewTextBoxColumn
            {
                Name = "ReleaseNote",
                HeaderText = "Примечание к релизу",
                DataPropertyName = "ReleaseNote",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 50
            };

            _grid.Columns.AddRange(selectedColumn, fileNameColumn, releaseNoteColumn);
            _grid.DataSource = _bindingSource;
            _grid.CurrentCellDirtyStateChanged += OnCurrentCellDirtyStateChanged;
            _grid.CellEndEdit += OnCellEndEdit;

            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10),
                Height = 50
            };

            var okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.None,
                Width = 100
            };
            okButton.Click += OnOkClick;

            var cancelButton = new Button
            {
                Text = "Отмена",
                DialogResult = DialogResult.Cancel,
                Width = 100
            };

            buttonPanel.Controls.Add(okButton);
            buttonPanel.Controls.Add(cancelButton);

            Controls.Add(_grid);
            Controls.Add(buttonPanel);

            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        private void OnCurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (_grid.IsCurrentCellDirty)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void OnCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            _bindingSource.EndEdit();
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            _bindingSource.EndEdit();

            var selectedFiles = _files.Where(file => file.Selected).ToList();
            if (selectedFiles.Count == 0)
            {
                MessageBox.Show(
                    "Выберите хотя бы один файл.",
                    "Проверка данных",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
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
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        public static List<FileItem> LoadFilteredFiles()
        {
            if (!Directory.Exists(AppConfig.SourceDirectory))
            {
                throw new DirectoryNotFoundException(
                    string.Format("Исходный каталог не найден: {0}", AppConfig.SourceDirectory));
            }

            return Directory
                .GetFiles(AppConfig.SourceDirectory)
                .Select(Path.GetFileName)
                .Where(fileName =>
                    !string.IsNullOrEmpty(fileName) &&
                    fileName.StartsWith(AppConfig.FileNamePrefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(fileName => fileName)
                .Select(fileName => new FileItem
                {
                    FileName = fileName,
                    FullPath = Path.Combine(AppConfig.SourceDirectory, fileName),
                    Selected = false,
                    ReleaseNote = string.Empty
                })
                .ToList();
        }
    }
}
