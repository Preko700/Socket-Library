using SocketLib.Configuration;
using SocketLib.Exceptions;
using SocketLib.Interfaces;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace SocketLib.Implementation
{
    // Implementation of a UDP socket server
    public class UdpSocketServer : ISocketServer
    {
        private UdpClient _listener;
        private readonly SocketOptions _options;
        private readonly ISocketLogger _logger;
        private CancellationTokenSource _serverCts;
        private IMessageHandler _messageHandler;
        private bool _isRunning;
        private bool _disposed;
        private readonly ConcurrentDictionary<string, IPEndPoint> _clients = new ConcurrentDictionary<string, IPEndPoint>();

        // Create a new UDP socket server
        public UdpSocketServer(SocketOptions options = null, ISocketLogger logger = null)
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
            _listener = new UdpClient(new IPEndPoint(ipAddress, port));
            _serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                _isRunning = true;

                _logger?.Info($"UDP Server started. Listening on {ipAddress}:{port}");

                await ProcessMessagesAsync(_serverCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.Error($"Failed to start UDP server: {ex.Message}", ex);
                _isRunning = false;
                throw new SocketServerException("Failed to start UDP server", ex);
            }
        }

        public void Stop()
        {
            _logger?.Info("Stopping UDP server");

            if (_serverCts != null && !_serverCts.IsCancellationRequested)
            {
                _serverCts.Cancel();
            }

            // Clear client list
            _clients.Clear();

            if (_listener != null)
            {
                _listener.Close();
                _listener.Dispose();
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

        private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                try
                {
                    // UDP is connectionless, so we don't have a client connection to accept
                    // Instead, we just wait for incoming datagrams
                    UdpReceiveResult result = await _listener.ReceiveAsync().ConfigureAwait(false);

                    // Get the remote endpoint
                    IPEndPoint remoteEndPoint = result.RemoteEndPoint;
                    string clientId = GetClientId(remoteEndPoint);

                    // Check if this is a new client
                    bool isNewClient = false;
                    if (!_clients.ContainsKey(clientId))
                    {
                        _clients[clientId] = remoteEndPoint;
                        isNewClient = true;

                        _logger?.Info($"New client detected: {remoteEndPoint}");

                        // Raise event
                        ClientConnected?.Invoke(this, new ClientConnectedEventArgs(remoteEndPoint));
                    }

                    // Process the message in a separate task to not block the receive loop
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            // Extract message from framed data
                            if (result.Buffer.Length < 4)
                            {
                                _logger?.Warning($"Received malformed UDP packet from {remoteEndPoint} (too short)");
                                return;
                            }

                            int messageLength = BitConverter.ToInt32(result.Buffer, 0);

                            // Sanity check on message length
                            if (messageLength <= 0 || messageLength > 100 * 1024 * 1024 || // Max 100MB
                                messageLength != result.Buffer.Length - 4)
                            {
                                _logger?.Warning($"Received malformed UDP packet from {remoteEndPoint} (invalid length: {messageLength})");
                                return;
                            }

                            // Extract the actual message
                            byte[] message = new byte[messageLength];
                            Buffer.BlockCopy(result.Buffer, 4, message, 0, messageLength);

                            _logger?.Debug($"Received {messageLength} bytes from {remoteEndPoint}");

                            // Process message with handler
                            byte[] response = await _messageHandler.HandleMessageAsync(remoteEndPoint, message, cancellationToken).ConfigureAwait(false);

                            // Send response if provided
                            if (response != null && response.Length > 0)
                            {
                                // Frame the response with its length
                                byte[] framedResponse = new byte[response.Length + 4];
                                BitConverter.GetBytes(response.Length).CopyTo(framedResponse, 0);
                                response.CopyTo(framedResponse, 4);

                                // Send the response back to the client
                                await _listener.SendAsync(framedResponse, framedResponse.Length, remoteEndPoint).ConfigureAwait(false);

                                _logger?.Debug($"Sent {response.Length} bytes to {remoteEndPoint}");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.Error($"Error processing UDP message: {ex.Message}", ex);
                        }
                    }, cancellationToken);
                }
                catch (System.Net.Sockets.SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    // This happens when a UDP client disconnects abruptly
                    // We can't know which client it was, so we just log it
                    _logger?.Warning($"UDP connection reset by remote host: {ex.Message}");
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    _logger?.Error($"Error receiving UDP message: {ex.Message}", ex);

                    // Brief delay before continuing to prevent CPU spinning on repeated errors
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private string GetClientId(IPEndPoint remoteEndPoint)
        {
            return $"{remoteEndPoint.Address}:{remoteEndPoint.Port}";
        }
    }
}