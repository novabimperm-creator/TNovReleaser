using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using TNovReleaser.Models;

namespace TNovReleaser.Services
{
    internal static class FileCopierService
    {
        public static void CopyFiles(IEnumerable<FileItem> files, ReleaseTarget target, StringBuilder log)
        {
            foreach (var file in files)
            {
                if (!file.Selected)
                {
                    continue;
                }

                log.AppendLine(string.Format("Копирование файла [{0}]: {1}", target.Name, file.FileName));

                var sourcePath = Path.Combine(target.SourceDirectory, file.FileName);
                if (!File.Exists(sourcePath))
                {
                    log.AppendLine(string.Format("  [ПРОПУСК] Нет файла в сборке {0}: {1}", target.Name, sourcePath));
                    continue;
                }

                foreach (var outputDirectory in target.OutputDirectories)
                {
                    CopyFileWithRetries(sourcePath, outputDirectory, file.FileName, log);
                }
            }
        }

        private static void CopyFileWithRetries(string sourcePath, string outputDirectory, string fileName, StringBuilder log)
        {
            var destinationPath = Path.Combine(outputDirectory, fileName);

            for (var attempt = 1; attempt <= AppConfig.MaxCopyAttempts; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(outputDirectory);
                    File.Copy(sourcePath, destinationPath, true);

                    log.AppendLine(string.Format(
                        "  [OK] {0} -> {1} (попытка {2})",
                        fileName,
                        destinationPath,
                        attempt));

                    return;
                }
                catch (Exception ex)
                {
                    log.AppendLine(string.Format(
                        "  [ОШИБКА] {0} -> {1} (попытка {2}/{3}): {4}",
                        fileName,
                        destinationPath,
                        attempt,
                        AppConfig.MaxCopyAttempts,
                        ex.Message));

                    if (attempt < AppConfig.MaxCopyAttempts)
                    {
                        Thread.Sleep(AppConfig.RetryDelayMilliseconds);
                    }
                }
            }

            log.AppendLine(string.Format(
                "  [ПРОВАЛ] Не удалось скопировать {0} в {1} после {2} попыток.",
                fileName,
                outputDirectory,
                AppConfig.MaxCopyAttempts));
        }
    }
}
