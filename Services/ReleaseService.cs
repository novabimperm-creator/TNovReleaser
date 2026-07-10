using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FileCopierApp.Models;

namespace FileCopierApp.Services
{
    internal static class ReleaseService
    {
        public static void AppendReleaseEntries(IEnumerable<FileItem> files, string productVersion, StringBuilder log)
        {
            var releasesPath = Path.Combine(AppConfig.SourceDirectory, AppConfig.ReleasesFileName);
            var entries = new List<string>();

            foreach (var file in files)
            {
                if (!file.Selected || file.VersionComment == "-")
                {
                    continue;
                }

                var entry = string.Format("{0}={1}={2}", file.FileName, productVersion, file.VersionComment);
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
