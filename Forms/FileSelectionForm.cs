using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using FileCopierApp.Models;

namespace FileCopierApp.Forms
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

            Text = "Выбор файлов для копирования";
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

            var commentColumn = new DataGridViewTextBoxColumn
            {
                Name = "VersionComment",
                HeaderText = "Комментарий к версии",
                DataPropertyName = "VersionComment",
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 50
            };

            _grid.Columns.AddRange(selectedColumn, fileNameColumn, commentColumn);
            _grid.DataSource = _bindingSource;
            _grid.CurrentCellDirtyStateChanged += OnCurrentCellDirtyStateChanged;
            _grid.CellValueChanged += OnCellValueChanged;
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

            Load += OnFormLoad;
        }

        private void OnFormLoad(object sender, EventArgs e)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var isSelected = row.Cells["Selected"].Value is bool selected && selected;
                row.Cells["VersionComment"].ReadOnly = !isSelected;
            }
        }

        private void OnCurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (_grid.IsCurrentCellDirty)
            {
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
            {
                return;
            }

            if (_grid.Columns[e.ColumnIndex].Name == "Selected")
            {
                var row = _grid.Rows[e.RowIndex];
                var commentCell = row.Cells["VersionComment"];
                commentCell.ReadOnly = !(bool)row.Cells["Selected"].Value;
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

            var filesWithoutComment = selectedFiles
                .Where(file => string.IsNullOrWhiteSpace(file.VersionComment))
                .Select(file => file.FileName)
                .ToList();

            if (filesWithoutComment.Count > 0)
            {
                MessageBox.Show(
                    "Для всех выбранных файлов необходимо указать комментарий к версии:\n" +
                    string.Join("\n", filesWithoutComment),
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
                    VersionComment = string.Empty
                })
                .ToList();
        }
    }
}
