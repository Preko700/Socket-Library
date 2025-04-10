namespace SocketLib.Configuration
{
    // Configuration options for socket operations
    public class SocketOptions
    {
        // Size of the buffer for receiving data
        public int BufferSize { get; set; } = 8192;

        // Timeout for socket operations in milliseconds
        public int TimeoutMilliseconds { get; set; } = 30000;

        // Maximum number of simultaneous connections (for server)
        public int MaxConnections { get; set; } = 100;

        // Whether to keep connection alive
        public bool KeepAlive { get; set; } = true;

        // Whether to use Nagle's algorithm
        public bool NoDelay { get; set; } = true;

        // Retry policy for failed operations
        public RetryPolicy RetryPolicy { get; set; } = new RetryPolicy();

        // Whether to auto-reconnect on disconnection
        public bool AutoReconnect { get; set; } = true;

        // Delay between reconnection attempts in milliseconds
        public int ReconnectDelayMilliseconds { get; set; } = 5000;
    }
}