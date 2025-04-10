using System;
using SocketLib.Interfaces;

namespace SocketLib.Helpers
{
    // Default implementation of socket logger
    public class DefaultLogger : ISocketLogger
    {
        public void Debug(string message)
        {
            Console.WriteLine($"[DEBUG] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }
        public void Info(string message)
        {
            Console.WriteLine($"[INFO] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }
        public void Warning(string message)
        {
            Console.WriteLine($"[WARNING] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        public void Error(string message)
        {
            Console.WriteLine($"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }

        public void Error(string message, Exception exception)
        {
            Console.WriteLine($"[ERROR] {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
            Console.WriteLine($"Exception: {exception.Message}");
            Console.WriteLine($"StackTrace: {exception.StackTrace}");
        }
    }
}