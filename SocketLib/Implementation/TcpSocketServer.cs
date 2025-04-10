using SocketLib.Configuration;
using SocketLib.Exceptions;
using SocketLib.Interfaces;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace SocketLib.Implementation
{

    // Implementation of a TCP socket server
    public class TcpSocketServer : ISocketServer
    {
        private TcpListener _listener;
        private readonly SocketOptions _options;
        private readonly ISocketLogger _logger;
        private CancellationTokenSource _serverCts;
        private IMessageHandler _messageHandler;
        private bool _isRunning;
        private bool _disposed;
        private readonly ConcurrentDictionary<string, TcpClient> _clients = new ConcurrentDictionary<string, TcpClient>();

        // Create a new TCP socket server
        public TcpSocketServer(SocketOptions options = null, ISocketLogger logger = null)
        {
            _options = options ?? new SocketOptions();
            _logger = logger;
            _serverCts = new CancellationTokenSource();
        }

        public bool IsRunning => _isRunning;

        public event EventHandler<ClientConnectedEventArgs> ClientConnected;

        public event EventHandler<ClientDisconnectedEventArgs> ClientDisconnected;

        public Task StartAsync(int port, IMessageHandler messageHandler, CancellationToken cancellationToken = default)
        {
            return StartAsync(IPAddress.Any, port, messageHandler, cancellationToken);
        }

        public async Task StartAsync(IPAddress ipAddress, int port, IMessageHandler messageHandler, CancellationToken cancellationToken = default)
        {
            if (_isRunning)
                throw new InvalidOperationException("Server is already running");

            if (messageHandler == null)
                throw new ArgumentNullException(nameof(messageHandler));

            _messageHandler = messageHandler;
            _listener = new TcpListener(ipAddress, port);
            _serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                _listener.Start(_options.MaxConnections);
                _isRunning = true;

                _logger?.Info($"Server started. Listening on {ipAddress}:{port}");

                await AcceptClientsAsync(_serverCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to start server: {ex.Message}", ex);
                _isRunning = false;
                throw new SocketServerException("Failed to start server", ex);
            }
        }
        public void Stop()
        {
            _logger?.Info("Stopping server");

            if (_serverCts != null && !_serverCts.IsCancellationRequested)
            {
                _serverCts.Cancel();
            }

            // Close all client connections
            foreach (var client in _clients.Values)
            {
                try
                {
                    client.Close();
                    client.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.Error($"Error closing client connection: {ex.Message}");
                }
            }

            _clients.Clear();

            if (_listener != null)
            {
                _listener.Stop();
                _listener = null;
            }

            _isRunning = false;
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
                Stop();

                if (_serverCts != null)
                {
                    _serverCts.Dispose();
                    _serverCts = null;
                }
            }

            _disposed = true;
        }

        private async Task AcceptClientsAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                try
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);

                    // Configure client
                    client.ReceiveTimeout = _options.TimeoutMilliseconds;
                    client.SendTimeout = _options.TimeoutMilliseconds;
                    client.NoDelay = _options.NoDelay;

                    if (_options.KeepAlive)
                    {
                        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                    }

                    string clientId = GetClientId(client);
                    _clients[clientId] = client;

                    IPEndPoint remoteEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;
                    _logger?.Info($"Client connected: {remoteEndPoint}");

                    // Raise event
                    ClientConnected?.Invoke(this, new ClientConnectedEventArgs(remoteEndPoint));

                    // Start processing client messages in separate task
                    _ = ProcessClientAsync(client, clientId, cancellationToken);
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger?.Error($"Error accepting client: {ex.Message}", ex);

                    // Brief delay before continuing to prevent CPU spinning on repeated errors
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private async Task ProcessClientAsync(TcpClient client, string clientId, CancellationToken cancellationToken)
        {
            NetworkStream stream = client.GetStream();
            IPEndPoint remoteEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;

            try
            {
                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    try
                    {
                        // Read message length (4 bytes)
                        byte[] lengthBytes = new byte[4];
                        await ReadExactlyAsync(stream, lengthBytes, 0, lengthBytes.Length, cancellationToken).ConfigureAwait(false);
                        int messageLength = BitConverter.ToInt32(lengthBytes, 0);

                        // Sanity check on message length
                        if (messageLength <= 0 || messageLength > 100 * 1024 * 1024) // Max 100MB
                        {
                            _logger?.Warning($"Invalid message length from {remoteEndPoint}: {messageLength}");
                            continue;
                        }

                        // Read message
                        byte[] message = new byte[messageLength];
                        await ReadExactlyAsync(stream, message, 0, messageLength, cancellationToken).ConfigureAwait(false);

                        _logger?.Debug($"Received {messageLength} bytes from {remoteEndPoint}");

                        // Process message asynchronously with handler
                        byte[] response = await _messageHandler.HandleMessageAsync(remoteEndPoint, message, cancellationToken).ConfigureAwait(false);

                        // Send response if provided
                        if (response != null && response.Length > 0)
                        {
                            // Send response length
                            byte[] responseLengthBytes = BitConverter.GetBytes(response.Length);
                            await stream.WriteAsync(responseLengthBytes, 0, responseLengthBytes.Length, cancellationToken).ConfigureAwait(false);

                            // Send response data
                            await stream.WriteAsync(response, 0, response.Length, cancellationToken).ConfigureAwait(false);
                            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

                            _logger?.Debug($"Sent {response.Length} bytes to {remoteEndPoint}");
                        }
                    }
                    catch (Exception ex) when (ex is System.Net.Sockets.SocketException || ex is IOException || ex is ObjectDisposedException)
                    {
                        _logger?.Warning($"Client connection error: {ex.Message}");
                        break;
                    }
                }
            }
            finally
            {
                // Clean up client connection
                try
                {
                    stream.Close();
                    client.Close();
                    _clients.TryRemove(clientId, out _);

                    _logger?.Info($"Client disconnected: {remoteEndPoint}");

                    // Raise event
                    ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs(remoteEndPoint));
                }
                catch (Exception ex)
                {
                    _logger?.Error($"Error cleaning up client: {ex.Message}");
                }
            }
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
                        throw new EndOfStreamException("Client closed the connection");
                    }

                    totalBytesRead += bytesRead;
                }
            }
        }

        private string GetClientId(TcpClient client)
        {
            IPEndPoint remoteEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;
            return $"{remoteEndPoint.Address}:{remoteEndPoint.Port}";
        }
    }
}