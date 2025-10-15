using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using Newtonsoft.Json;

namespace Comfizen;
// --- START: Cache Structure Classes ---

/// <summary>
///     Holds the cached data for a specific model type (e.g., checkpoints).
/// </summary>
public class ModelCacheData
{
    /// <summary>
    ///     Stores the last write time of each monitored folder to detect changes.
    ///     Key: Full path to the monitored folder.
    ///     Value: The folder's last modification time (UTC) when it was scanned.
    /// </summary>
    public Dictionary<string, DateTime> MonitoredFolders { get; set; } = new();

    /// <summary>
    ///     The cached list of relative model file paths.
    /// </summary>
    public List<string> ModelFiles { get; set; } = new();
}

/// <summary>
///     Caches all model types for a specific server instance.
/// </summary>
public class ServerModelCache
{
    /// <summary>
    ///     A dictionary containing cache data for each model type.
    ///     Key: The model type name (e.g., "checkpoints", "loras").
    ///     Value: The cached data for that type.
    /// </summary>
    public Dictionary<string, ModelCacheData> ModelTypes { get; set; } = new();
}

/// <summary>
///     The root object for the entire model cache, supporting multiple servers.
/// </summary>
public class ModelCache
{
    /// <summary>
    ///     A dictionary containing the cache for each server address.
    ///     Key: The server address (e.g., "127.0.0.1:8188").
    ///     Value: The complete cache for that server.
    /// </summary>
    public Dictionary<string, ServerModelCache> Servers { get; set; } = new();
}

// --- END: Cache Structure Classes ---

public class ModelTypeInfo
{
    [JsonProperty("name")] public string Name { get; set; }

    [JsonProperty("folders")] public List<string> Folders { get; set; }
}

public class ModelService
{
    private static readonly HttpClient _httpClient = new();

    // In-memory cache for the current session (fastest access)
    private static readonly ConcurrentDictionary<string, List<ModelTypeInfo>> _modelTypesCache = new();
    private static readonly ConcurrentDictionary<string, List<string>> _modelFilesCache = new();

    // --- START: Persistent Cache Logic ---

