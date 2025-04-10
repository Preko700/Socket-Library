namespace SocketLib.Interfaces
{

    // Defines the contract for a socket client
    public interface ISocketClient : IDisposable
    {

        // Connect to a remote endpoint
        Task ConnectAsync(string hostname, int port, CancellationToken cancellationToken = default);

        // Send data to the connected endpoint
        Task SendAsync(byte[] data, CancellationToken cancellationToken = default);

        // Send a string message to the connected endpoint
        Task SendStringAsync(string message, CancellationToken cancellationToken = default);


        // Receive data from the connected endpoint
        Task<byte[]> ReceiveAsync(CancellationToken cancellationToken = default);

        // Receive a string message from the connected endpoint
        Task<string> ReceiveStringAsync(CancellationToken cancellationToken = default);

        // Disconnect from the remote endpoint
        void Disconnect();

        // Check if the client is connected
        bool IsConnected { get; }
    }
}