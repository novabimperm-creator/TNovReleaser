using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
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
        private static void Main()
        {
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
                    return;
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
                    return;
                }

                var productVersion = VersionService.ReadProductVersion();
                log.AppendLine(string.Format("Версия продукта (version.txt): {0}", productVersion));
                log.AppendLine(string.Format("Исходный каталог: {0}", AppConfig.SourceDirectory));
                log.AppendLine("Выходные каталоги:");
                foreach (var outputDirectory in AppConfig.OutputDirectories)
                {
                    log.AppendLine(string.Format("  - {0}", outputDirectory));
                }

                log.AppendLine();
                ReleaseService.AppendReleaseEntries(files, log);
                log.AppendLine();
                FileCopierService.CopyFiles(files, log);
            }
            catch (Exception ex)
            {
                log.AppendLine(string.Format("Критическая ошибка: {0}", ex.Message));
                log.AppendLine(ex.StackTrace);
            }

            LogService.WriteLog(log);
            WaitForExit();
        }

        private static void WaitForExit()
        {
            Console.WriteLine();
            Console.WriteLine("Нажмите любую клавишу для выхода...");
            Console.ReadKey(true);
        }
    }
}
