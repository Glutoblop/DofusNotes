namespace ChangeLogTracker.Core.Interfaces
{
    public enum ELogType
    {
        /// <summary>Only shows log level</summary>
        Log = 1,
        /// <summary>Shows warnings and log level</summary>
        Warning = 2,
        /// <summary>Shows warnings and logs</summary>
        Error = 3,
        /// <summary>Shows Verbose, Errors, Warning and Log</summary>
        Verbose = 4,
        /// <summary>Shows all logging</summary>
        VeryVerbose = 5,
    }

    public interface ILogger
    {
        void Log(string message, ELogType logType = ELogType.Log, params object[] arguments);
    }
}
