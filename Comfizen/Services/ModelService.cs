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

public class ModelTypeInfo
{
    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("folders")]
    public List<string> Folders { get; set; }
}

public class ModelService
{
    private readonly string _apiBaseUrl;
    private static readonly HttpClient _httpClient = new HttpClient();
    private readonly AppSettings _settings;

    private static readonly ConcurrentDictionary<string, List<ModelTypeInfo>> _modelTypesCache = new ConcurrentDictionary<string, List<ModelTypeInfo>>();
    private static readonly ConcurrentDictionary<string, List<string>> _modelFilesCache = new ConcurrentDictionary<string, List<string>>();
    
    public static void ClearCache()
    {
        _modelTypesCache.Clear();
        _modelFilesCache.Clear();
    }
    
    // --- НАЧАЛО ИЗМЕНЕНИЯ ---
    public ModelService(AppSettings settings)
    {
        _settings = settings;
        _apiBaseUrl = $"http://{_settings.ServerAddress}";
    }
    // --- КОНЕЦ ИЗМЕНЕНИЯ ---

    public async Task<List<ModelTypeInfo>> GetModelTypesAsync()
    {
        if (_modelTypesCache.TryGetValue(_apiBaseUrl, out var cachedTypes))
        {
            return cachedTypes;
        }

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
            MessageBox.Show($"Failed to fetch model types: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return new List<ModelTypeInfo>();
    }

    public async Task<List<string>> GetModelFilesAsync(ModelTypeInfo modelTypeInfo)
    {
        if (modelTypeInfo == null)
        {
            return new List<string>();
        }

        string cacheKey = modelTypeInfo.Name;
        if (_modelFilesCache.TryGetValue(cacheKey, out var cachedFiles))
        {
            return cachedFiles;
        }

        var tasks = modelTypeInfo.Folders.Select(folder => Task.Run(() =>
        {
            // Используем наш безопасный метод обхода.
            // Он сам обработает все внутренние исключения доступа.
            return EnumerateFilesSafely(folder)
                .Where(f => _settings.ModelExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .Select(fullPath => Path.GetRelativePath(folder, fullPath).Replace(Path.DirectorySeparatorChar, '\\'));

        })).ToList();
    
        // Ожидаем завершения сканирования всех корневых папок.
        var results = await Task.WhenAll(tasks);

        // Собираем все результаты в один список, используя SelectMany для эффективности
        var allFiles = results.SelectMany(files => files).ToList();

        var sortedFiles = allFiles.Distinct().OrderBy(f => f).ToList();
        _modelFilesCache.TryAdd(cacheKey, sortedFiles);
        return sortedFiles;
    }

    private IEnumerable<string> EnumerateFilesSafely(string path)
    {
        var pathsToSearch = new Stack<string>();

        try
        {
            if (!Directory.Exists(path))
            {
                Debug.WriteLine($"Директория для поиска моделей не найдена: '{path}'");
                yield break;
            }

            pathsToSearch.Push(path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ошибка при первоначальном доступе к корневой папке '{path}': {ex}");
            yield break;
        }

        while (pathsToSearch.Count > 0)
        {
            string currentPath = pathsToSearch.Pop();

            // Обработка поддиректорий (здесь нет yield, так что все в порядке)
            try
            {
                var subDirectories = Directory.EnumerateDirectories(currentPath);
                foreach (var subDirectory in subDirectories)
                {
                    pathsToSearch.Push(subDirectory);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка доступа к поддиректориям в '{currentPath}': {ex}");
            }

            // --- ИСПРАВЛЕННЫЙ БЛОК ДЛЯ ФАЙЛОВ ---

            string[] files = null;
            try
            {
                // 1. Пытаемся получить все файлы и материализовать их в массив.
                // Исключение будет поймано здесь.
                files = Directory.EnumerateFiles(currentPath).ToArray();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка чтения файлов в '{currentPath}': {ex}");
            }

            // 2. Если файлы были успешно получены, "отдаем" их наружу.
            // Этот цикл находится ВНЕ блока try-catch.
            if (files != null)
            {
                foreach (var file in files)
                {
                    yield return file;
                }
            }
        }
    }
}