using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TNovReleaser.Models;

namespace TNovReleaser.Services
{
    internal static class ReleaseService
    {
        public static void AppendReleaseEntries(IEnumerable<FileItem> files, string sourceDirectory, StringBuilder log)
        {
            var releasesPath = Path.Combine(AppConfig.ReleaseRootDirectory, AppConfig.ReleasesFileName);
            var releaseDate = DateTime.Now.ToString("yyyy-MM-dd");
            var entries = new List<string>();

            foreach (var file in files)
            {
                if (!file.Selected || file.ReleaseNote == "-")
                {
                    continue;
                }

                var filePath = Path.Combine(sourceDirectory, file.FileName);
                var productName = FileMetadataService.ReadProductName(filePath);
                var fileVersion = FileMetadataService.ReadFileVersion(filePath);
                var entry = string.Format(
                    "{0}/{1}/{2}/{3}",
                    productName,
                    fileVersion,
                    releaseDate,
                    file.ReleaseNote);

                entries.Add(entry);
                log.AppendLine(string.Format("Добавлена запись в releases: {0}", entry));
            }

            if (entries.Count == 0)
            {
                log.AppendLine("Новые записи в releases не добавлялись.");
                return;
            }

            File.AppendAllLines(releasesPath, entries, Encoding.UTF8);
            log.AppendLine(string.Format("Файл releases обновлён: {0}", releasesPath));
        }
    }
}
