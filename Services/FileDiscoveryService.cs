using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TNovReleaser.Models;

namespace TNovReleaser.Services
{
    internal static class FileDiscoveryService
    {
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
