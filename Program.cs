using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using TNovReleaser.Models;
using TNovReleaser.Services;
using TNovReleaser.Views;

namespace TNovReleaser
{
    internal static class Program
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--check")
            {
                return CheckMode(args);
            }

            if (args.Length > 0 && args[0] == "--publish")
            {
                return PublishMode(args);
            }

            if (args.Length > 0 && args[0] == "--bootstrap")
            {
                return RunConsole(BootstrapMode);
            }

            if (args.Length > 0 && args[0] == "--promote")
            {
                var tnovApiJson = args.Length > 1 ? args[1] : null;
                return RunConsole(output => ManifestService.Promote(tnovApiJson, output));
            }

            AllocConsole();

            var log = new StringBuilder();

            try
            {
                var files = FileDiscoveryService.LoadFilteredFiles();
                if (files.Count == 0)
                {
                    log.AppendLine("Файлы с заданным префиксом не найдены.");
                    log.AppendLine(string.Format("Каталог: {0}", AppConfig.SourceDirectory));
                    log.AppendLine(string.Format("Префикс: {0}", AppConfig.FileNamePrefix));
                    LogService.WriteLog(log);
                    WaitForExit();
                    return 0;
                }

                var app = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };

                var window = new FileSelectionWindow(files);
                var confirmed = window.ShowDialog() == true;
                app.Shutdown();

                if (!confirmed)
                {
                    log.AppendLine("Операция отменена пользователем.");
                    LogService.WriteLog(log);
                    WaitForExit();
                    return 0;
                }

                var targets = new List<ReleaseTarget>();
                if (window.Release2022)
                {
                    targets.Add(AppConfig.Target2022);
                }

                if (window.Release2027)
                {
                    targets.Add(AppConfig.Target2027);
                }

                // Без токена GitHub выпуск не дойдёт до клиентов — останавливаемся до записи в releases.
                if (string.IsNullOrWhiteSpace(AppConfig.GitHubToken))
                {
                    log.AppendLine(AppConfig.GitHubTokenHint);
                    LogService.WriteLog(log);
                    WaitForExit();
                    return 1;
                }

                var productVersion = VersionService.ReadProductVersion();
                log.AppendLine(string.Format("Версия продукта (version.txt): {0}", productVersion));
                foreach (var target in targets)
                {
                    log.AppendLine(string.Format("[{0}] Исходный каталог: {1}", target.Name, target.SourceDirectory));
                    if (!window.DuplicateToShare)
                    {
                        continue;
                    }

                    log.AppendLine(string.Format("[{0}] Выходные каталоги:", target.Name));
                    foreach (var outputDirectory in target.OutputDirectories)
                    {
                        log.AppendLine(string.Format("  - {0}", outputDirectory));
                    }
                }

                log.AppendLine();
                var entries = ReleaseService.BuildEntries(files, targets[0].SourceDirectory, log);
                if (window.DuplicateToShare)
                {
                    ReleaseService.AppendToShare(entries, log);
                    foreach (var target in targets)
                    {
                        log.AppendLine();
                        FileCopierService.CopyFiles(files, target, log);
                    }
                }

                log.AppendLine();
                var items = new List<ManifestService.PublishItem>();
                foreach (var target in targets)
                {
                    foreach (var file in files.Where(file => file.Selected))
                    {
                        var sourcePath = Path.Combine(target.SourceDirectory, file.FileName);
                        if (File.Exists(sourcePath))
                        {
                            items.Add(new ManifestService.PublishItem(target.Name + "/TNov/" + file.FileName, sourcePath));
                        }
                        else
                        {
                            log.AppendLine(string.Format("[ПРОПУСК] Нет файла в сборке {0}: {1}", target.Name, sourcePath));
                        }
                    }
                }

