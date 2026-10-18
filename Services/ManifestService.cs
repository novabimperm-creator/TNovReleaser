using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using TNovReleaser.Models;

namespace TNovReleaser.Services
{
    /// <summary>
    /// Публикация в GitHub Releases и манифесты. В релиз попадают только файлы, чей SHA-256
    /// отличается от записанного в корпоративном манифесте; остальные записи ссылаются на прежние релизы.
    /// </summary>
    internal static class ManifestService
    {
        public sealed class PublishItem
        {
            public PublishItem(string path, string localPath)
            {
                Path = path;
                LocalPath = localPath;
            }

            /// <summary>Путь в манифесте (через «/»).</summary>
            public string Path { get; private set; }
            public string LocalPath { get; private set; }
        }

        /// <summary>Файлы папки как записи манифеста с префиксом (client, addin, 2022/TNov…).</summary>
        public static List<PublishItem> FolderItems(string prefix, string folder)
        {
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException(string.Format("Нет папки {0}", folder));

            return Directory.GetFiles(folder)
                .Where(file => !AppConfig.ExcludedFromManifest.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase))
                .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
                .Select(file => new PublishItem(prefix + "/" + Path.GetFileName(file), file))
                .ToList();
        }

        /// <summary>
        /// Выложить изменённые файлы новым релизом и обновить корпоративный манифест.
        /// replacePrefixes: записи с этими префиксами, которых нет среди items, удаляются
        /// (папка публикуется целиком). false — менять нечего.
        /// </summary>
        public static bool Publish(IList<PublishItem> items, IEnumerable<string> replacePrefixes, string notes, StringBuilder log,
            IList<string> releaseEntries = null, bool writeShareCopies = false)
        {
            Manifest manifest = ReadCorp(log);
            if (manifest == null)
                throw new InvalidOperationException("Корпоративный манифест на GitHub недоступен — выпуск остановлен.");

            // Журнал releases — файл того же манифеста: выпуск не требует доступа к шаре.
            items = new List<PublishItem>(items);
            string journal = null;
            if (releaseEntries != null && releaseEntries.Count > 0)
            {
                journal = BuildReleasesJournal(manifest, releaseEntries, log);
                items.Add(new PublishItem(AppConfig.ReleasesFileName, journal));
            }

            try
            {
                return PublishItems(manifest, items, replacePrefixes, notes, log, writeShareCopies);
            }
            finally
            {
                if (journal != null)
                    File.Delete(journal);
            }
        }

