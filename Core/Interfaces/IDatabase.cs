namespace ChangeLogTracker.Core.Interfaces
{
    public interface IDatabase
    {
        Task Init(string localCacheName);

        Task<T> GetAsync<T>(string path) where T : class;

        Task GetAllAsync<T>(string path, Func<string, T, bool> onItemFound) where T : class;

        Task PutAsync<T>(string path, T data);

        Task DeleteAsync(string path);
    }
}