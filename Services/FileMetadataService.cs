using System;
using System.Diagnostics;
using System.IO;

namespace TNovReleaser.Services
{
    internal static class FileMetadataService
    {
        public static string ReadProductName(string filePath)
        {
            var productName = FileVersionInfo.GetVersionInfo(filePath).ProductName;
            if (string.IsNullOrWhiteSpace(productName))
            {
                throw new InvalidOperationException(
                    string.Format("Не удалось прочитать «Название продукта» для файла: {0}", Path.GetFileName(filePath)));
            }

            return productName.Replace(AppConfig.ProductNamePrefixToRemove, string.Empty).Trim();
        }

        public static string ReadFileVersion(string filePath)
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(filePath);
            var version = !string.IsNullOrWhiteSpace(versionInfo.ProductVersion)
                ? versionInfo.ProductVersion
                : versionInfo.FileVersion;

            if (string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidOperationException(
                    string.Format("Не удалось прочитать версию для файла: {0}", Path.GetFileName(filePath)));
            }

            return version.Trim();
        }
    }
}
