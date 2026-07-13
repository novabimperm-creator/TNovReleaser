using System;
using System.IO;
using System.Text;

namespace TNovReleaser.Services
{
    internal static class LogService
    {
        public static void WriteLog(StringBuilder log)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var header = string.Format("=== Лог копирования ({0}) ===", timestamp);
            var footer = "=== Конец лога ===";

            var fullLog = new StringBuilder();
            fullLog.AppendLine(header);
            fullLog.Append(log);
            fullLog.AppendLine(footer);

            var text = fullLog.ToString();
            Console.WriteLine(text);

            var logPath = Path.Combine(AppConfig.SourceDirectory, AppConfig.LogFileName);
            File.WriteAllText(logPath, text, Encoding.UTF8);
            Console.WriteLine();
            Console.WriteLine(string.Format("Лог сохранён в файл: {0}", logPath));
        }
    }
}
