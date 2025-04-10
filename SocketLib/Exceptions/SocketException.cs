using System;
using System.Runtime.Serialization;

namespace SocketLib.Exceptions
{
    // Base exception for all socket-related exceptions
    [Serializable]
    public class SocketException : Exception
    {
        public SocketException() { }

        public SocketException(string message) : base(message) { }

        public SocketException(string message, Exception innerException) : base(message, innerException) { }

        protected SocketException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    // Exception thrown when connection to a remote endpoint fails
    [Serializable]
    public class SocketConnectionException : SocketException
    {
        public SocketConnectionException() { }

        public SocketConnectionException(string message) : base(message) { }

        public SocketConnectionException(string message, Exception innerException) : base(message, innerException) { }

        protected SocketConnectionException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    // Exception thrown when sending data fails
    [Serializable]
    public class SocketSendException : SocketException
    {
        public SocketSendException() { }

        public SocketSendException(string message) : base(message) { }

        public SocketSendException(string message, Exception innerException) : base(message, innerException) { }

        protected SocketSendException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    // Exception thrown when receiving data fails
    [Serializable]
    public class SocketReceiveException : SocketException
    {
        public SocketReceiveException() { }

        public SocketReceiveException(string message) : base(message) { }

        public SocketReceiveException(string message, Exception innerException) : base(message, innerException) { }

        protected SocketReceiveException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    // Exception thrown when a socket operation is attempted on a disconnected socket
    [Serializable]
    public class SocketNotConnectedException : SocketException
    {
        public SocketNotConnectedException() { }

        public SocketNotConnectedException(string message) : base(message) { }

        public SocketNotConnectedException(string message, Exception innerException) : base(message, innerException) { }

        protected SocketNotConnectedException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    // Exception thrown when a server fails to start
    [Serializable]
    public class SocketServerException : SocketException
    {
        public SocketServerException() { }

        public SocketServerException(string message) : base(message) { }

        public SocketServerException(string message, Exception innerException) : base(message, innerException) { }

        protected SocketServerException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    // Exception thrown when a reconnection attempt fails
    [Serializable]
    public class SocketReconnectException : SocketException
    {
        public SocketReconnectException() { }

        public SocketReconnectException(string message) : base(message) { }

        public SocketReconnectException(string message, Exception innerException) : base(message, innerException) { }

        protected SocketReconnectException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}