        private static bool PublishItems(Manifest manifest, IList<PublishItem> items, IEnumerable<string> replacePrefixes,
            string notes, StringBuilder log, bool writeShareCopies)
        {

            var changed = new List<KeyValuePair<PublishItem, ManifestFile>>();
            foreach (PublishItem item in items)
            {
                ManifestFile entry = Describe(item);
                ManifestFile existing = manifest.Find(item.Path);
                if (existing != null && string.Equals(existing.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    log.AppendLine(string.Format("  [=] {0} не изменился", item.Path));
                    continue;
                }

                changed.Add(new KeyValuePair<PublishItem, ManifestFile>(item, entry));
            }

            var paths = new HashSet<string>(items.Select(item => item.Path), StringComparer.OrdinalIgnoreCase);
            var prefixes = (replacePrefixes ?? Enumerable.Empty<string>()).Select(prefix => prefix.TrimEnd('/') + "/").ToList();
            List<ManifestFile> removed = manifest.Files
                .Where(file => prefixes.Any(prefix => file.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                               && !paths.Contains(file.Path))
                .ToList();

            if (changed.Count == 0 && removed.Count == 0)
            {
                log.AppendLine("GitHub: изменений нет, релиз не создавался.");
                return false;
            }

            if (changed.Count > 0)
            {
                string tag = UploadRelease(changed, notes, log);
                manifest.Release = tag;
            }

            foreach (KeyValuePair<PublishItem, ManifestFile> pair in changed)
            {
                manifest.Files.RemoveAll(file => string.Equals(file.Path, pair.Value.Path, StringComparison.OrdinalIgnoreCase));
                manifest.Files.Add(pair.Value);
            }

            foreach (ManifestFile file in removed)
            {
                manifest.Files.Remove(file);
                log.AppendLine(string.Format("  [-] {0} удалён из манифеста", file.Path));
            }

            WriteCorp(manifest, writeShareCopies, log);
            return true;
        }

        /// <summary>
        /// Личный манифест на GitHub Pages: корпоративный (с журналом releases) + tnovapi.json.
        /// tnovApiJson не задан — остаётся tnovapi.json, опубликованный раньше.
        /// </summary>
        public static void Promote(string tnovApiJson, StringBuilder log)
        {
            Manifest corp = ReadCorp(log);
            if (corp == null)
                throw new InvalidOperationException("Корпоративный манифест на GitHub недоступен.");
            if (corp.Files.Count == 0)
                throw new InvalidOperationException("Корпоративный манифест пуст — сначала выпустите файлы (или --bootstrap).");

            using (GitHubClient github = CreateClient())
            {
                github.EnsureBranch(AppConfig.PagesBranch);
                string sha;
                string text = github.GetFile(AppConfig.PagesBranch, AppConfig.ManifestFileName, out sha);
                Manifest previous = text != null ? JsonConvert.DeserializeObject<Manifest>(text) : new Manifest();

                var data = new List<PublishItem>();
                if (!string.IsNullOrWhiteSpace(tnovApiJson))
                    data.Add(new PublishItem("tnovapi.json", tnovApiJson));

                var personal = new Manifest
                {
                    Channel = "personal",
                    Release = corp.Release,
                    Generated = DateTime.UtcNow,
                    Files = new List<ManifestFile>(corp.Files)
                };

                var changed = new List<KeyValuePair<PublishItem, ManifestFile>>();
                foreach (PublishItem item in data)
                {
                    ManifestFile entry = Describe(item);
                    ManifestFile existing = previous.Find(item.Path);
                    if (existing != null && string.Equals(existing.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
                        personal.Files.Add(existing);
                    else
                        changed.Add(new KeyValuePair<PublishItem, ManifestFile>(item, entry));
                }

                // tnovapi.json, опубликованный раньше и не переданный сейчас, сохраняем.
                ManifestFile previousApi = previous.Find("tnovapi.json");
                if (previousApi != null && personal.Find("tnovapi.json") == null && data.All(item => item.Path != "tnovapi.json"))
                    personal.Files.Add(previousApi);

                if (changed.Count > 0)
                {
                    UploadRelease(changed, "Данные личной конфигурации", log, github, "p");
                    personal.Files.AddRange(changed.Select(pair => pair.Value));
                }

                personal.Files.Sort((left, right) => string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));
                github.PutFile(AppConfig.PagesBranch, AppConfig.ManifestFileName,
                    JsonConvert.SerializeObject(personal, Formatting.Indented),
                    "Личный манифест: " + (corp.Release ?? "-"), sha);
                log.AppendLine(string.Format("Личный манифест опубликован ({0} файлов): {1}", personal.Files.Count, AppConfig.PersonalManifestUrl));
            }
        }

        private static string UploadRelease(List<KeyValuePair<PublishItem, ManifestFile>> changed, string notes, StringBuilder log)
        {
            using (GitHubClient github = CreateClient())
            {
                return UploadRelease(changed, notes, log, github, "r");
            }
        }

        private static string UploadRelease(List<KeyValuePair<PublishItem, ManifestFile>> changed, string notes, StringBuilder log,
            GitHubClient github, string tagPrefix)
        {
            string tag = tagPrefix + DateTime.Now.ToString("yyyy.MM.dd-HHmmss");
            GitHubClient.Release release = github.CreateRelease(tag, notes);
            log.AppendLine(string.Format("GitHub: релиз {0} ({1})", tag, AppConfig.GitHubRepository));
            try
            {
                foreach (KeyValuePair<PublishItem, ManifestFile> pair in changed)
                {
                    pair.Value.Url = UploadWithRetries(github, release, AssetName(pair.Key.Path), pair.Key.LocalPath);
                    log.AppendLine(string.Format("  [+] {0} ({1})", pair.Key.Path, pair.Value.Version ?? pair.Value.Sha256.Substring(0, 12)));
                }
            }
            catch
            {
                // Манифест не менялся — недокачанный релиз никому не нужен.
                try { github.DeleteRelease(release); }
                catch (Exception ex) { log.AppendLine("  Не удалось удалить неполный релиз: " + ex.Message); }
                throw;
            }

            return tag;
        }

        private static string UploadWithRetries(GitHubClient github, GitHubClient.Release release, string assetName, string localPath)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return github.UploadAsset(release, assetName, localPath);
                }
                catch (Exception) when (attempt < AppConfig.MaxCopyAttempts)
                {
                    System.Threading.Thread.Sleep(AppConfig.RetryDelayMilliseconds * attempt);
                }
            }
        }

        /// <summary>Ассеты релиза без папок: путь кодируется в имени (2022/TNov/TNovBeams.dll → 2022.TNov.TNovBeams.dll).</summary>
        private static string AssetName(string path)
        {
            return Regex.Replace(path.Replace('/', '.'), "[^A-Za-z0-9._-]", "_");
        }

        private static ManifestFile Describe(PublishItem item)
        {
            var info = new FileInfo(item.LocalPath);
            return new ManifestFile
            {
                Path = item.Path,
                Sha256 = ComputeSha256(item.LocalPath),
                Size = info.Length,
                Version = ReadVersion(item.LocalPath)
            };
        }

