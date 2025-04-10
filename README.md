# Socket Library

Biblioteca de sockets genérica para .NET 8 que proporciona una capa de abstracción para comunicaciones TCP/UDP.

![.NET 8.0](https://img.shields.io/badge/.NET-8.0-blue)
![License](https://img.shields.io/badge/License-MIT-green)
![Status](https://img.shields.io/badge/Status-Stable-brightgreen)

**Última actualización:** 2025-04-09  
**Mantenedor:** [Adrian Monge Mairena](https://github.com/Preko700)
**Desarrollador:** [Jimena Castillo Campos](https://github.com/JimenaCastillo)

## 🌟 Características

- **Soporte para múltiples protocolos**: TCP y UDP
- **Operaciones asíncronas**: API totalmente asíncrona basada en Task/async-await
- **Recuperación automática**: Reconexión y reintentos configurables
- **Desacoplamiento**: Lógica de procesamiento de mensajes independiente
- **Extensibilidad**: Arquitectura basada en interfaces para facilitar extensiones
- **Manejo robusto de errores**: Excepciones personalizadas y políticas de reintentos
- **Configuración flexible**: Opciones para personalizar el comportamiento

## 📋 Requisitos

- .NET 8.0 o superior
- Visual Studio 2022 (para desarrollo)

## 🚀 Instalación

### Opción 1: Referencia directa a la DLL

1. Descarga la última versión de `SocketLib.dll` desde la sección de releases
2. Agrega una referencia a la DLL en tu proyecto

### Opción 2: Clonar el repositorio y compilar

```bash
git clone https://github.com/Preko700/SocketLib.git
cd SocketLib
dotnet build -c Release
```

La DLL compilada estará disponible en `bin/Release/net8.0/SocketLib.dll`

## 🔰 Uso Rápido

### Ejemplo de Servidor TCP

```csharp
using SocketLib.Configuration;
using SocketLib.Helpers;
using SocketLib.Interfaces;
using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;

// Implementa un manejador de mensajes
public class EchoMessageHandler : IMessageHandler
{
    public Task<byte[]> HandleMessageAsync(IPEndPoint sender, byte[] message, CancellationToken cancellationToken = default)
    {
        string receivedText = Encoding.UTF8.GetString(message);
        Console.WriteLine($"Recibido de {sender}: {receivedText}");
        
        string response = $"Echo: {receivedText}";
        return Task.FromResult(Encoding.UTF8.GetBytes(response));
    }
}

// Crear y ejecutar un servidor
async Task RunServerAsync()
{
    var options = new SocketOptions { MaxConnections = 10 };
    var logger = new DefaultLogger();
    var messageHandler = new EchoMessageHandler();
    
    using (var server = SocketFactory.CreateTcpServer(options, logger))
    {
        // Eventos para conectar/desconectar clientes
        server.ClientConnected += (s, e) => Console.WriteLine($"Cliente conectado: {e.RemoteEndPoint}");
        server.ClientDisconnected += (s, e) => Console.WriteLine($"Cliente desconectado: {e.RemoteEndPoint}");
        
        // Iniciar el servidor
        await server.StartAsync(8080, messageHandler);
        
        Console.WriteLine("Servidor iniciado en puerto 8080. Presiona Enter para detener.");
        Console.ReadLine();
        
        server.Stop();
    }
}
```

### Ejemplo de Cliente TCP

```csharp
using SocketLib.Configuration;
using SocketLib.Helpers;
using System;
using System.Threading.Tasks;

async Task RunClientAsync()
{
    var options = new SocketOptions 
    { 
        AutoReconnect = true, 
        RetryPolicy = new RetryPolicy { MaxRetries = 3 } 
    };
    var logger = new DefaultLogger();
    
    using (var client = SocketFactory.CreateTcpClient(options, logger))
    {
        try
        {
            // Conectar al servidor
            await client.ConnectAsync("localhost", 8080);
            Console.WriteLine("Conectado al servidor!");
            
            // Enviar mensaje
            await client.SendStringAsync("Hola, servidor!");
            
            // Recibir respuesta
            string response = await client.ReceiveStringAsync();
            Console.WriteLine($"Respuesta del servidor: {response}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
        finally
        {
            client.Disconnect();
        }
    }
}
```

## 📐 Arquitectura

La biblioteca está diseñada siguiendo principios SOLID y patrones de diseño comunes:

```
SocketLib/
├── Interfaces/         # Contratos del API
├── Configuration/      # Opciones configurables
├── Implementation/     # Implementaciones concretas (TCP/UDP)
├── Helpers/            # Utilidades y factories
└── Exceptions/         # Excepciones personalizadas
```

### Principios de Diseño

- **Principio de Responsabilidad Única (SRP)**: Cada clase tiene una única responsabilidad
- **Principio Abierto/Cerrado (OCP)**: La biblioteca es extensible sin modificar el código existente
- **Principio de Sustitución de Liskov (LSP)**: Las implementaciones son intercambiables a través de interfaces
- **Principio de Segregación de Interfaces (ISP)**: Interfaces específicas para clientes y servidores
- **Principio de Inversión de Dependencias (DIP)**: Dependencia de abstracciones en lugar de implementaciones concretas

### Patrones de Diseño Implementados

- **Factory**: `SocketFactory` para crear instancias
- **Strategy**: `IMessageHandler` para personalizar el procesamiento de mensajes
- **Observer**: Eventos para notificaciones de conexión/desconexión
- **Retry Pattern**: Política de reintentos configurable

## 📋 API Principal

### Interfaces

- `ISocketClient`: Operaciones del cliente (conectar, enviar, recibir)
- `ISocketServer`: Operaciones del servidor (iniciar, detener, eventos)
- `IMessageHandler`: Procesamiento de mensajes
- `ISocketLogger`: Registro de eventos y errores

### Implementaciones

- `TcpSocketClient`: Cliente TCP con reconexión automática
- `TcpSocketServer`: Servidor TCP multi-cliente
- `UdpSocketClient`: Cliente UDP con framing de mensajes
- `UdpSocketServer`: Servidor UDP

### Configuración

- `SocketOptions`: Opciones para personalizar el comportamiento
- `RetryPolicy`: Configuración de reintentos y backoff exponencial

## 🧪 Pruebas

La solución incluye proyectos de ejemplo para probar la biblioteca:

- `ChatServer`: Implementación de un servidor de chat simple
- `ChatClient`: Cliente de chat interactivo para consola

Para ejecutar las pruebas:

1. Establece los proyectos de inicio múltiples en las propiedades de la solución
2. Presiona F5 para iniciar ambos proyectos
3. Sigue las instrucciones en la consola del cliente

## 💡 Casos de Uso

- Aplicaciones cliente-servidor
- Servicios de comunicación en tiempo real
- Sistemas de chat y mensajería
- Servidores de juegos multijugador
- Monitoreo y telemetría

## 🤝 Contribuciones

Las contribuciones son bienvenidas. Para contribuir:

1. Haz fork del repositorio
2. Crea una rama para tu feature (`git checkout -b feature/amazing-feature`)
3. Realiza tus cambios
4. Commitea tus cambios (`git commit -m 'Add amazing feature'`)
5. Push a la rama (`git push origin feature/amazing-feature`)
6. Abre un Pull Request

## 📜 Licencia

Este proyecto está licenciado bajo la licencia Apache-2.0 - ver el archivo [LICENSE](LICENSE) para más detalles.

## 📞 Contacto

- Jimena Castillo Campos
- Adrian Monge Mairena

---

Desarrollado como parte del curso de Algoritmos y Estructuras de Datos I perteneciente al plan de estudio de la Ingeniería en Computadores (2103) del Tecnológico de Costa Rica (TEC) para el primer Semestre del 2025
