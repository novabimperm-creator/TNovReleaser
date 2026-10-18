using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TNovReleaser.Models;

namespace TNovReleaser.Services
{
    internal static class ReleaseService
    {
        /// <summary>
        /// Записи журнала releases для выбранных файлов. Журнал хранится в корпоративном манифесте
        /// на GitHub (ManifestService.Publish), на шару пишется только при «Дублировать на шару».
        /// </summary>
        public static List<string> BuildEntries(IEnumerable<FileItem> files, string sourceDirectory, StringBuilder log)
        {
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
                log.AppendLine(string.Format("Запись в releases: {0}", entry));
            }

            if (entries.Count == 0)
            {
                log.AppendLine("Новые записи в releases не добавлялись.");
            }

            return entries;
        }

        /// <summary>Дописать записи в журнал на шаре (старые клиенты, «Дублировать на шару»).</summary>
        public static void AppendToShare(IList<string> entries, StringBuilder log)
        {
            if (entries.Count == 0)
            {
                return;
            }

            var releasesPath = Path.Combine(AppConfig.ReleaseRootDirectory, AppConfig.ReleasesFileName);
            try
            {
                File.AppendAllLines(releasesPath, entries, Encoding.UTF8);
                log.AppendLine(string.Format("Файл releases на шаре обновлён: {0}", releasesPath));
            }
            catch (Exception ex)
            {
                log.AppendLine(string.Format("[ОШИБКА] releases на шаре не обновлён: {0}", ex.Message));
            }
        }
    }
}
