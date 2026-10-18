using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using TNovReleaser.Models;

namespace TNovReleaser.Services
{
    /// <summary>
    /// Выпуск TNovClient: папка сборки клиента (TNovBuild\client\config1 — клиент, Updater,
    /// CreateShortcut.vbs) публикуется целиком под префиксом client вместе с выбранными dll.
    /// </summary>
    internal static class ClientReleaseService
    {
        public const string ManifestPrefix = "client";
        private const string ClientDll = "TNovClient.dll";

        public sealed class ClientInfo
        {
            public Version Built;
            public Version Published;

            /// <summary>Выпускать есть что: сборка новее опубликованной (иначе клиенты её не возьмут).</summary>
            public bool CanRelease
            {
                get { return Built != null && (Published == null || Built.CompareTo(Published) > 0); }
            }

            public string Describe()
            {
                if (Built == null)
                    return string.Format("TNovClient: нет сборки в {0}", AppConfig.ClientBuildDirectory);

                string published = Published != null ? Published.ToString() : "нет";
                string text = string.Format("TNovClient {0} в сборке (опубликован {1})", Built, published);
                if (!CanRelease)
                    text += " — поднимите версию в TNovClient.csproj";
                return text;
            }
        }

        public static ClientInfo Read(StringBuilder log)
        {
            var info = new ClientInfo { Built = ReadVersion(Path.Combine(AppConfig.ClientBuildDirectory, ClientDll)) };
            Manifest manifest = ManifestService.ReadCorp(log);
            ManifestFile published = manifest != null ? manifest.Find(ManifestPrefix + "/" + ClientDll) : null;
            if (published != null)
                info.Published = Parse(published.Version);
            return info;
        }

        /// <summary>
        /// «2.6.0+abc123» (ProductVersion SDK-проекта) и «2.6.0.0» → 2.6.0.0: без нормализации
        /// 2.6.0.0 считался бы новее 2.6.0.
        /// </summary>
        private static Version Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            int suffix = text.IndexOfAny(new[] { '+', '-', ' ' });
            Version version;
            if (!Version.TryParse(suffix >= 0 ? text.Substring(0, suffix) : text, out version))
                return null;
            return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
        }

        public static List<ManifestService.PublishItem> Items()
        {
            return ManifestService.FolderItems(ManifestPrefix, AppConfig.ClientBuildDirectory);
        }

        /// <summary>«Дублировать на шару»: клиенты до 2.6.0 обновляются только из actual\client.</summary>
        public static void CopyToShares(StringBuilder log)
        {
            foreach (string target in AppConfig.ClientShareDirectories)
            {
                try
                {
                    Directory.CreateDirectory(target);
                    foreach (string file in Directory.GetFiles(AppConfig.ClientBuildDirectory))
                    {
                        File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
                    }

                    log.AppendLine(string.Format("  [OK] клиент -> {0}", target));
                }
                catch (Exception ex)
                {
                    log.AppendLine(string.Format("  [ОШИБКА] клиент -> {0}: {1}", target, ex.Message));
                }
            }
        }

        private static Version ReadVersion(string path)
        {
            return File.Exists(path) ? Parse(FileVersionInfo.GetVersionInfo(path).FileVersion) : null;
        }
    }
}
