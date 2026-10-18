using System;
using System.IO;
using TNovReleaser.Models;

namespace TNovReleaser
{
    internal static class AppConfig
    {
        /// <summary>
        /// Папка репозиториев (C:\DEV и т.п.): та, где лежит TNovDeploy\TNov.Build.props.
        /// Ищется вверх от exe (TNovReleaser\bin\Release); переопределяется переменной TNOV_DEV_ROOT.
        /// </summary>
        public static string DevRoot
        {
            get
            {
                var fromEnvironment = Environment.GetEnvironmentVariable("TNOV_DEV_ROOT");
                if (!string.IsNullOrWhiteSpace(fromEnvironment))
                    return fromEnvironment;

                for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
                {
                    if (File.Exists(Path.Combine(dir.FullName, @"TNovDeploy\TNov.Build.props")))
                        return dir.FullName;
                }

                throw new DirectoryNotFoundException(
                    "Не найдена папка репозиториев (с TNovDeploy\\TNov.Build.props) над " + AppDomain.CurrentDomain.BaseDirectory +
                    ". Задайте переменную окружения TNOV_DEV_ROOT.");
            }
        }

        /// <summary>Локальная сборка (TNov.Build.props: TNovBuildRoot), переопределяется переменной TNovBuildRoot.</summary>
        public static string BuildRoot
        {
            get
            {
                var fromEnvironment = Environment.GetEnvironmentVariable("TNovBuildRoot");
                return !string.IsNullOrWhiteSpace(fromEnvironment) ? fromEnvironment : Path.Combine(DevRoot, "TNovBuild");
            }
        }

        public static string SourceDirectory
        {
            get { return Path.Combine(BuildRoot, "2022"); }
        }

        public static string SourceDirectory2027
        {
            get { return Path.Combine(BuildRoot, "2027"); }
        }

        /// <summary>Выпущенные модули по корпоративному манифесту — для проверки совместимости (ActualCacheService).</summary>
        public static string ActualCacheDirectory(string year)
        {
            return Path.Combine(BuildRoot, "actual", year, "TNov");
        }

        /// <summary>actual\ на шарах — только для «Дублировать на шару».</summary>
        public static string[] OutputDirectories
        {
            get { return LocalSettings.OutputDirectories; }
        }

        public const string ReleaseRootDirectory = @"\\fs-nova\Distr\0.For Admin\_TNov";

        public const string VostokRootDirectory = @"\\192.168.27.11\nova-vostok\NovaService\_TNov";

        public static readonly string[] OutputDirectories2027 =
        {
            Path.Combine(ReleaseRootDirectory, @"actual\2027\TNov"),
            Path.Combine(VostokRootDirectory, @"actual\2027\TNov")
        };

        public static ReleaseTarget Target2022
        {
            get { return new ReleaseTarget("2022", SourceDirectory, OutputDirectories); }
        }

        public static ReleaseTarget Target2027
        {
            get { return new ReleaseTarget("2027", SourceDirectory2027, OutputDirectories2027); }
        }

        public const string FileNamePrefix = "TNov";

        public const string ProductNamePrefixToRemove = "TNov. ";

        public const string VersionFileName = "version.txt";
        public const string ReleasesFileName = "releases";
        public const string LogFileName = "copy_log.txt";

        public const int MaxCopyAttempts = 3;
        public const int RetryDelayMilliseconds = 500;

        // ===== GitHub Releases и манифесты (откуда обновляется TNovClient) =====

        /// <summary>Публичный репозиторий, в релизы которого выкладываются файлы.</summary>
        public const string GitHubRepository = "novabimperm-creator/TNovDist";

        /// <summary>Ветка GitHub Pages с манифестами (личный — manifest.json, корпоративный — corp/manifest.json).</summary>
        public const string PagesBranch = "gh-pages";

        public const string ManifestFileName = "manifest.json";

        /// <summary>Корпоративный манифест в ветке Pages — основная копия; журнал releases входит в него файлом.</summary>
        public const string CorpManifestPagesPath = "corp/manifest.json";

        /// <summary>Должен совпадать с OfficeServers.PersonalManifestUrl в TNovClient.</summary>
        public const string PersonalManifestUrl = "https://novabimperm-creator.github.io/TNovDist/manifest.json";

        /// <summary>Должен совпадать с OfficeServers.CorpManifestUrl в TNovClient.</summary>
        public const string CorpManifestUrl = "https://novabimperm-creator.github.io/TNovDist/corp/manifest.json";

        /// <summary>
        /// Копии корпоративного манифеста на шарах офисов. Их обновляют сами клиенты (по очереди,
        /// раз в ~10 минут на офис); релизер пишет туда только при «Дублировать на шару».
        /// </summary>
        public static readonly string[] ManifestRoots = { ReleaseRootDirectory, VostokRootDirectory };

        /// <summary>Корень actual\ на шаре — источник для --bootstrap.</summary>
        public static readonly string ShareActualDirectory = Path.Combine(ReleaseRootDirectory, "actual");

        /// <summary>Корпоративный конфиг и установщик из комплекта клиента не публикуются: затёрли бы конфиг пользователя.</summary>
        public static readonly string[] ExcludedFromManifest = { "TNovConfig.json", "install-client.bat" };

        private static readonly string GitHubTokenFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TNovReleaser", "github-token.txt");

        public static string GitHubTokenHint
        {
            get
            {
                return string.Format(
                    "Нет токена GitHub: задайте переменную окружения TNOV_GITHUB_TOKEN или положите токен в {0} " +
                    "(fine-grained, репозиторий {1}, права Contents: Read and write).",
                    GitHubTokenFile,
                    GitHubRepository);
            }
        }

        /// <summary>Токен не храним в LocalSettings: его перезаписывает update.ps1.</summary>
        public static string GitHubToken
        {
            get
            {
                var token = Environment.GetEnvironmentVariable("TNOV_GITHUB_TOKEN");
                if (!string.IsNullOrWhiteSpace(token))
                    return token.Trim();
                return File.Exists(GitHubTokenFile) ? File.ReadAllText(GitHubTokenFile).Trim() : null;
            }
        }
    }
}
