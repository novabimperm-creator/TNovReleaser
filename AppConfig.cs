using System;

namespace TNovReleaser
{
    internal static class AppConfig
    {
        public const string SourceDirectory = @"Z:\DEV\TNovReleaser";

        public static readonly string[] OutputDirectories =
        {
            @"C:\Output\Dir1",
            @"C:\Output\Dir2",
            @"C:\Output\Dir3"
        };

        public const string FileNamePrefix = "TNov";

        public const string ProductNamePrefixToRemove = "TNov. ";

        public const string VersionFileName = "version.txt";
        public const string ReleasesFileName = "releases";
        public const string LogFileName = "copy_log.txt";

        public const int MaxCopyAttempts = 3;
        public const int RetryDelayMilliseconds = 500;
    }
}
