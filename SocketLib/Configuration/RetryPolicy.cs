namespace SocketLib.Configuration
{
    // Policy for retrying failed operations
    public class RetryPolicy
    {
        // Maximum number of retry attempts
        public int MaxRetries { get; set; } = 3;

        // Base delay between retries in milliseconds
        public int BaseDelayMilliseconds { get; set; } = 1000;

        // Whether to use exponential backoff
        public bool UseExponentialBackoff { get; set; } = true;

        // Types of exceptions that should trigger a retry
        public Type[] RetryableExceptions { get; set; } = new Type[]
        {
            typeof(TimeoutException),
            typeof(System.Net.Sockets.SocketException)
        };

        // Calculate the delay for a specific retry attempt
        public int GetDelayForAttempt(int attempt)
        {
            if (attempt < 0)
                throw new ArgumentOutOfRangeException(nameof(attempt));

            if (!UseExponentialBackoff)
                return BaseDelayMilliseconds;

            // Calculate exponential backoff with jitter
            int exponentialDelay = BaseDelayMilliseconds * (int)Math.Pow(2, attempt);
            Random random = new Random();
            int jitter = random.Next(-(BaseDelayMilliseconds / 4), BaseDelayMilliseconds / 4);

            return Math.Max(BaseDelayMilliseconds, exponentialDelay + jitter);
        }
    }
}