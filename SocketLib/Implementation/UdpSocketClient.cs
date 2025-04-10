using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SocketLib.Configuration;
using SocketLib.Interfaces;
using SocketLib.Exceptions;

namespace SocketLib.Implementation
{
    // Implementation of a UDP socket client
    public class UdpSocketClient : ISocketClient
    {
        private UdpClient _client;
        private readonly SocketOptions _options;
        private readonly ISocketLogger _logger;
        private IPEndPoint _remoteEndPoint;
        private bool _isConnected;
        private CancellationTokenSource _reconnectCts;
        private string _lastConnectedHost;
        private int _lastConnectedPort;
        private bool _disposed;

        // Create a new UDP socket client
        public UdpSocketClient(SocketOptions options = null, ISocketLogger logger = null)
        {
            _options = options ?? new SocketOptions();
            _logger = logger;
            _client = new UdpClient();
            _reconnectCts = new CancellationTokenSource();
        }

        public bool IsConnected => _isConnected && _client != null && _remoteEndPoint != null;

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
                        _client = new UdpClient();
                    }

                    // Resolve hostname to IP address
                    IPAddress[] addresses = await Dns.GetHostAddressesAsync(hostname);
                    if (addresses.Length == 0)
                    {
                        throw new SocketConnectionException($"Could not resolve hostname: {hostname}");
                    }

                    // Use the first address
                    _remoteEndPoint = new IPEndPoint(addresses[0], port);

                    // In UDP there's no actual "connection" as in TCP,
                    // but we can "connect" the UdpClient to a specific endpoint
                    // which simplifies sending data
                    using (var timeoutCts = new CancellationTokenSource(_options.TimeoutMilliseconds))
                    using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken))
                    {
                        // Send a small "ping" packet to verify connectivity
                        byte[] pingData = Encoding.UTF8.GetBytes("PING");
                        await _client.SendAsync(pingData, pingData.Length, _remoteEndPoint).ConfigureAwait(false);

                        // Unlike TCP, UDP doesn't have a connection state to check
                        // We're assuming the connection is successful if no exceptions are thrown
                        _client.Connect(_remoteEndPoint);
                        _isConnected = true;
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
                    using (var timeoutCts = new CancellationTokenSource(_options.TimeoutMilliseconds))
                    using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken))
                    {
                        // In UDP we need to frame the message ourselves (include length)
                        // since UDP doesn't guarantee that a single message won't be fragmented
                        byte[] framedData = new byte[data.Length + 4];
                        BitConverter.GetBytes(data.Length).CopyTo(framedData, 0);
                        data.CopyTo(framedData, 4);

                        await _client.SendAsync(framedData, framedData.Length).ConfigureAwait(false);

                        sent = true;
                        _logger?.Debug($"Sent {data.Length} bytes of data");
                    }
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
                    using (var timeoutCts = new CancellationTokenSource(_options.TimeoutMilliseconds))
                    using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken))
                    {
                        // Receive response
                        UdpReceiveResult udpResult = await _client.ReceiveAsync(linkedCts.Token).ConfigureAwait(false);

                        // Extract message length from the first 4 bytes
                        if (udpResult.Buffer.Length < 4)
                        {
                            throw new SocketReceiveException("Received UDP packet is too small (missing length prefix)");
                        }

                        int messageLength = BitConverter.ToInt32(udpResult.Buffer, 0);

                        // Sanity check on message length
                        if (messageLength <= 0 || messageLength > 100 * 1024 * 1024 || // Max 100MB
                            messageLength != udpResult.Buffer.Length - 4)
                        {
                            throw new SocketReceiveException($"Invalid message length: expected {messageLength}, got {udpResult.Buffer.Length - 4}");
                        }

                        // Extract the actual message
                        result = new byte[messageLength];
                        Buffer.BlockCopy(udpResult.Buffer, 4, result, 0, messageLength);

                        _logger?.Debug($"Received {messageLength} bytes of data");
                    }
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

            if (_client != null)
            {
                _client.Close();
                _client.Dispose();
                _client = null;
            }

            _isConnected = false;
            _remoteEndPoint = null;
        }

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
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }

            _client = new UdpClient();
            _isConnected = false;

            try
            {
                // Resolve hostname to IP address
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(_lastConnectedHost);
                if (addresses.Length == 0)
                {
                    throw new SocketConnectionException($"Could not resolve hostname: {_lastConnectedHost}");
                }

                // Use the first address
                _remoteEndPoint = new IPEndPoint(addresses[0], _lastConnectedPort);

                // Send a small "ping" packet to verify connectivity
                byte[] pingData = Encoding.UTF8.GetBytes("PING");
                await _client.SendAsync(pingData, pingData.Length, _remoteEndPoint).ConfigureAwait(false);

                // Connect the client
                _client.Connect(_remoteEndPoint);
                _isConnected = true;

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
            Task.Run(async () => {
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