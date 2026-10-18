using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace TNovReleaser.Models
{
    /// <summary>
    /// Манифест выпуска: полный список файлов actual\ со ссылками на ассеты GitHub Releases.
    /// Корпоративный лежит на шарах офисов (_TNov\manifest.json), личный — на GitHub Pages.
    /// TNovClient скачивает файлы, чей SHA-256 отличается от локального.
    /// </summary>
    public sealed class Manifest
    {
        public const int CurrentSchema = 1;

        [JsonProperty("schema")]
        public int Schema { get; set; } = CurrentSchema;

        [JsonProperty("channel")]
        public string Channel { get; set; }

        [JsonProperty("release")]
        public string Release { get; set; }

        [JsonProperty("generated")]
        public DateTime Generated { get; set; }

        [JsonProperty("files")]
        public List<ManifestFile> Files { get; set; } = new List<ManifestFile>();

        public ManifestFile Find(string path)
        {
            return Files.Find(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed class ManifestFile
    {
        /// <summary>Путь внутри actual\ через «/»: 2022/TNov/TNovBeams.dll, client/TNovClient.dll, releases.</summary>
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("sha256")]
        public string Sha256 { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("version", NullValueHandling = NullValueHandling.Ignore)]
        public string Version { get; set; }
    }
}
