using OpenKh.Patcher;
using OpenKh.Tools.ModsManager.Interfaces;
using OpenKh.Tools.ModsManager.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace OpenKh.Tools.ModsManager.Services
{
    public class DownloadableModsService
    {
        /// <summary>
        /// Delegate and event for status updates notification
        /// </summary>
        /// <param name="status"></param>
        public delegate void StatusUpdateHandler(string status);

        /// <summary>
        /// Notify for status updates
        /// </summary>
        public event StatusUpdateHandler OnStatusUpdate;

        /// <summary>
        /// Notify for diagnostic log messages
        /// </summary>
        /// <param name="status"></param>
        public delegate void DiagLogHandler(string status);

        /// <summary>
        /// Notify for diagnostic log messages
        /// </summary>
        public event DiagLogHandler OnDiagLog;

        private const string DownloadableModsJsonUrl = "https://raw.githubusercontent.com/OpenKH/mods-manager-feed/main/downloadable-mods.json";

        private const string ModMetadataFileName = "mod.yml";

        /// <summary>
        /// Different file name variants for better compatibility
        /// </summary>
        private static readonly string[] ModIconFileNames = { "icon.png", "Icon.png", "ICON.png", "Icon.PNG", "icon.PNG" };

        private static readonly string[] ModPreviewFileNames = { "preview.png", "Preview.png", "PREVIEW.png", "Preview.PNG", "preview.PNG" };

        /// <summary>
        /// A cache directory name for cache mechanism of this DownloadableModsService.
        /// This directory will be placed like `%LOCALAPPDATA%/OpenKh/downloadable-mods-cache`
        /// </summary>
        private const string CacheDirectoryName = "downloadable-mods-cache";

        /// <summary>
        /// HTTP request timeout (5 seconds)
        /// </summary>
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Cache of downloadable mods by game
        /// </summary>
        private readonly Dictionary<string, List<DownloadableModModel>> _modsCache = new Dictionary<string, List<DownloadableModModel>>();

        /// <summary>
        /// Cache expiration time (1 day)
        /// </summary>
        private static readonly TimeSpan CacheExpiration = TimeSpan.FromDays(1);

        private static Lazy<DownloadableModsService> _lazyDefault = new Lazy<DownloadableModsService>(
            () => new DownloadableModsService()
        );

        /// <summary>
        /// Provide a singleton default instance
        /// </summary>
        public static DownloadableModsService Default => _lazyDefault.Value;

        /// <summary>
        /// Encodes the repository path to handle special characters in URLs
        /// </summary>
        /// <param name="repositoryPath">Repository path in username/repository format</param>
        /// <returns>URL-safe encoded path</returns>
        private string EncodeRepositoryPath(string repo)
        {
            if (string.IsNullOrEmpty(repo))
                return string.Empty;

            try
            {
                // Encode each part of the repository (username/repository-name) separately
                var parts = repo.Split('/');
                if (parts.Length != 2)
                    return repo;

                string encodedOwner = Uri.EscapeDataString(parts[0]);
                string encodedRepo = Uri.EscapeDataString(parts[1]);

                return $"{encodedOwner}/{encodedRepo}";
            }
            catch (Exception ex)
            {
                OnDiagLog?.Invoke($"Error encoding repository path: {ex.Message}");
                return repo; // In case of error, return the original
            }
        }

        /// <summary>
        /// Attempts to load an image by trying different filename variants and branches
        /// </summary>
        private async Task TryLoadImageWithVariants(
            DownloadableModModel mod,
            string cachePath,
            string encodedRepo,
            string[] fileNameVariants,
            Action<ImageData> setImage,
            CancellationToken cancellationToken = default)
        {
            // Repository branches to try
            string[] branches = { "main", "master" };
            bool success = false;

            foreach (var branch in branches)
            {
                if (success)
                    break;

                foreach (var fileName in fileNameVariants)
                {
                    try
                    {
                        // Build the URL and specific cache path for this combination
                        string url = $"https://raw.githubusercontent.com/{encodedRepo}/{branch}/{fileName}";
                        string specificCachePath = Path.Combine(
                            Path.GetDirectoryName(cachePath),
                            $"{branch}_{fileName}_{Path.GetFileName(cachePath)}");

                        OnDiagLog?.Invoke($"Trying URL: {url}");

                        // Try to load this variant
                        if (await LoadImageWithCache(mod, specificCachePath, url, setImage, cancellationToken))
                        {
                            OnDiagLog?.Invoke($"Success with URL: {url}");
                            success = true;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        OnDiagLog?.Invoke($"Error with variant {fileName} in branch {branch}: {ex.Message}");
                        // Continue with the next variant
                    }
                }
            }

            if (!success)
            {
                OnDiagLog?.Invoke($"Could not load any image variant for {mod.Repo}");
            }
        }

        /// <summary>
        /// Last cache update time by game
        /// </summary>
        private readonly Dictionary<string, DateTime> _lastCacheUpdate = new Dictionary<string, DateTime>();

        /// <summary>
        /// Base directory for file cache
        /// </summary>
        private readonly string _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenKh", CacheDirectoryName);

        public DownloadableModsService(
            string cacheDirectory = null)
        {
            _cacheDirectory = cacheDirectory ?? _cacheDirectory;

            if (!Directory.Exists(_cacheDirectory))
                Directory.CreateDirectory(_cacheDirectory);
        }

        /// <summary>
        /// Get the list of downloadable mods for a specific game
        /// </summary>
        public async Task GetDownloadableModsForGameAsync(
            string gameId,
            Func<DownloadableModModel, Task> emitAsync,
            bool fallbackToLocalCache = true,
            CancellationToken cancellationToken = default)
        {
            OnStatusUpdate?.Invoke("Starting mod loading...");

            await GetDownloadableModsForGameInternalAsync(gameId, emitAsync, fallbackToLocalCache, cancellationToken);
        }

        private async Task GetDownloadableModsForGameInternalAsync(
            string gameId,
            Func<DownloadableModModel, Task> emitAsync,
            bool fallbackToLocalCache = true,
            CancellationToken cancellationToken = default
        )
        {
            // Check if cancellation is requested
            cancellationToken.ThrowIfCancellationRequested();
            OnStatusUpdate?.Invoke("Checking available cache...");

            // Check if we have valid cache
            if (_modsCache.TryGetValue(gameId, out var cachedMods) &&
                _lastCacheUpdate.TryGetValue(gameId, out var lastUpdate) &&
                DateTime.Now - lastUpdate < CacheExpiration)
            {
                OnStatusUpdate?.Invoke("Using cached data (less than 1 hour old)...");

                // Cache is up to date, filter out any mod that's already installed
                var installedMods = ModsService.Mods.ToHashSet();
                var filteredMods = cachedMods
                    .Where(mod => !installedMods.Contains(mod.Repo))
                    .ToList();

                OnStatusUpdate?.Invoke($"Found {filteredMods.Count} available mods in cache");

                foreach (var mod in filteredMods)
                {
                    await emitAsync(mod);
                }
            }

            // No cache or it's expired, load from network

            try
            {
                // Check if cancellation is requested
                cancellationToken.ThrowIfCancellationRequested();

                OnStatusUpdate?.Invoke($"Downloading mod list for {gameId}...");

                // Get the list of available mods from the JSON
                using var client = new HttpClient();
                client.Timeout = RequestTimeout;

                // Use GetAsync with HttpCompletionOption.ResponseHeadersRead for better performance
                using var response = await client.GetAsync(DownloadableModsJsonUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();

                var jsonContent = await response.Content.ReadAsStringAsync();

                // Save JSON to cache
                try
                {
                    OnStatusUpdate?.Invoke("Saving data to cache...");
                    var jsonCachePath = Path.Combine(_cacheDirectory, "downloadable-mods.json");
                    await File.WriteAllTextAsync(jsonCachePath, jsonContent, cancellationToken);
                }
                catch (Exception ex)
                {
                    OnDiagLog?.Invoke($"Error caching JSON: {ex.Message}");
                }

                // Check if cancellation is requested
                cancellationToken.ThrowIfCancellationRequested();

                OnStatusUpdate?.Invoke("Processing server data...");

                using var modData = System.Text.Json.JsonDocument.Parse(jsonContent);

                if (true
                    && modData.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && modData.RootElement.TryGetProperty("mods", out var modsElements)
                    && modsElements.ValueKind == System.Text.Json.JsonValueKind.Object
                    && modsElements.TryGetProperty(gameId, out var gameModsElement)
                    && gameModsElement.ValueKind == System.Text.Json.JsonValueKind.Array
                )
                {
                    // Get list of installed mods to filter
                    var installedMods = ModsService.Mods.ToHashSet();
                    var blacklistedMods = ConfigurationService.BlacklistedMods ?? Enumerable.Empty<string>();
                    var modTasks = new List<Task<DownloadableModModel>>();
                    var allMods = new List<DownloadableModModel>(); // List of all mods (installed or not) for caching

                    int totalMods = gameModsElement.EnumerateArray().Count();

                    OnStatusUpdate?.Invoke($"Found {totalMods} mods for {gameId}. Loading details...");

                    // Check if cancellation is requested
                    cancellationToken.ThrowIfCancellationRequested();

                    var modElements = gameModsElement.EnumerateArray().ToArray();

                    var numEmitted = 0;

                    // Process each mod entry and create tasks for parallel loading
                    foreach (var (modElement, processedMods) in modElements.Select((modElement, processedMods) => (modElement, processedMods)))
                    {
                        // Check if cancellation is requested
                        cancellationToken.ThrowIfCancellationRequested();

                        // Update status before processing batch
                        OnStatusUpdate?.Invoke($"Loading mod details... ({processedMods}/{totalMods})");

                        if (false
                            // skip if repo property is missing or invalid
                            || !modElement.TryGetProperty("repo", out var repoElement)
                            // skip if repo is null or not a string
                            || repoElement.ValueKind != System.Text.Json.JsonValueKind.String
                            || !(repoElement.GetString() is string repo)
                            || repo == null
                        )
                        {
                            continue;
                        }

                        // Create base mod entry for all mods (for caching)
                        var mod = new DownloadableModModel
                        {
                            Repo = repo,
                            Game = gameId
                        };

                        allMods.Add(mod);

                        // Skip already installed mods from the result list but still process for caching
                        bool isInstalled = installedMods.Contains(repo);
                        bool isBlacklisted = blacklistedMods.Contains(repo);

                        // Add task to load this mod
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            // Use file cache for metadata/images
                            await LoadMetadataWithCache(mod, cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            throw; // Re-throw cancellation exceptions
                        }
                        catch (Exception ex)
                        {
                            OnDiagLog?.Invoke($"Error loading mod {repo}: {ex.Message}");
                        }

                        if (isInstalled || isBlacklisted)
                        {
                            continue;
                        }
                        else
                        {
                            await emitAsync(mod);
                            numEmitted += 1;
                        }
                    }

                    // Update cache
                    _modsCache[gameId] = allMods;
                    _lastCacheUpdate[gameId] = DateTime.Now;

                    OnStatusUpdate?.Invoke($"Loading completed. {numEmitted} mods available for installation.");
                }
            }
            catch (Exception ex)
            {
                OnDiagLog?.Invoke($"Error loading downloadable mods: {ex.Message}");

                if (fallbackToLocalCache)
                {
                    // Try to load from local cache in case of network error
                    try
                    {
                        foreach (var it in await GetDownloadableModsLocallyAsync(gameId))
                        {
                            await emitAsync(it);
                        }
                    }
                    catch (Exception cacheEx)
                    {
                        OnDiagLog?.Invoke($"Error loading from cache: {cacheEx.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Get downloadable mods from local cache only
        /// </summary>
        public async Task<List<DownloadableModModel>> GetDownloadableModsLocallyAsync(string gameId)
        {
            var mods = new List<DownloadableModModel>();

            var jsonCachePath = Path.Combine(_cacheDirectory, "downloadable-mods.json");
            if (File.Exists(jsonCachePath))
            {
                var jsonContent = await File.ReadAllTextAsync(jsonCachePath);
                using var modData = System.Text.Json.JsonDocument.Parse(jsonContent);

                var blacklistedMods = ConfigurationService.BlacklistedMods ?? Enumerable.Empty<string>();

                if (true
                    && modData.RootElement.TryGetProperty("mods", out var modsElement)
                    && modsElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && modsElement.TryGetProperty(gameId, out var gameModsElement)
                    && gameModsElement.ValueKind == System.Text.Json.JsonValueKind.Array
                )
                {
                    var installedMods = ModsService.Mods.ToHashSet();
                    foreach (var modElement in gameModsElement
                        .EnumerateArray()
                        .Where(it => it.ValueKind == System.Text.Json.JsonValueKind.Object)
                    )
                    {
                        if (false
                            // skip if repo property is missing or invalid
                            || !modElement.TryGetProperty("repo", out var repoElement)
                            // skip if repo is null or not a string
                            || repoElement.ValueKind != System.Text.Json.JsonValueKind.String
                            || !(repoElement.GetString() is string repo)
                            || repo == null
                            // skip if already installed
                            || installedMods.Contains(repo)
                            // skip if blacklisted
                            || blacklistedMods.Contains(repo)
                        )
                        {
                            continue;
                        }

                        var mod = new DownloadableModModel
                        {
                            Repo = repo,
                            Game = gameId,
                            Title = repo.Split('/').Last(),
                            Description = "Loaded from local cache. Limited information available."
                        };
                        mods.Add(mod);
                    }
                }
            }

            return mods;
        }

        private async Task LoadMetadataWithCache(DownloadableModModel mod, CancellationToken cancellationToken = default)
        {
            try
            {
                // Create cache directory for this specific repository
                string modCacheDir = Path.Combine(_cacheDirectory, mod.Repo.Replace('/', '_'));
                string metadataPath = Path.Combine(modCacheDir, ModMetadataFileName);
                string iconPath = Path.Combine(modCacheDir, ModIconFileNames[0]);
                string previewPath = Path.Combine(modCacheDir, ModPreviewFileNames[0]);

                if (!Directory.Exists(modCacheDir))
                    Directory.CreateDirectory(modCacheDir);

                // Load metadata
                Metadata metadata = null;

                // Try loading from cache first
                if (File.Exists(metadataPath) && (DateTime.Now - File.GetLastWriteTime(metadataPath) < CacheExpiration))
                {
                    try
                    {
                        using var stream = File.OpenRead(metadataPath);
                        metadata = Metadata.Read(stream);
                    }
                    catch (Exception ex)
                    {
                        OnDiagLog?.Invoke($"Error reading cached metadata: {ex.Message}");
                    }
                }

                // If unable to load from cache, load from network
                if (metadata == null)
                {
                    using var client = new HttpClient();
                    client.Timeout = RequestTimeout;

                    // Load metadata file
                    var modYmlUrl = $"https://raw.githubusercontent.com/{mod.Repo}/main/{ModMetadataFileName}";
                    var metadataContent = await client.GetStringAsync(modYmlUrl);

                    // Parse YAML to get metadata
                    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(metadataContent));
                    metadata = Metadata.Read(stream);

                    // Save to cache
                    try
                    {
                        await File.WriteAllTextAsync(metadataPath, metadataContent);
                    }
                    catch (Exception ex)
                    {
                        OnDiagLog?.Invoke($"Error caching metadata: {ex.Message}");
                    }
                }

                // Map metadata to model
                mod.Title = metadata.Title ?? mod.RepoName;
                mod.OriginalAuthor = metadata.OriginalAuthor ?? mod.RepoOwner;
                mod.Description = metadata.Description ?? "No description available.";

                // Encode repository path to handle special characters
                string encodedRepo = EncodeRepositoryPath(mod.Repo);

                // DEBUG: Show all possible paths for debugging
                OnDiagLog?.Invoke($"Original repository: {mod.Repo}");
                OnDiagLog?.Invoke($"Encoded repository: {encodedRepo}");

                // Load the icon by trying different variants
                await TryLoadImageWithVariants(mod, iconPath, encodedRepo, ModIconFileNames, image => mod.IconImage = image, cancellationToken);

                // Load the preview image by trying different variants
                await TryLoadImageWithVariants(mod, previewPath, encodedRepo, ModPreviewFileNames, image => mod.ScreenshotImageSource = image, cancellationToken);
            }
            catch (Exception ex)
            {
                OnDiagLog?.Invoke($"Error loading metadata for {mod.Repo}: {ex.Message}");
                // Set default values if metadata loading fails
                mod.Title = mod.Title ?? mod.RepoName;
                mod.OriginalAuthor = mod.OriginalAuthor ?? mod.RepoOwner;
                mod.Description = mod.Description ?? $"Could not load description for {mod.Repo}. Check your internet connection or try again later.";

                // Create default image if needed
                if (mod.IconImage == null)
                {
                    try
                    {
                        mod.IconImage = GetTextBasedAvatarImageOf(mod.RepoName ?? "?");
                    }
                    catch (Exception exIconImage)
                    {
                        OnDiagLog?.Invoke($"Error creating placeholder: {exIconImage.Message}");
                    }
                }
            }
        }

        internal async Task<bool> LoadImageWithCache(DownloadableModModel mod, string cachePath, string url, Action<ImageData> setImage, CancellationToken cancellationToken = default)
        {
            try
            {
                // Always show a placeholder first
                CreatePlaceholderImage(mod, setImage);

                byte[] imageData = null;
                byte[] staleImageData = null;

                // Extra debugging for URL
                OnDiagLog?.Invoke($"Attempting to load image from URL: {url}");
                OnDiagLog?.Invoke($"Cache path: {cachePath}");

                // Prefer a fresh cache entry and retain stale bytes as an
                // offline fallback if refreshing them fails.
                if (File.Exists(cachePath))
                {
                    try
                    {
                        var cachedBytes = File.ReadAllBytes(cachePath);
                        if (DateTime.Now - File.GetLastWriteTime(cachePath) < CacheExpiration)
                        {
                            imageData = cachedBytes;
                            OnDiagLog?.Invoke($"Image loaded from cache: {cachePath}");
                        }
                        else
                        {
                            staleImageData = cachedBytes;
                            OnDiagLog?.Invoke($"Retaining stale cached image as fallback: {cachePath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        OnDiagLog?.Invoke($"Error reading cached image: {ex.Message}");
                        // Delete corrupt cache file
                        try
                        { File.Delete(cachePath); }
                        catch { }
                    }
                }

                // If not in cache or the cache read failed, download it.
                if (imageData == null)
                {
                    try
                    {
                        using var client = new HttpClient();
                        client.Timeout = TimeSpan.FromSeconds(20); // Longer timeout

                        // Add User-Agent to avoid potential blocks
                        client.DefaultRequestHeaders.Add("User-Agent", "OpenKh-ModManager/1.0");

                        OnDiagLog?.Invoke($"Downloading image from URL: {url}");
                        var response = await client.GetAsync(url, cancellationToken);

                        if (response.IsSuccessStatusCode)
                        {
                            imageData = await response.Content.ReadAsByteArrayAsync();
                            OnDiagLog?.Invoke($"Image downloaded successfully: {imageData.Length} bytes");

                            try
                            {
                                // Create cache directory if it doesn't exist
                                var directory = Path.GetDirectoryName(cachePath);
                                if (!Directory.Exists(directory))
                                {
                                    Directory.CreateDirectory(directory);
                                }

                                // Save to cache
                                File.WriteAllBytes(cachePath, imageData);
                            }
                            catch (Exception ex)
                            {
                                OnDiagLog?.Invoke($"Error saving image to cache: {ex.Message}");
                            }
                        }
                        else
                        {
                            OnDiagLog?.Invoke($"Failed to download image: {response.StatusCode} for {url}");
                        }
                    }
                    catch (Exception ex)
                    {
                        OnDiagLog?.Invoke($"Error downloading image: {ex.Message} for {url}");
                    }
                }

                imageData ??= staleImageData;

                // Keep encoded bytes neutral. Decoding belongs to each frontend.
                if (imageData != null)
                {
                    try
                    {
                        setImage(CreateEncodedImageData(imageData, cachePath, url));
                        OnDiagLog?.Invoke($"Image set successfully to UI");
                        return true;
                    }
                    catch (Exception ex)
                    {
                        OnDiagLog?.Invoke($"Error creating image data: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                OnDiagLog?.Invoke($"Unhandled error in LoadImageWithCache: {ex.Message}");
            }

            return false;
        }

        private void CreatePlaceholderImage(DownloadableModModel mod, Action<ImageData> setImage)
        {
            try
            {
                // Pick a color based on repository name
                byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(mod.Repo ?? "unknown");
                var r = (byte)((nameBytes.Length > 0 ? nameBytes[0] : 100) % 200 + 55);
                var g = (byte)((nameBytes.Length > 1 ? nameBytes[1] : 149) % 200 + 55);
                var b = (byte)((nameBytes.Length > 2 ? nameBytes[2] : 237) % 200 + 55);

                setImage(CreateSolidColorBmp(r, g, b));
            }
            catch (Exception ex)
            {
                OnDiagLog?.Invoke($"Error creating placeholder: {ex.Message}");
            }
        }

        private ImageData GetTextBasedAvatarImageOf(string name) =>
            CreateSolidColorBmp(100, 149, 237);

        private static ImageData CreateEncodedImageData(byte[] bytes, string cachePath, string url)
        {
            var extension = Path.GetExtension(cachePath ?? url)?.ToLowerInvariant();
            var mediaType = extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                _ => "image/png"
            };
            return new ImageData(bytes, ImagePixelFormat.Encoded, mediaType: mediaType);
        }

        // A dependency-free encoded BMP placeholder keeps Core cross-platform.
        // Frontends decode it like downloaded images. Text initials can return
        // later behind a neutral image-rendering service.
        private static ImageData CreateSolidColorBmp(byte r, byte g, byte b)
        {
            const int width = 64;
            const int height = 64;
            const int headerSize = 54;
            var bytes = new byte[headerSize + width * height * 3];
            bytes[0] = (byte)'B';
            bytes[1] = (byte)'M';
            WriteInt32(bytes, 2, bytes.Length);
            WriteInt32(bytes, 10, headerSize);
            WriteInt32(bytes, 14, 40);
            WriteInt32(bytes, 18, width);
            WriteInt32(bytes, 22, height);
            bytes[26] = 1;
            bytes[28] = 24;
            WriteInt32(bytes, 34, width * height * 3);
            for (var offset = headerSize; offset < bytes.Length; offset += 3)
            {
                bytes[offset] = b;
                bytes[offset + 1] = g;
                bytes[offset + 2] = r;
            }
            return new ImageData(bytes, ImagePixelFormat.Encoded, width, height, "image/bmp");
        }

        private static void WriteInt32(byte[] target, int offset, int value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
            target[offset + 2] = (byte)(value >> 16);
            target[offset + 3] = (byte)(value >> 24);
        }
    }
}
