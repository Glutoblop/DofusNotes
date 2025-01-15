// ReSharper disable once CheckNamespace
namespace ChangeLogTracker
{
    public class Defined
    {
#if DEBUG
        public const bool IsProd = false;
        public const bool UseProdDatabase = false;
        public const bool UseProdToken = false;
#else
        public const bool IsProd = true;
        public const bool UseProdDatabase = true;
        public const bool UseProdToken = true;
#endif
    }
}
