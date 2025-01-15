using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ChangeLogTracker.Core.Interfaces;

namespace ChangeLogTracker.Database
{
    /// <summary>
    /// A Firebase Realtime Database, but it stores all the json objects locally in a file structure.
    /// If an entry doesnt exist, it will grab it from Firebase.
    /// If it does exist, and the last time this entry was accessed was X minutes ago, then it will just use the local version.
    /// </summary>
    public class CachedDatabase : IDatabase
    {
        private static readonly SemaphoreSlim _SemaphoreSlim = new SemaphoreSlim(1, 1);

        private string _baseDirectory = "CachedDatabase";

        public Dictionary<string, Dictionary<string, object>> CachedCollections { get; set; } = new();

        private class OfflineObject
        {
            public DateTime LastAccessed;
            public string Key;
            public string Json;
        }

        public async Task Init(string localCacheName = "CachedDatabase")
        {
            _baseDirectory = localCacheName;
            Directory.CreateDirectory($"{_baseDirectory}{Path.DirectorySeparatorChar}");
        }

        public async Task<T> GetAsync<T>(string path) where T : class
        {
            await _SemaphoreSlim.WaitAsync();
            try
            {
                string? json = null;
                var offlinePath = $"{_baseDirectory}{Path.DirectorySeparatorChar}{path}.json";

                //If the offline file exists, use it
                if (File.Exists(offlinePath))
                {
                    json = await File.ReadAllTextAsync(offlinePath);
                }

                var offlineObj = JsonConvert.DeserializeObject<OfflineObject>(json);

                return JsonConvert.DeserializeObject<T>(offlineObj.Json, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                });
            }
            catch (Exception e)
            {
                return default;
            }
            finally
            {
                _SemaphoreSlim.Release();
            }
        }

        public async Task GetAllAsync<T>(string path, Func<string, T, bool> onItemFound) where T : class
        {
            await _SemaphoreSlim.WaitAsync();
            try
            {
                var baseDirectoryPath = $"{_baseDirectory}{Path.DirectorySeparatorChar}{path}";

                Directory.CreateDirectory(baseDirectoryPath);

                foreach (var filePath in Directory.EnumerateFileSystemEntries(baseDirectoryPath))
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    var offlineObj = JsonConvert.DeserializeObject<OfflineObject>(json);
                    if (offlineObj?.Json == null) continue;
                    try
                    {
                        if (onItemFound?.Invoke(offlineObj.Key, JsonConvert.DeserializeObject<T>(offlineObj.Json)) ?? false)
                        {
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Failed to parse json element in GetAllAsync");
                    }
                }
            }
            finally
            {
                _SemaphoreSlim.Release();
            }
        }

        public async Task PutAsync<T>(string path, T data)
        {
            await _SemaphoreSlim.WaitAsync();
            try
            {
                await Internal_PutAsync(path, data);
            }
            finally
            {
                _SemaphoreSlim.Release();
            }
        }

        private async Task Internal_PutAsync<T>(string path, T data)
        {
            var dataJson = JsonConvert.SerializeObject(data);

            JObject? jsonObj = JsonConvert.DeserializeObject<JObject>(dataJson);
            if (jsonObj != null)
            {
                FileInfo file = new FileInfo($"{_baseDirectory}{Path.DirectorySeparatorChar}{path}.json");
                var filePath = file.FullName;
                var directoryName = Path.GetDirectoryName(filePath);
                Directory.CreateDirectory(directoryName);

                await File.WriteAllTextAsync(filePath, JsonConvert.SerializeObject(new OfflineObject
                {
                    Json = dataJson,
                    Key = path,
                    LastAccessed = DateTime.Now
                }));
            }
        }

        public async Task DeleteAsync(string path)
        {
            await _SemaphoreSlim.WaitAsync();
            try
            {
                File.Delete($"{_baseDirectory}{Path.DirectorySeparatorChar}{path}.json");

                RecursiveDelete(new DirectoryInfo($"{_baseDirectory}{Path.DirectorySeparatorChar}{path}"));
            }
            finally
            {
                _SemaphoreSlim.Release();
            }
        }

        private static void RecursiveDelete(DirectoryInfo baseDir)
        {
            if (!baseDir.Exists)
                return;

            foreach (var dir in baseDir.EnumerateDirectories())
            {
                RecursiveDelete(dir);
            }
            var files = baseDir.GetFiles();
            foreach (var file in files)
            {
                file.IsReadOnly = false;
                file.Delete();
            }
            baseDir.Delete();
        }
    }
}
