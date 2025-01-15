using ChangeLogTracker.Core.Interfaces;

namespace ChangeLogTracker
{
    public static class ConstantData
    {
        /// <summary>You can commend players in an event 1 hour after the event started</summary>
        public static TimeSpan CommendationReminderTime => Defined.IsProd ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(15);

        public static TimeSpan SyncDatabaseTickTime => TimeSpan.FromMinutes(60);

        public static TimeSpan RemindOfEventStartTime => Defined.IsProd ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(5);
        public static TimeSpan ExpiryDuration => Defined.IsProd ? TimeSpan.FromMinutes(20) : TimeSpan.FromMinutes(5);

        //Message Timer
        public static TimeSpan MessageTimer_TimeSlot => Defined.IsProd ? TimeSpan.FromMinutes(5) : TimeSpan.FromSeconds(5);
        public static TimeSpan MessageTimer_TickTime => Defined.IsProd ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(3);

        public static ELogType LogType => Defined.IsProd ? ELogType.Log : ELogType.VeryVerbose;

    }
}
