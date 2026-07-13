using System;

namespace TNovReleaser
{
    internal static class AppConfig
    {
        public static string SourceDirectory
        {
            get { return LocalSettings.SourceDirectory; }
        }

        public static string[] OutputDirectories
        {
            get { return LocalSettings.OutputDirectories; }
        }

        public const string FileNamePrefix = "TNov";

        public const string ProductNamePrefixToRemove = "TNov. ";

        public const string VersionFileName = "version.txt";
        public const string ReleasesFileName = "releases";
        public const string LogFileName = "copy_log.txt";

        public const int MaxCopyAttempts = 3;
        public const int RetryDelayMilliseconds = 500;
    }
}