    private static readonly string _cacheFilePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "model_cache.json");

    private static ModelCache _persistentCache;
    private static readonly object _cacheLock = new();
    private readonly string _apiBaseUrl;
    private readonly AppSettings _settings;

    /// <summary>
    ///     Static constructor to load the persistent cache from disk when the class is first used.
    /// </summary>
    static ModelService()
    {
        LoadPersistentCache();
    }

    // --- END: Persistent Cache Logic ---

    public ModelService(AppSettings settings)
    {
        _settings = settings;
        _apiBaseUrl = $"http://{_settings.ServerAddress}";
    }

    /// <summary>
    ///     Loads the model cache from the JSON file into memory.
    ///     If the file doesn't exist or is corrupt, a new cache is created.
    /// </summary>
    private static void LoadPersistentCache()
    {
        try
        {
            if (File.Exists(_cacheFilePath))
            {
                var json = File.ReadAllText(_cacheFilePath);
                _persistentCache = JsonConvert.DeserializeObject<ModelCache>(json) ?? new ModelCache();
            }
            else
            {
                _persistentCache = new ModelCache();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error loading model cache: {ex.Message}");
            _persistentCache = new ModelCache();
        }
    }

    /// <summary>
    ///     Saves the current in-memory cache to the JSON file on disk.
    /// </summary>
    private static void SavePersistentCache()
    {
        lock (_cacheLock)
        {
            try
            {
                var json = JsonConvert.SerializeObject(_persistentCache, Formatting.Indented);
                File.WriteAllText(_cacheFilePath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving model cache: {ex.Message}");
            }
        }
    }

    /// <summary>
    ///     Clears both the in-memory and persistent caches.
    ///     This is called when the user manually refreshes the model list.
    /// </summary>
    public static void ClearCache()
    {
        _modelTypesCache.Clear();
        _modelFilesCache.Clear();
        lock (_cacheLock)
        {
            _persistentCache = new ModelCache();
        }

        if (File.Exists(_cacheFilePath))
            try
            {
                File.Delete(_cacheFilePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not delete model cache file: {ex.Message}");
            }
    }

    public async Task<List<ModelTypeInfo>> GetModelTypesAsync()
    {
        if (_modelTypesCache.TryGetValue(_apiBaseUrl, out var cachedTypes)) return cachedTypes;

        try
        {
            var response = await _httpClient.GetStringAsync($"{_apiBaseUrl}/api/experiment/models");
            var types = JsonConvert.DeserializeObject<List<ModelTypeInfo>>(response);
            if (types != null)
            {
                _modelTypesCache.TryAdd(_apiBaseUrl, types);
                return types;
            }
        }
        catch (Exception ex)
        {
            var message = string.Format(LocalizationService.Instance["ModelService_ErrorFetchModelTypes"], ex.Message);
            var title = LocalizationService.Instance["General_Error"];
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return new List<ModelTypeInfo>();
    }

    /// <summary>
    ///     Gets the list of model files for a given type, utilizing a multi-level cache for performance.
    /// </summary>
    /// <param name="modelTypeInfo">The information about the model type to scan.</param>
    /// <returns>A list of relative paths to the model files.</returns>
    public async Task<List<string>> GetModelFilesAsync(ModelTypeInfo modelTypeInfo)
    {
        if (modelTypeInfo == null) return new List<string>();

        var serverKey = _settings.ServerAddress;
        var modelTypeKey = modelTypeInfo.Name;

        // 1. Check in-memory cache (fastest, for current session).
        if (_modelFilesCache.TryGetValue($"{serverKey}_{modelTypeKey}", out var cachedFiles)) return cachedFiles;

        // 2. Check persistent cache and validate it by comparing folder modification times.
        var isCacheValid = false;
        lock (_cacheLock)
        {
            if (_persistentCache.Servers.TryGetValue(serverKey, out var serverCache) &&
                serverCache.ModelTypes.TryGetValue(modelTypeKey, out var cacheData))
            {
                isCacheValid = true;
                // Check if any of the monitored directories have changed since the last scan.
                foreach (var folder in modelTypeInfo.Folders.Where(Directory.Exists))
                    if (!cacheData.MonitoredFolders.TryGetValue(folder, out var lastWriteTime) ||
                        Directory.GetLastWriteTimeUtc(folder) != lastWriteTime)
                    {
                        isCacheValid = false; // A folder was modified, cache is invalid.
                        break;
                    }

                if (isCacheValid)
                {
                    // Cache is valid, use it and populate the in-memory cache for this session.
                    _modelFilesCache.TryAdd($"{serverKey}_{modelTypeKey}", cacheData.ModelFiles);
                    return cacheData.ModelFiles;
                }
            }
        }

        // 3. If cache is invalid or missing, perform a full file scan.
        Debug.WriteLine($"Cache miss or invalid for model type '{modelTypeKey}'. Performing full scan.");
        var tasks = modelTypeInfo.Folders.Select(folder => Task.Run(() =>
        {
            return EnumerateFilesSafely(folder)
                .Where(f => _settings.ModelExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(fullPath => Path.GetRelativePath(folder, fullPath).Replace(Path.DirectorySeparatorChar, '\\'));
        })).ToList();

        var results = await Task.WhenAll(tasks);
        var allFiles = results.SelectMany(files => files).Distinct().OrderBy(f => f).ToList();

        // 4. Update both in-memory and persistent caches with the new scan results.
        _modelFilesCache.TryAdd($"{serverKey}_{modelTypeKey}", allFiles);

        var newCacheData = new ModelCacheData { ModelFiles = allFiles };
        foreach (var folder in modelTypeInfo.Folders)
            if (Directory.Exists(folder))
                // Store the current modification time for future validation.
                newCacheData.MonitoredFolders[folder] = Directory.GetLastWriteTimeUtc(folder);

        lock (_cacheLock)
        {
            if (!_persistentCache.Servers.ContainsKey(serverKey))
                _persistentCache.Servers[serverKey] = new ServerModelCache();
            _persistentCache.Servers[serverKey].ModelTypes[modelTypeKey] = newCacheData;
        }

        SavePersistentCache();

        return allFiles;
    }

    /// <summary>
    ///     Safely enumerates all files in a given path and its subdirectories, ignoring access errors.
    /// </summary>
    /// <param name="path">The root directory to start scanning from.</param>
    /// <returns>An enumerable collection of full file paths.</returns>
    private IEnumerable<string> EnumerateFilesSafely(string path)
    {
        var pathsToSearch = new Stack<string>();

        if (!Directory.Exists(path))
        {
            Debug.WriteLine($"Model search directory not found: '{path}'");
            yield break;
        }

        pathsToSearch.Push(path);

        while (pathsToSearch.Count > 0)
        {
            var currentPath = pathsToSearch.Pop();

            // Enumerate subdirectories and add them to the stack.
            try
            {
                var subDirectories = Directory.EnumerateDirectories(currentPath);
                foreach (var subDirectory in subDirectories) pathsToSearch.Push(subDirectory);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error accessing subdirectories in '{currentPath}': {ex.Message}");
            }

            // Enumerate files in the current directory.
            string[] files = null;
            try
            {
                files = Directory.EnumerateFiles(currentPath).ToArray();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading files in '{currentPath}': {ex.Message}");
            }

            if (files != null)
                foreach (var file in files)
                    yield return file;
        }
    }
}