        private static string ReadVersion(string path)
        {
            string extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
                return null;
            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                string version = !string.IsNullOrWhiteSpace(info.ProductVersion) ? info.ProductVersion : info.FileVersion;
                return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string ComputeSha256(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        /// <summary>
        /// Корпоративный манифест из ветки Pages (через API — без задержки CDN). Его ещё нет —
        /// берём копию с шары (переход со схемы, где манифест лежал только на шарах), иначе пустой.
        /// null — GitHub недоступен: публиковать нельзя, иначе манифест потеряет файлы.
        /// </summary>
        public static Manifest ReadCorp(StringBuilder log)
        {
            string text;
            try
            {
                using (var github = new GitHubClient(AppConfig.GitHubRepository, AppConfig.GitHubToken))
                {
                    string sha;
                    text = github.GetFile(AppConfig.PagesBranch, AppConfig.CorpManifestPagesPath, out sha);
                }
            }
            catch (Exception ex)
            {
                log.AppendLine("Корпоративный манифест на GitHub не прочитан: " + ex.Message);
                return null;
            }

            if (text != null)
                return JsonConvert.DeserializeObject<Manifest>(text) ?? new Manifest();

            foreach (string root in AppConfig.ManifestRoots)
            {
                string path = Path.Combine(root, AppConfig.ManifestFileName);
                try
                {
                    if (File.Exists(path))
                    {
                        log.AppendLine("На GitHub корпоративного манифеста ещё нет — основа: " + path);
                        return JsonConvert.DeserializeObject<Manifest>(File.ReadAllText(path, Encoding.UTF8)) ?? new Manifest();
                    }
                }
                catch (Exception ex)
                {
                    log.AppendLine(string.Format("Манифест {0} не прочитан: {1}", path, ex.Message));
                }
            }

            return new Manifest();
        }

        /// <summary>
        /// Основная копия — ветка Pages. Копии на шарах обновляют клиенты офиса; релизер пишет их
        /// сам только при «Дублировать на шару», чтобы офис получил выпуск сразу.
        /// </summary>
        private static void WriteCorp(Manifest manifest, bool writeShareCopies, StringBuilder log)
        {
            manifest.Channel = "corp";
            manifest.Generated = DateTime.UtcNow;
            manifest.Files.Sort((left, right) => string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));
            string text = JsonConvert.SerializeObject(manifest, Formatting.Indented);

            using (GitHubClient github = CreateClient())
            {
                github.EnsureBranch(AppConfig.PagesBranch);
                string sha;
                github.GetFile(AppConfig.PagesBranch, AppConfig.CorpManifestPagesPath, out sha);
                github.PutFile(AppConfig.PagesBranch, AppConfig.CorpManifestPagesPath, text,
                    "Корпоративный манифест: " + (manifest.Release ?? "-"), sha);
            }

            log.AppendLine(string.Format("Корпоративный манифест опубликован ({0} файлов): {1}", manifest.Files.Count, AppConfig.CorpManifestUrl));
            if (!writeShareCopies)
                return;

            foreach (string root in AppConfig.ManifestRoots)
            {
                string path = Path.Combine(root, AppConfig.ManifestFileName);
                string temp = path + ".tmp";
                try
                {
                    // Через временный файл: клиент не прочтёт половину.
                    File.WriteAllText(temp, text, new UTF8Encoding(false));
                    if (File.Exists(path))
                        File.Replace(temp, path, null);
                    else
                        File.Move(temp, path);
                    log.AppendLine(string.Format("Копия манифеста на шаре: {0}", path));
                }
                catch (Exception ex)
                {
                    log.AppendLine(string.Format("[ОШИБКА] Копия манифеста не записана в {0}: {1}", path, ex.Message));
                }
            }
        }

        /// <summary>
        /// Журнал releases с новыми записями во временном файле: текущий — из манифеста (GitHub),
        /// при первой публикации — с шары.
        /// </summary>
        private static string BuildReleasesJournal(Manifest manifest, IList<string> entries, StringBuilder log)
        {
            string current = string.Empty;
            ManifestFile existing = manifest.Find(AppConfig.ReleasesFileName);
            if (existing != null)
            {
                using (var github = new GitHubClient(AppConfig.GitHubRepository, null))
                {
                    current = new UTF8Encoding(false).GetString(github.Download(existing.Url));
                }
            }
            else
            {
                string share = Path.Combine(AppConfig.ReleaseRootDirectory, AppConfig.ReleasesFileName);
                if (File.Exists(share))
                {
                    current = File.ReadAllText(share, Encoding.UTF8);
                    log.AppendLine("Журнал releases перенесён с шары: " + share);
                }
            }

            current = current.TrimStart('﻿');
            if (current.Length > 0 && !current.EndsWith("\n"))
                current += Environment.NewLine;

            string temp = Path.Combine(Path.GetTempPath(), "TNovReleaser-releases-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(temp, current + string.Join(Environment.NewLine, entries) + Environment.NewLine, new UTF8Encoding(false));
            return temp;
        }

        private static GitHubClient CreateClient()
        {
            string token = AppConfig.GitHubToken;
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException(AppConfig.GitHubTokenHint);
            return new GitHubClient(AppConfig.GitHubRepository, token);
        }
    }
}
