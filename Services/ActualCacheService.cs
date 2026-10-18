using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using TNovReleaser.Models;

namespace TNovReleaser.Services
{
    /// <summary>
    /// Локальная копия выпущенных модулей (TNovBuild\actual\&lt;год&gt;\TNov) по корпоративному манифесту.
    /// С ней сверяется совместимость: actual на шаре больше не обязательно актуален
    /// («Дублировать на шару» выключено по умолчанию).
    /// </summary>
    internal static class ActualCacheService
    {
        /// <summary>
        /// Каталог выпущенных модулей года: кэш, обновлённый по манифесту, если манифест доступен;
        /// без кэша — actual на шаре. null — проверять не с чем.
        /// </summary>
        public static string Resolve(string year, StringBuilder log)
        {
            string cache = AppConfig.ActualCacheDirectory(year);
            try
            {
                Sync(year, cache, log);
            }
            catch (Exception ex)
            {
                log.AppendLine(string.Format("Копия выпущенных модулей {0} не обновлена: {1}", year, ex.Message));
            }

            if (Directory.Exists(cache) && Directory.GetFiles(cache, "*.dll").Length > 0)
                return cache;

            string share = Path.Combine(AppConfig.ShareActualDirectory, year, "TNov");
            return Directory.Exists(share) ? share : null;
        }

        private static void Sync(string year, string cache, StringBuilder log)
        {
            Manifest manifest = ManifestService.ReadCorp(log);
            if (manifest == null || manifest.Files.Count == 0)
            {
                log.AppendLine("Корпоративный манифест недоступен — проверка по прежней копии выпущенных модулей.");
                return;
            }

            string prefix = year + "/TNov/";
            List<ManifestFile> entries = manifest.Files
                .Where(file => file.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                               && file.Path.IndexOf('/', prefix.Length) < 0)
                .ToList();

            Directory.CreateDirectory(cache);
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("TNovReleaser");
                foreach (ManifestFile entry in entries)
                {
                    string local = Path.Combine(cache, entry.Path.Substring(prefix.Length));
                    if (File.Exists(local) && string.Equals(ManifestService.ComputeSha256(local), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string temp = local + ".tmp";
                    byte[] data = Task.Run(() => http.GetByteArrayAsync(entry.Url)).GetAwaiter().GetResult();
                    File.WriteAllBytes(temp, data);
                    if (!string.Equals(ManifestService.ComputeSha256(temp), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(temp);
                        throw new InvalidDataException("хэш не совпал: " + entry.Path);
                    }

                    if (File.Exists(local))
                        File.Delete(local);
                    File.Move(temp, local);
                }
            }

            var names = new HashSet<string>(entries.Select(entry => entry.Path.Substring(prefix.Length)), StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(cache).Where(file => !names.Contains(Path.GetFileName(file))))
                File.Delete(file);
        }
    }
}
