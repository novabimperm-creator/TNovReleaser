using System;

namespace FileCopierApp
{
    internal static class AppConfig
    {
        public const string SourceDirectory = @"C:\Source\Files";

        public static readonly string[] OutputDirectories =
        {
            @"C:\Output\Dir1",
            @"C:\Output\Dir2",
            @"C:\Output\Dir3"
        };

        public const string FileNamePrefix = "MyApp_";

        public const string VersionFileName = "version.txt";
        public const string ReleasesFileName = "releases";
        public const string LogFileName = "copy_log.txt";

        public const int MaxCopyAttempts = 3;
        public const int RetryDelayMilliseconds = 500;
    }
}
