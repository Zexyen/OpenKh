using OpenKh.Tools.ModsManager.Interfaces;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace OpenKh.Tools.ModsManager.Services
{
    public sealed class SetupWizardFileSystem : ISetupWizardFileSystem
    {
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public IEnumerable<string> EnumerateFiles(string path, string pattern) => Directory.EnumerateFiles(path, pattern);
        public string ReadAllText(string path) => File.ReadAllText(path);
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken) => File.ReadAllTextAsync(path, cancellationToken);
        public void WriteAllText(string path, string content) => File.WriteAllText(path, content);
        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken) => File.WriteAllTextAsync(path, content, cancellationToken);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public void CopyFile(string source, string destination, bool overwrite) => File.Copy(source, destination, overwrite);
        public void MoveFile(string source, string destination, bool overwrite) => File.Move(source, destination, overwrite);
        public void DeleteFile(string path) => File.Delete(path);
        public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
        public Stream OpenRead(string path) => File.OpenRead(path);
    }

    public sealed class GitHubLuaBackendReleaseSource : ILuaBackendReleaseSource, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;

        public GitHubLuaBackendReleaseSource(HttpClient httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            _ownsHttpClient = httpClient == null;
            if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("OpenKh-ModsManager");
        }

        public async Task<LuaBackendRelease> GetLatestAsync(CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync(
                "https://api.github.com/repos/Sirius902/LuaBackend/releases/latest",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var json = JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var assets = json["assets"]?.Children().ToArray() ?? Array.Empty<JToken>();
            var asset = assets.FirstOrDefault(value => ((string)value["name"])?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
                ?? assets.FirstOrDefault();
            if (asset == null)
                return null;
            return new LuaBackendRelease((string)json["tag_name"], new Uri((string)asset["browser_download_url"]), (string)asset["name"]);
        }

        public async Task DownloadAsync(Uri uri, string destinationPath, CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            await source.CopyToAsync(destination, cancellationToken);
        }

        public void Dispose()
        {
            if (_ownsHttpClient)
                _httpClient.Dispose();
        }
    }

    public sealed class SteamProtonConfigRepository : IProtonConfigRepository
    {
        public bool IsSteamRunning => Process.GetProcessesByName("steam").Length > 0;
        public IReadOnlyList<string> GetConfigurationFiles() => SteamService.FindLocalConfigFiles().ToList();
        public string Read(string path) => File.ReadAllText(path);
        public void BackupAndWrite(string path, string content)
        {
            File.Copy(path, path + ".openkh.bak", true);
            File.WriteAllText(path, content);
        }
    }
}
