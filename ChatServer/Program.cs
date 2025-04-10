using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using SocketLib.Configuration;
using SocketLib.Helpers;
using SocketLib.Interfaces;

namespace ChatServer
{
    // Clase que maneja los mensajes recibidos
    public class ChatMessageHandler : IMessageHandler
    {
        private readonly ConcurrentDictionary<string, IPEndPoint> _clients = new ConcurrentDictionary<string, IPEndPoint>();
        private readonly ISocketLogger _logger;

        public ChatMessageHandler(ISocketLogger logger)
        {
            _logger = logger;
        }

        public async Task<byte[]> HandleMessageAsync(IPEndPoint sender, byte[] message, CancellationToken cancellationToken = default)
        {
            string messageStr = Encoding.UTF8.GetString(message);
            string senderKey = $"{sender.Address}:{sender.Port}";

            _logger.Info($"Mensaje de {senderKey}: {messageStr}");

            // Si es un mensaje de registro (formato: "REGISTER:username")
            if (messageStr.StartsWith("REGISTER:", StringComparison.OrdinalIgnoreCase))
            {
                string username = messageStr.Substring(9).Trim();
                if (!string.IsNullOrEmpty(username))
                {
                    _clients[username] = sender;
                    _logger.Info($"Usuario {username} registrado desde {senderKey}");
                    return Encoding.UTF8.GetBytes($"Bienvenido al chat, {username}! Hay {_clients.Count} usuarios conectados.");
                }
                return Encoding.UTF8.GetBytes("Error: Nombre de usuario no válido");
            }

            // Si es un mensaje privado (formato: "@username:mensaje")
            if (messageStr.StartsWith("@"))
            {
                int colonIndex = messageStr.IndexOf(':');
                if (colonIndex > 1)
                {
                    string targetUser = messageStr.Substring(1, colonIndex - 1);
                    string privateMessage = messageStr.Substring(colonIndex + 1);

                    if (_clients.TryGetValue(targetUser, out IPEndPoint targetEndpoint))
                    {
                        _logger.Info($"Mensaje privado para {targetUser}");
                        // Simplemente confirmamos que el mensaje privado fue enviado
                        // En un sistema real, enviaríamos directamente al usuario destino
                        return Encoding.UTF8.GetBytes($"Mensaje privado enviado a {targetUser}");
                    }
                    else
                    {
                        return Encoding.UTF8.GetBytes($"Error: Usuario {targetUser} no encontrado");
                    }
                }
            }

            // Mensaje normal - simplemente hacemos eco con una marca de tiempo
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            return Encoding.UTF8.GetBytes($"[{timestamp}] ECHO: {messageStr}");
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            Console.Title = "Chat Server - Ejemplo de SocketLib";
            Console.WriteLine("=== SERVIDOR DE CHAT ===");
            Console.WriteLine("Fecha actual: 2025-04-10 (UTC)");
            Console.WriteLine("Administrador: Preko700");
            Console.WriteLine();

            // Puerto en el que escuchará el servidor
            int port = 8080;

            // Crear opciones de configuración
            var options = new SocketOptions
            {
                BufferSize = 4096,
                MaxConnections = 100,
                RetryPolicy = new RetryPolicy
                {
                    MaxRetries = 3,
                    BaseDelayMilliseconds = 1000
                }
            };

            // Crear logger
            var logger = new DefaultLogger();

            // Crear manejador de mensajes
            var messageHandler = new ChatMessageHandler(logger);

            // Crear y configurar el servidor TCP
            using (var server = SocketFactory.CreateTcpServer(options, logger))
            {
                // Suscribirse a eventos
                server.ClientConnected += (sender, e) =>
                    Console.WriteLine($"[INFO] Cliente conectado: {e.RemoteEndPoint}");

                server.ClientDisconnected += (sender, e) =>
                    Console.WriteLine($"[INFO] Cliente desconectado: {e.RemoteEndPoint}");

                try
                {
                    // Iniciar el servidor en un puerto específico
                    Console.WriteLine($"Iniciando servidor en puerto {port}...");

                    // Usamos Task.Run para iniciar el servidor en un hilo separado, 
                    // así podemos seguir usando la consola
                    await Task.Run(() => server.StartAsync(port, messageHandler));

                    Console.WriteLine($"Servidor iniciado. Escuchando en el puerto {port}");
                    Console.WriteLine("Presiona ESC para detener el servidor");

                    // Mantener el servidor en ejecución hasta que se presione ESC
                    while (true)
                    {
                        if (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(true);
                            if (key.Key == ConsoleKey.Escape)
                                break;
                        }

                        await Task.Delay(100);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: {ex.Message}");
                }
                finally
                {
                    // Asegurar que el servidor se detiene correctamente
                    Console.WriteLine("Deteniendo servidor...");
                    server.Stop();
                    Console.WriteLine("Servidor detenido");
                }
            }

            Console.WriteLine("Presiona cualquier tecla para salir...");
            Console.ReadKey();
        }
    }
}
