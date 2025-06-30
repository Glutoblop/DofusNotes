using ChangeLogTracker.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace ChangeLogTracker.Data
{
    public class AnkamaPoll
    {
        public AnkamaPoll()
        {
            Date = DateTime.UtcNow;
        }

        public DateTime Date;

        public static async Task AwaitTimestampDelay(IServiceProvider services)
        {
            var path = "LastPollAnkama";
            var db = services.GetRequiredService<IDatabase>();
            var timestamp = await db.GetAsync<AnkamaPoll>(path);
            if (timestamp == null)
            {
                await db.PutAsync(path, new AnkamaPoll());
                return;
            }

            var dif = DateTime.UtcNow - timestamp.Date;
            if (dif.TotalSeconds < 2.4f && dif.TotalSeconds > 0)
            {
                await Task.Delay(dif);
            }

            timestamp.Date = DateTime.UtcNow;
            await db.PutAsync(path, timestamp);
        }
    }
}
