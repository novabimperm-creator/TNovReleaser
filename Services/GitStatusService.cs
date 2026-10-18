using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace TNovReleaser.Services
{
    /// <summary>
    /// Незакоммиченные и неотправленные изменения в репозиториях выпускаемых сборок:
    /// выпуск должен соответствовать коду на GitHub, иначе его не повторить с другой машины.
    /// </summary>
    internal static class GitStatusService
    {
        /// <summary>Не модули плагина и не выпускаются релизером — их состояние не проверяем.</summary>
        private static readonly HashSet<string> NotTracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "TNovAuto", "TNovIssues", "TNovDesktop", "TNovDesktop.Installer", "TNovCloud"
        };

        public sealed class RepoStatus
        {
            public string Name;
            public List<string> Changes = new List<string>();
            public int Unpushed;
        }

        /// <summary>Репозитории (по именам dll) с изменениями; пустой список — всё закоммичено и отправлено.</summary>
        public static List<RepoStatus> Check(IEnumerable<string> fileNames)
        {
            var result = new List<RepoStatus>();
            string devRoot;
            List<string> repos;
            try
            {
                devRoot = AppConfig.DevRoot;
                repos = Directory.GetDirectories(devRoot)
                    .Where(dir => Directory.Exists(Path.Combine(dir, ".git")))
                    .Select(Path.GetFileName)
                    .Where(repo => !NotTracked.Contains(repo))
                    .ToList();
            }
            catch (Exception)
            {
                return result;
            }

            // TNovApi.Client.dll → TNovApi: самое длинное имя репозитория, с которого начинается имя сборки.
            var names = fileNames
                .Select(Path.GetFileNameWithoutExtension)
                .Select(assembly => repos
                    .Where(repo => string.Equals(assembly, repo, StringComparison.OrdinalIgnoreCase)
                                   || assembly.StartsWith(repo + ".", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(repo => repo.Length)
                    .FirstOrDefault())
                .Where(repo => repo != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(repo => repo, StringComparer.OrdinalIgnoreCase);

            foreach (string name in names)
            {
                string path = Path.Combine(devRoot, name);
                string status;
                if (RunGit(path, "status --porcelain", out status) != 0)
                    continue;

                var repoStatus = new RepoStatus { Name = name };
                repoStatus.Changes.AddRange(status
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(line => line.Length > 3)
                    .Select(line => line.Substring(3).Trim('"')));

                // Нет upstream — команда завершится ошибкой, неотправленное не считаем.
                string unpushed;
                int count;
                if (RunGit(path, "rev-list --count @{u}..HEAD", out unpushed) == 0 && int.TryParse(unpushed.Trim(), out count))
                    repoStatus.Unpushed = count;

                if (repoStatus.Changes.Count > 0 || repoStatus.Unpushed > 0)
                    result.Add(repoStatus);
            }

            return result;
        }

        public static string Describe(IEnumerable<RepoStatus> statuses)
        {
            var text = new StringBuilder();
            foreach (RepoStatus status in statuses)
            {
                var parts = new List<string>();
                if (status.Changes.Count > 0)
                {
                    parts.Add(string.Format("незакоммичено {0}: {1}{2}",
                        status.Changes.Count,
                        string.Join(", ", status.Changes.Take(5)),
                        status.Changes.Count > 5 ? ", …" : string.Empty));
                }

                if (status.Unpushed > 0)
                    parts.Add(string.Format("не отправлено на GitHub коммитов: {0}", status.Unpushed));
                text.AppendLine(string.Format("{0} — {1}", status.Name, string.Join("; ", parts)));
            }

            return text.ToString();
        }

        private static int RunGit(string repository, string arguments, out string output)
        {
            output = string.Empty;
            try
            {
                var info = new ProcessStartInfo("git", "-c core.quotepath=off -C \"" + repository + "\" " + arguments)
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (Process process = Process.Start(info))
                {
                    output = process.StandardOutput.ReadToEnd();
                    process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    return process.ExitCode;
                }
            }
            catch (Exception)
            {
                // git не установлен — проверку пропускаем.
                return -1;
            }
        }
    }
}
