using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SocketLib.Configuration;
using SocketLib.Helpers;

namespace ChatClient
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.Title = "Chat Client - Ejemplo de SocketLib";
            Console.WriteLine("=== CLIENTE DE CHAT ===");
            Console.WriteLine("Fecha actual: 2025-04-10 (UTC)");
            Console.WriteLine("Usuario: Preko700");
            Console.WriteLine();

            // Configuración predeterminada del servidor
            string serverHost = "localhost";
            int serverPort = 8080;

            // Permitir al usuario configurar el servidor
            Console.Write($"Servidor [{serverHost}]: ");
            string input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input))
                serverHost = input;

            Console.Write($"Puerto [{serverPort}]: ");
            input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input) && int.TryParse(input, out int port))
                serverPort = port;

            // Solicitar nombre de usuario
            Console.Write("Nombre de usuario: ");
            string username = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(username))
                username = $"Usuario_{new Random().Next(1000, 9999)}";

            // Crear opciones
            var options = new SocketOptions
            {
                TimeoutMilliseconds = 5000,
                AutoReconnect = true,
                RetryPolicy = new RetryPolicy
                {
                    MaxRetries = 3,
                    BaseDelayMilliseconds = 1000
                }
            };

            // Crear logger
            var logger = new DefaultLogger();

            // Crear cliente TCP
            using (var client = SocketFactory.CreateTcpClient(options, logger))
            {
                try
                {
                    // Conectar al servidor
                    Console.WriteLine($"Conectando a {serverHost}:{serverPort}...");
                    await client.ConnectAsync(serverHost, serverPort);
                    Console.WriteLine("Conectado al servidor!");

                    // Registrar el usuario
                    await client.SendStringAsync($"REGISTER:{username}");
                    string response = await client.ReceiveStringAsync();
                    Console.WriteLine($"Servidor: {response}");

                    // Inicio de la sesión de chat
                    Console.WriteLine("\n=== CHAT INICIADO ===");
                    Console.WriteLine("Escribe tus mensajes y presiona Enter para enviar.");
                    Console.WriteLine("Para enviar un mensaje privado: @usuario:mensaje");
                    Console.WriteLine("Para salir escribe 'exit' o 'quit'");
                    Console.WriteLine("===================\n");

                    // Iniciar un hilo para recibir mensajes de forma asíncrona
                    var cts = new CancellationTokenSource();
                    Task receiveTask = Task.Run(async () => {
                        try
                        {
                            while (!cts.Token.IsCancellationRequested && client.IsConnected)
                            {
                                try
                                {
                                    string message = await client.ReceiveStringAsync(cts.Token);
                                    Console.WriteLine($"\rServidor: {message}");
                                    Console.Write("> "); // Restaurar el prompt
                                }
                                catch (OperationCanceledException)
                                {
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"\rError recibiendo mensajes: {ex.Message}");
                                    if (!client.IsConnected)
                                        break;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"\rError fatal en recepción: {ex.Message}");
                        }
                    }, cts.Token);

                    // Bucle principal para enviar mensajes
                    while (client.IsConnected)
                    {
                        Console.Write("> ");
                        input = Console.ReadLine();

                        if (string.IsNullOrEmpty(input))
                            continue;

                        // Comprobar si el usuario quiere salir
                        if (input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                            input.Equals("quit", StringComparison.OrdinalIgnoreCase))
                            break;

                        try
                        {
                            // Enviar mensaje
                            await client.SendStringAsync(input);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error enviando mensaje: {ex.Message}");
                            if (!client.IsConnected)
                                break;
                        }
                    }

                    // Cancelar el hilo de recepción
                    cts.Cancel();
                    try
                    {
                        await receiveTask.WaitAsync(TimeSpan.FromSeconds(2));
                    }
                    catch (TimeoutException)
                    {
                        Console.WriteLine("El hilo de recepción no pudo cerrarse correctamente.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: {ex.Message}");
                }
                finally
                {
                    // Asegurar que el cliente se desconecta correctamente
                    Console.WriteLine("Desconectando del servidor...");
                    client.Disconnect();
                    Console.WriteLine("Desconectado");
                }
            }

            Console.WriteLine("Presiona cualquier tecla para salir...");
            Console.ReadKey();
        }
    }
}