using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TNovReleaser.Models
{
    public sealed class FileItem : INotifyPropertyChanged
    {
        private bool _selected;
        private string _releaseNote = string.Empty;

        public string FileName { get; set; }
        public string FullPath { get; set; }

        public bool Selected
        {
            get { return _selected; }
            set
            {
                if (_selected == value)
                {
                    return;
                }

                _selected = value;
                OnPropertyChanged();
            }
        }

        public string ReleaseNote
        {
            get { return _releaseNote; }
            set
            {
                if (_releaseNote == value)
                {
                    return;
                }

                _releaseNote = value ?? string.Empty;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
