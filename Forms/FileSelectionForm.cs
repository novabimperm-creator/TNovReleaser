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

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                BackgroundColor = SystemColors.Window,
                EditMode = DataGridViewEditMode.EditOnEnter
            };

            var selectedColumn = new DataGridViewCheckBoxColumn
            {
                Name = "Selected",
                HeaderText = "Выбрать",
                Width = 70,
                TrueValue = true,
                FalseValue = false
            };

            var fileNameColumn = new DataGridViewTextBoxColumn
            {
                Name = "FileName",
                HeaderText = "Файл",
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 50
            };

            var releaseNoteColumn = new DataGridViewTextBoxColumn
            {
                Name = "ReleaseNote",
                HeaderText = "Примечание к релизу",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 50
            };

            _grid.Columns.AddRange(selectedColumn, fileNameColumn, releaseNoteColumn);
            _grid.CellValueChanged += OnCellValueChanged;
            _grid.CellEndEdit += OnCellEndEdit;

            PopulateGrid();

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

        private void PopulateGrid()
        {
            _grid.Rows.Clear();

            foreach (var file in _files)
            {
                var rowIndex = _grid.Rows.Add(false, file.FileName, file.ReleaseNote ?? string.Empty);
                _grid.Rows[rowIndex].Tag = file;
            }
        }

        private void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            SyncRowToModel(e.RowIndex);
        }

        private void OnCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }

            SyncRowToModel(e.RowIndex);
        }

        private void SyncRowToModel(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _grid.Rows.Count)
            {
                return;
            }

            var row = _grid.Rows[rowIndex];
            var file = row.Tag as FileItem;
            if (file == null)
            {
                return;
            }

            var selectedValue = row.Cells["Selected"].Value;
            file.Selected = selectedValue is bool selected && selected;

            var releaseNoteValue = row.Cells["ReleaseNote"].Value;
            file.ReleaseNote = releaseNoteValue == null
                ? string.Empty
                : Convert.ToString(releaseNoteValue);
        }

        private void SyncAllRowsToModel()
        {
            if (_grid.IsCurrentCellInEditMode)
            {
                _grid.EndEdit();
            }

            for (var rowIndex = 0; rowIndex < _grid.Rows.Count; rowIndex++)
            {
                SyncRowToModel(rowIndex);
            }
        }

        private void OnOkClick(object sender, EventArgs e)
        {
            SyncAllRowsToModel();

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
