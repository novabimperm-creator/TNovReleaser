using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using FileCopierApp.Models;

namespace FileCopierApp.Services
{
    internal static class FileCopierService
    {
        public static void CopyFiles(IEnumerable<FileItem> files, StringBuilder log)
        {
            foreach (var file in files)
            {
                if (!file.Selected)
                {
                    continue;
                }

                log.AppendLine(string.Format("Копирование файла: {0}", file.FileName));

                foreach (var outputDirectory in AppConfig.OutputDirectories)
                {
                    CopyFileWithRetries(file.FullPath, outputDirectory, file.FileName, log);
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
