namespace TNovReleaser.Models
{
    internal sealed class FileItem
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public bool Selected { get; set; }
        public string ReleaseNote { get; set; }
    }
}
