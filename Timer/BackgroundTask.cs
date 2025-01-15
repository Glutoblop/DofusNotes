namespace ChangeLogTracker.Timer
{
    public class BackgroundTask(TimeSpan interval, Func<Task> doWork, TimeSpan? initialDelay = null)
    {
        private Task? _timerTask;
        private readonly PeriodicTimer _timer = new(interval);
        private readonly CancellationTokenSource _cts = new();

        private PeriodicTimer _intialTimer;

        public void Start()
        {
            if (initialDelay != null)
            {
                _intialTimer = new(initialDelay.Value);
            }

            _timerTask = DoWorkAsync();
        }

        private async Task DoWorkAsync()
        {
            try
            {
                while (
                    (_intialTimer != null && await _intialTimer.WaitForNextTickAsync(_cts.Token)) || 
                    await _timer.WaitForNextTickAsync(_cts.Token))
                {
                    _intialTimer = null;
                    await doWork();
                }
            }
            catch (OperationCanceledException)
            {

            }
        }

        public async Task StopAsync()
        {
            if (_timerTask is null)
            {
                return;
            }

            await _cts.CancelAsync();
            await _timerTask;
            _cts.Dispose();
        }
    }
}