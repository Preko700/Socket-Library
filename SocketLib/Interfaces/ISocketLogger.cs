namespace SocketLib.Interfaces
{
    // Defines the contract for socket logging
    public interface ISocketLogger
    {
        void Debug(string message);
        void Info(string message);
        void Warning(string message);
        void Error(string message);
        void Error(string message, Exception exception);
    }
}