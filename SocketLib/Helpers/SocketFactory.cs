﻿using SocketLib.Configuration;
using SocketLib.Implementation;
using SocketLib.Interfaces;

namespace SocketLib.Helpers
{
    // Factory for creating socket clients and servers
    public static class SocketFactory
    {
        // Create a TCP client
        public static ISocketClient CreateTcpClient(SocketOptions? options = null, ISocketLogger? logger = null)
        {
            return new TcpSocketClient(options, logger);
        }

        // Create a TCP server
        public static ISocketServer CreateTcpServer(SocketOptions? options = null, ISocketLogger? logger = null)
        {
            return new TcpSocketServer(options, logger);
        }

        // Create a UDP client
        public static ISocketClient CreateUdpClient(SocketOptions? options = null, ISocketLogger? logger = null)
        {
            return new UdpSocketClient(options, logger);
        }

        // Create a UDP server
        public static ISocketServer CreateUdpServer(SocketOptions? options = null, ISocketLogger? logger = null)
        {
            return new UdpSocketServer(options, logger);
        }
    }
}