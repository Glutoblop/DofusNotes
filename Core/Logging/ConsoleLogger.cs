using ChangeLogTracker.Core.Interfaces;

namespace ChangeLogTracker.Core.Logging
{
    public class ConsoleLogger : ILogger
    {
        private ELogType _LogType;

        public ConsoleLogger(ELogType logType)
        {
            _LogType = logType;
        }

        public void Log(string message, ELogType logType, params object[] arguments)
        {
            if (_LogType < logType) return;

            if (arguments == null || arguments.Length == 0)
            {
                Console.WriteLine($"[{DateTime.Now:t}] {logType} {message}");
            }
            else
            {
                Console.WriteLine($"[{DateTime.Now:t}] {logType} {message}", arguments);
            }
        }
    }
}