                ManifestService.Publish(items, null, string.Join("\n", entries), log, entries, window.DuplicateToShare);
                if (window.PublishPersonal)
                {
                    log.AppendLine();
                    ManifestService.Promote(null, log);
                }
            }
            catch (Exception ex)
            {
                log.AppendLine(string.Format("Критическая ошибка: {0}", ex.Message));
                log.AppendLine(ex.StackTrace);
            }

            LogService.WriteLog(log);
            WaitForExit();
            return 0;
        }

        /// <summary>
        /// TNovReleaser.exe --check &lt;каталог build&gt; &lt;каталог actual&gt; [файлы…]
        /// Проверка совместимости без окна (для сборки TNovCommon): файлы из build (по умолчанию все TNov*.dll)
        /// подставляются в actual. Проблемы выводятся в формате предупреждений MSBuild. Код возврата 1 — есть проблемы.
        /// </summary>
        private static int CheckMode(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 3)
            {
                Console.WriteLine("Использование: TNovReleaser.exe --check <каталог build> <год|каталог actual> [файлы...]");
                return 2;
            }

            var sourceDirectory = args[1];
            var actualDirectory = args[2];
            // Год (2022/2027) — сверка с выпущенными модулями по корпоративному манифесту.
            if (System.Text.RegularExpressions.Regex.IsMatch(actualDirectory, @"^\d{4}$"))
            {
                actualDirectory = ActualCacheService.Resolve(actualDirectory, new StringBuilder())
                                  ?? AppConfig.ActualCacheDirectory(actualDirectory);
            }
            if (!Directory.Exists(actualDirectory))
            {
                Console.WriteLine("TNovReleaser : warning TNOV002: проверка совместимости пропущена — нет доступа к {0}", actualDirectory);
                return 0;
            }

            var fileNames = args.Length > 3
                ? args.Skip(3)
                : Directory.GetFiles(sourceDirectory, AppConfig.FileNamePrefix + "*.dll").Select(Path.GetFileName);
            var replacements = DependencyCheckService.Replacements(sourceDirectory, fileNames);

            var problems = CompatibilityChecker.Check(actualDirectory, replacements);
            foreach (var problem in problems)
            {
                Console.WriteLine("TNovReleaser : warning TNOV001: несовместимость с {0}: {1}", actualDirectory, problem);
            }

            if (problems.Count > 0)
            {
                Console.WriteLine("TNovReleaser : warning TNOV001: модули в actual сломаются при выпуске. " +
                                  "Удалённое или изменённое в общей сборке лучше вернуть с [Obsolete].");
                return 1;
            }

            return 0;
        }

        /// <summary>
        /// TNovReleaser.exe --publish &lt;префикс&gt; &lt;папка&gt;
        /// Выложить папку целиком под префиксом манифеста: client, addin, 2022/TNov…
        /// Записи префикса, которых больше нет в папке, удаляются из манифеста.
        /// </summary>
        private static int PublishMode(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Использование: TNovReleaser.exe --publish <client|addin|2022/TNov|2027/TNov> <папка>");
                return 2;
            }

            var prefix = args[1].Replace('\\', '/').Trim('/');
            var folder = args[2];
            return RunConsole(log => ManifestService.Publish(
                ManifestService.FolderItems(prefix, folder),
                new[] { prefix },
                "Публикация " + prefix,
                log));
        }

        /// <summary>
        /// TNovReleaser.exe --bootstrap
        /// Первая публикация: всё содержимое actual\ на шаре (addin, 2022, 2027, client) → GitHub и манифест.
        /// </summary>
        private static void BootstrapMode(StringBuilder log)
        {
            var parts = new[]
            {
                new KeyValuePair<string, string>("addin", Path.Combine(AppConfig.ShareActualDirectory, "addin")),
                new KeyValuePair<string, string>("2022/TNov", Path.Combine(AppConfig.ShareActualDirectory, @"2022\TNov")),
                new KeyValuePair<string, string>("2027/TNov", Path.Combine(AppConfig.ShareActualDirectory, @"2027\TNov")),
                new KeyValuePair<string, string>("client", Path.Combine(AppConfig.ShareActualDirectory, "client"))
            };

            var items = new List<ManifestService.PublishItem>();
            var prefixes = new List<string>();
            foreach (var part in parts)
            {
                if (!Directory.Exists(part.Value))
                {
                    log.AppendLine(string.Format("[ПРОПУСК] Нет папки {0}", part.Value));
                    continue;
                }

                items.AddRange(ManifestService.FolderItems(part.Key, part.Value));
                prefixes.Add(part.Key);
            }

            // Журнал релизов переезжает с шары в манифест.
            var releases = Path.Combine(AppConfig.ReleaseRootDirectory, AppConfig.ReleasesFileName);
            if (File.Exists(releases))
            {
                items.Add(new ManifestService.PublishItem(AppConfig.ReleasesFileName, releases));
            }

            ManifestService.Publish(items, prefixes, "Первая публикация из " + AppConfig.ShareActualDirectory, log,
                writeShareCopies: true);
        }

        private static int RunConsole(Action<StringBuilder> action)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var log = new StringBuilder();
            var code = 0;
            try
            {
                action(log);
            }
            catch (Exception ex)
            {
                log.AppendLine(string.Format("Ошибка: {0}", ex.Message));
                code = 1;
            }

            Console.WriteLine(log.ToString());
            return code;
        }

        private static void WaitForExit()
        {
            Console.WriteLine();
            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey(true);
        }
    }
}
