using System;
using System.IO;
using System.Reflection;

namespace TNovReleaser.Services
{
    internal static class VersionService
    {
        public static string ReadProductVersion()
        {
            var versionFilePath = Path.Combine(AppConfig.SourceDirectory, AppConfig.VersionFileName);

            if (File.Exists(versionFilePath))
            {
                var version = File.ReadAllText(versionFilePath).Trim();
                if (!string.IsNullOrEmpty(version))
                {
                    return version;
                }
            }

            var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
            return assemblyVersion != null
                ? assemblyVersion.ToString()
                : "0.0.0.0";
        }
    }
}
