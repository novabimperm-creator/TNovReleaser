using System;
using System.IO;
using TNovReleaser.Models;

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

        public const string ReleaseRootDirectory = @"\\fs-nova\Distr\0.For Admin\_TNov";

        public static readonly string SourceDirectory2027 = Path.Combine(ReleaseRootDirectory, @"build\2027");
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
    }
}
