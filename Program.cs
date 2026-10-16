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

                var productVersion = VersionService.ReadProductVersion();
                log.AppendLine(string.Format("Версия продукта (version.txt): {0}", productVersion));
                foreach (var target in targets)
                {
                    log.AppendLine(string.Format("[{0}] Исходный каталог: {1}", target.Name, target.SourceDirectory));
                    log.AppendLine(string.Format("[{0}] Выходные каталоги:", target.Name));
                    foreach (var outputDirectory in target.OutputDirectories)
                    {
                        log.AppendLine(string.Format("  - {0}", outputDirectory));
                    }
                }

                log.AppendLine();
                ReleaseService.AppendReleaseEntries(files, targets[0].SourceDirectory, log);
                foreach (var target in targets)
                {
                    log.AppendLine();
                    FileCopierService.CopyFiles(files, target, log);
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
                Console.WriteLine("Использование: TNovReleaser.exe --check <каталог build> <каталог actual> [файлы...]");
                return 2;
            }

            var sourceDirectory = args[1];
            var actualDirectory = args[2];
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

        private static void WaitForExit()
        {
            Console.WriteLine();
            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey(true);
        }
    }
}
