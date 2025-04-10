using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SocketLib.Interfaces
{

    // Defines the contract for a socket server
    public interface ISocketServer : IDisposable
    {
        // Start listening for client connections
        Task StartAsync(int port, IMessageHandler messageHandler, CancellationToken cancellationToken = default);

        // Start listening for client connections on a specific IP address
        Task StartAsync(IPAddress ipAddress, int port, IMessageHandler messageHandler, CancellationToken cancellationToken = default);


        // Stop the server
        void Stop();

        // Check if the server is running
        bool IsRunning { get; }

        // Event triggered when a client connects
        event EventHandler<ClientConnectedEventArgs> ClientConnected;


        // Event triggered when a client disconnects
        event EventHandler<ClientDisconnectedEventArgs> ClientDisconnected;
    }


    // Event arguments for client connection events
    public class ClientConnectedEventArgs : EventArgs
    {
        public IPEndPoint RemoteEndPoint { get; }

        public ClientConnectedEventArgs(IPEndPoint remoteEndPoint)
        {
            RemoteEndPoint = remoteEndPoint;
        }
    }

    // Event arguments for client disconnection events
    public class ClientDisconnectedEventArgs : EventArgs
    {
        public IPEndPoint RemoteEndPoint { get; }

        public ClientDisconnectedEventArgs(IPEndPoint remoteEndPoint)
        {
            RemoteEndPoint = remoteEndPoint;
        }
    }
}