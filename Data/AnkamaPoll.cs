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

            var now = DateTime.UtcNow;
            var dif = DateTime.UtcNow - timestamp.Date;

            Console.WriteLine($"Check Ankama Site Delay: {now:HH:mm:ss} -  {timestamp.Date:HH:mm:ss} = {dif.TotalMinutes} minutes.");

            if (dif.TotalSeconds < 2.4f && dif.TotalSeconds > 0)
            {
                var seconds = Math.Abs(2.4-dif.TotalSeconds);
                seconds = Math.Max(1.2, seconds);
                Console.WriteLine($"Waiting min of 1.2 seconds... {seconds} seconds");
                await Task.Delay(TimeSpan.FromSeconds(seconds));
            }
            else
            {
                Console.WriteLine($"No delay required");
            }

            timestamp.Date = DateTime.UtcNow;
            await db.PutAsync(path, timestamp);
        }
    }
}
