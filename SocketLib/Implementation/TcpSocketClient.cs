using SocketLib.Configuration;
using SocketLib.Exceptions;
using SocketLib.Interfaces;
using System.Net.Sockets;
using System.Text;

namespace SocketLib.Implementation
{
    // Implementation of a TCP socket client
    public class TcpSocketClient : ISocketClient
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private readonly SocketOptions _options;
        private readonly ISocketLogger _logger;
        private CancellationTokenSource _reconnectCts;
        private string _lastConnectedHost;
        private int _lastConnectedPort;
        private bool _disposed;

        // Create a new TCP socket client
        public TcpSocketClient(SocketOptions options = null, ISocketLogger logger = null)
        {
            _options = options ?? new SocketOptions();
            _logger = logger;
            _client = new TcpClient();
            _client.ReceiveTimeout = _options.TimeoutMilliseconds;
            _client.SendTimeout = _options.TimeoutMilliseconds;
            _client.NoDelay = _options.NoDelay;

            if (_options.KeepAlive)
            {
                _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            }

            _reconnectCts = new CancellationTokenSource();
        }

        public bool IsConnected => _client?.Connected == true && _stream != null;

        public async Task ConnectAsync(string hostname, int port, CancellationToken cancellationToken = default)
        {
            _lastConnectedHost = hostname;
            _lastConnectedPort = port;

            _logger?.Info($"Connecting to {hostname}:{port}");

            int attempts = 0;
            bool connected = false;

            while (!connected && attempts <= _options.RetryPolicy.MaxRetries)
            {
                try
                {
                    if (_client == null || _disposed)
                    {
                        _client = new TcpClient();
                        _client.ReceiveTimeout = _options.TimeoutMilliseconds;
                        _client.SendTimeout = _options.TimeoutMilliseconds;
                        _client.NoDelay = _options.NoDelay;
                    }

                    using (var timeoutCts = new CancellationTokenSource(_options.TimeoutMilliseconds))
                    using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken))
                    {
                        await _client.ConnectAsync(hostname, port, linkedCts.Token).ConfigureAwait(false);
                        _stream = _client.GetStream();
                        connected = true;

                        _logger?.Info($"Successfully connected to {hostname}:{port}");

                        // Start auto-reconnect monitoring if needed
                        if (_options.AutoReconnect)
                        {
                            StartReconnectMonitor();
                        }
                    }
                }
                catch (Exception ex) when (IsRetryableException(ex))
                {
                    attempts++;

                    if (attempts <= _options.RetryPolicy.MaxRetries)
                    {
                        int delayMs = _options.RetryPolicy.GetDelayForAttempt(attempts - 1);
                        _logger?.Warning($"Connection attempt {attempts} failed, retrying in {delayMs}ms: {ex.Message}");

                        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        _logger?.Error($"Failed to connect after {attempts} attempts", ex);
                        throw new SocketConnectionException($"Failed to connect to {hostname}:{port} after {attempts} attempts", ex);
                    }
                }
            }
        }

        public async Task SendAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            int attempts = 0;
            bool sent = false;

            while (!sent && attempts <= _options.RetryPolicy.MaxRetries)
            {
                try
                {
                    // Send message length first (4 bytes)
                    byte[] lengthBytes = BitConverter.GetBytes(data.Length);
                    await _stream.WriteAsync(lengthBytes, 0, lengthBytes.Length, cancellationToken).ConfigureAwait(false);

                    // Then send actual data
                    await _stream.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
                    await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);

                    sent = true;
                    _logger?.Debug($"Sent {data.Length} bytes of data");
                }
                catch (Exception ex) when (IsRetryableException(ex))
                {
                    attempts++;

                    if (attempts <= _options.RetryPolicy.MaxRetries)
                    {
                        int delayMs = _options.RetryPolicy.GetDelayForAttempt(attempts - 1);
                        _logger?.Warning($"Send attempt {attempts} failed, retrying in {delayMs}ms: {ex.Message}");

                        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);

                        // Try to reconnect if needed
                        if (!IsConnected && _options.AutoReconnect)
                        {
                            await ReconnectAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        _logger?.Error($"Failed to send data after {attempts} attempts", ex);
                        throw new SocketSendException($"Failed to send data after {attempts} attempts", ex);
                    }
                }
            }
        }

        public async Task SendStringAsync(string message, CancellationToken cancellationToken = default)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            await SendAsync(data, cancellationToken).ConfigureAwait(false);
        }

        public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken = default)
        {
            EnsureConnected();

            int attempts = 0;
            byte[] result = null;

            while (result == null && attempts <= _options.RetryPolicy.MaxRetries)
            {
                try
                {
                    // Read message length first (4 bytes)
                    byte[] lengthBytes = new byte[4];
                    await ReadExactlyAsync(_stream, lengthBytes, 0, lengthBytes.Length, cancellationToken).ConfigureAwait(false);
                    int messageLength = BitConverter.ToInt32(lengthBytes, 0);

                    // Sanity check on message length
                    if (messageLength <= 0 || messageLength > 100 * 1024 * 1024) // Max 100MB
                    {
                        throw new SocketReceiveException($"Invalid message length: {messageLength}");
                    }

                    // Read actual message
                    result = new byte[messageLength];
                    await ReadExactlyAsync(_stream, result, 0, messageLength, cancellationToken).ConfigureAwait(false);

                    _logger?.Debug($"Received {messageLength} bytes of data");
                }
                catch (Exception ex) when (IsRetryableException(ex))
                {
                    attempts++;

                    if (attempts <= _options.RetryPolicy.MaxRetries)
                    {
                        int delayMs = _options.RetryPolicy.GetDelayForAttempt(attempts - 1);
                        _logger?.Warning($"Receive attempt {attempts} failed, retrying in {delayMs}ms: {ex.Message}");

                        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);

                        // Try to reconnect if needed
                        if (!IsConnected && _options.AutoReconnect)
                        {
                            await ReconnectAsync(cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        _logger?.Error($"Failed to receive data after {attempts} attempts", ex);
                        throw new SocketReceiveException($"Failed to receive data after {attempts} attempts", ex);
                    }
                }
            }

            return result;
        }

        public async Task<string> ReceiveStringAsync(CancellationToken cancellationToken = default)
        {
            byte[] data = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
            return Encoding.UTF8.GetString(data);
        }

        public void Disconnect()
        {
            _logger?.Info("Disconnecting client");

            if (_reconnectCts != null)
            {
                _reconnectCts.Cancel();
                _reconnectCts.Dispose();
                _reconnectCts = new CancellationTokenSource();
            }

            if (_stream != null)
            {
                _stream.Close();
                _stream.Dispose();
                _stream = null;
            }

            if (_client != null)
            {
                _client.Close();
                _client.Dispose();
                _client = null;
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                Disconnect();

                if (_reconnectCts != null)
                {
                    _reconnectCts.Dispose();
                    _reconnectCts = null;
                }
            }

            _disposed = true;
        }

        private async Task ReadExactlyAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int totalBytesRead = 0;

            using (var timeoutCts = new CancellationTokenSource(_options.TimeoutMilliseconds))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken))
            {
                while (totalBytesRead < count)
                {
                    int bytesRead = await stream.ReadAsync(
                        buffer,
                        offset + totalBytesRead,
                        count - totalBytesRead,
                        linkedCts.Token).ConfigureAwait(false);

                    if (bytesRead == 0)
                    {
                        throw new EndOfStreamException("Remote endpoint closed the connection");
                    }

                    totalBytesRead += bytesRead;
                }
            }
        }

        private void EnsureConnected()
        {
            if (!IsConnected)
            {
                if (_options.AutoReconnect && !string.IsNullOrEmpty(_lastConnectedHost) && _lastConnectedPort > 0)
                {
                    // Auto-reconnect will happen in another method
                    throw new SocketNotConnectedException("Not connected to a remote endpoint");
                }
                else
                {
                    throw new SocketNotConnectedException("Not connected to a remote endpoint");
                }
            }
        }

        private async Task ReconnectAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_lastConnectedHost) || _lastConnectedPort <= 0)
                return;

            _logger?.Info($"Attempting to reconnect to {_lastConnectedHost}:{_lastConnectedPort}");

            // Clean up existing connections
            if (_stream != null)
            {
                _stream.Dispose();
                _stream = null;
            }

            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }

            _client = new TcpClient();
            _client.ReceiveTimeout = _options.TimeoutMilliseconds;
            _client.SendTimeout = _options.TimeoutMilliseconds;
            _client.NoDelay = _options.NoDelay;

            try
            {
                await _client.ConnectAsync(_lastConnectedHost, _lastConnectedPort, cancellationToken).ConfigureAwait(false);
                _stream = _client.GetStream();
                _logger?.Info($"Successfully reconnected to {_lastConnectedHost}:{_lastConnectedPort}");
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to reconnect: {ex.Message}", ex);
                throw new SocketReconnectException("Failed to reconnect", ex);
            }
        }

        private void StartReconnectMonitor()
        {
            Task.Run(async () =>
            {
                while (!_reconnectCts.IsCancellationRequested)
                {
                    await Task.Delay(1000, _reconnectCts.Token).ConfigureAwait(false);

                    if (!_reconnectCts.IsCancellationRequested && !IsConnected &&
                        !string.IsNullOrEmpty(_lastConnectedHost) && _lastConnectedPort > 0)
                    {
                        _logger?.Info("Connection lost, attempting to reconnect");

                        try
                        {
                            await ReconnectAsync(_reconnectCts.Token).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger?.Error($"Auto-reconnect failed: {ex.Message}");
                            await Task.Delay(_options.ReconnectDelayMilliseconds, _reconnectCts.Token).ConfigureAwait(false);
                        }
                    }
                }
            }, _reconnectCts.Token);
        }

        private bool IsRetryableException(Exception ex)
        {
            foreach (var exceptionType in _options.RetryPolicy.RetryableExceptions)
            {
                if (exceptionType.IsAssignableFrom(ex.GetType()))
                    return true;
            }

            return false;
        }
    }
}