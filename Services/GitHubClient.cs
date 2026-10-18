using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TNovReleaser.Services
{
    /// <summary>
    /// GitHub REST API: релизы с ассетами и файл манифеста в ветке Pages.
    /// Вызовы синхронные (консольный релизер), через Task.Run — без захвата контекста WPF.
    /// </summary>
    internal sealed class GitHubClient : IDisposable
    {
        private const string ApiBase = "https://api.github.com";
        private readonly HttpClient _http;
        private readonly string _repository;

        public GitHubClient(string repository, string token)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            _repository = repository;
            _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("TNovReleaser");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            // Без токена — только чтение публичного репозитория (лимит API 60 запросов в час).
            if (!string.IsNullOrWhiteSpace(token))
                _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        }

        /// <summary>Скачать файл по прямой ссылке (ассет релиза).</summary>
        public byte[] Download(string url)
        {
            return Task.Run(async () =>
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    using (HttpResponseMessage response = await _http.SendAsync(request).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    }
                }
            }).GetAwaiter().GetResult();
        }

        public sealed class Release
        {
            public long Id;
            public string Tag;
            public string UploadUrl;
        }

        public Release CreateRelease(string tag, string body)
        {
            var request = new JObject
            {
                ["tag_name"] = tag,
                ["name"] = tag,
                ["body"] = body ?? string.Empty
            };
            JObject json = Send(HttpMethod.Post, ApiBase + "/repos/" + _repository + "/releases", Json(request));
            var uploadUrl = (string)json["upload_url"];
            int brace = uploadUrl.IndexOf('{');
            return new Release
            {
                Id = (long)json["id"],
                Tag = tag,
                UploadUrl = brace >= 0 ? uploadUrl.Substring(0, brace) : uploadUrl
            };
        }

        /// <summary>Удалить релиз и его тег (откат неудачной публикации).</summary>
        public void DeleteRelease(Release release)
        {
            Send(HttpMethod.Delete, ApiBase + "/repos/" + _repository + "/releases/" + release.Id, null);
            Send(HttpMethod.Delete, ApiBase + "/repos/" + _repository + "/git/refs/tags/" + Uri.EscapeDataString(release.Tag), null);
        }

        /// <summary>Загрузить ассет; возвращает прямую ссылку на скачивание.</summary>
        public string UploadAsset(Release release, string assetName, string filePath)
        {
            var content = new ByteArrayContent(File.ReadAllBytes(filePath));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            JObject json = Send(HttpMethod.Post, release.UploadUrl + "?name=" + Uri.EscapeDataString(assetName), content);
            return (string)json["browser_download_url"];
        }

        /// <summary>Текст файла из ветки (null — файла нет); sha — для последующей перезаписи.</summary>
        public string GetFile(string branch, string path, out string sha)
        {
            sha = null;
            JObject json = Send(HttpMethod.Get,
                ApiBase + "/repos/" + _repository + "/contents/" + path + "?ref=" + Uri.EscapeDataString(branch), null, allowNotFound: true);
            if (json == null)
                return null;
            sha = (string)json["sha"];
            var base64 = ((string)json["content"] ?? string.Empty).Replace("\n", string.Empty);
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }

        public void PutFile(string branch, string path, string text, string message, string sha)
        {
            var request = new JObject
            {
                ["message"] = message,
                ["content"] = Convert.ToBase64String(new UTF8Encoding(false).GetBytes(text)),
                ["branch"] = branch
            };
            if (sha != null)
                request["sha"] = sha;
            Send(HttpMethod.Put, ApiBase + "/repos/" + _repository + "/contents/" + path, Json(request));
        }

        /// <summary>Создать ветку Pages от ветки по умолчанию, если её ещё нет.</summary>
        public void EnsureBranch(string branch)
        {
            if (Send(HttpMethod.Get, ApiBase + "/repos/" + _repository + "/branches/" + branch, null, allowNotFound: true) != null)
                return;

            JObject repo = Send(HttpMethod.Get, ApiBase + "/repos/" + _repository, null);
            var defaultBranch = (string)repo["default_branch"];
            JObject head = Send(HttpMethod.Get, ApiBase + "/repos/" + _repository + "/git/ref/heads/" + defaultBranch, null);
            var request = new JObject
            {
                ["ref"] = "refs/heads/" + branch,
                ["sha"] = (string)head["object"]["sha"]
            };
            Send(HttpMethod.Post, ApiBase + "/repos/" + _repository + "/git/refs", Json(request));
        }

        private static HttpContent Json(JObject body)
        {
            return new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
        }

        private JObject Send(HttpMethod method, string url, HttpContent content, bool allowNotFound = false)
        {
            return Task.Run(async () =>
            {
                using (var request = new HttpRequestMessage(method, url) { Content = content })
                using (HttpResponseMessage response = await _http.SendAsync(request).ConfigureAwait(false))
                {
                    var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                        return null;
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(string.Format(
                            "GitHub {0} {1}: {2} {3}",
                            method,
                            url,
                            (int)response.StatusCode,
                            body.Length > 300 ? body.Substring(0, 300) : body));
                    }

                    return string.IsNullOrWhiteSpace(body) ? new JObject() : JObject.Parse(body);
                }
            }).GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
