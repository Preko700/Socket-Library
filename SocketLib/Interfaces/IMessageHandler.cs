using System.Net;

namespace SocketLib.Interfaces
{
    // Defines the contract for handling socket messages
    public interface IMessageHandler
    {
        // Handle a message received from a client
        Task<byte[]> HandleMessageAsync(IPEndPoint sender, byte[] message, CancellationToken cancellationToken = default);
    }
}