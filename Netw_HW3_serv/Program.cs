using System.Net;
using System.Net.Sockets;
using System.Text;

public interface INetworkServer
{
    Task StartAsync(CancellationToken cancellationToken);
    void Stop();
}

public interface IMessageHandler
{
    Task HandleMessageAsync(TcpClient client, string message);
}

public interface ILoggerService
{
    void Log(string message);
    void LogError(string message);
}

public class ConsoleLogger : ILoggerService
{
    public void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH-mm-ss}] - {message}");
    public void LogError(string message) => Console.Error.WriteLine(message);
}

public class EchoMessageHandler : IMessageHandler
{
    private Random rnd = new Random();
    private readonly ILoggerService logger;

    public EchoMessageHandler(ILoggerService logger)
    {
        this.logger = logger;
    }

    public async Task HandleMessageAsync(TcpClient client, string message)
    {
        var response = rnd.Next(10).ToString();
        switch (message)
        {
            case "EUR USD": response = "1.14"; break;
            case "USD EUR": response = "0.88"; break;
        }
        byte[] responseBytes = Encoding.UTF8.GetBytes(response);
        try
        {
            await client.GetStream().WriteAsync(responseBytes, 0, responseBytes.Length);
            logger.Log($"Ответ отправлен: {response}");
        }
        catch
        {
            logger.LogError("Не удалось отправить ответ клиенту.");
        }
    }
}

public class TcpNetworkServer : INetworkServer
{
    private readonly int port;
    private readonly IMessageHandler messageHandler;
    private readonly ILoggerService logger;
    private TcpListener? listener;
    private readonly CancellationTokenSource token = new();
    
    Dictionary<string, string> allowedUsers = new Dictionary<string, string>
    {
        { "Test1", "Password1" },
        { "Test2", "Password2" },
        { "Test3", "Password3" },
        { "Test4", "Password4" },
        { "Test5", "Password5" }
    };
    
    public TcpNetworkServer(int port, IMessageHandler messageHandler, ILoggerService logger)
    {
        this.port = port;
        this.messageHandler = messageHandler;
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        logger.Log($"Сервер запущен на порту {port}");
        logger.Log($"Ожидается подключение клиентов...");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync();
                logger.Log("Клиент подключился.");
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Ошибка сервера: {ex.Message}");
        }
    }

    public void Stop()
    {
        token.Cancel();
        listener?.Stop();
        logger.Log("Сервер остановлен.");
    }
 
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var buffer = new byte[512];
        int tries = 10;
        string[] t = new []{"", ""};
        try
        {
            var stream = client.GetStream();
            if (!cancellationToken.IsCancellationRequested && client.Connected)
            {
                int bytesRead = await stream.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0) return;
                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                try
                {
                    t = message.Split(";");
                    if (allowedUsers.TryGetValue(t[0], out string? passw))
                    {
                        if (t[1] == passw)
                        {
                            allowedUsers.Remove(t[0]);
                        }
                        else
                        {
                            client.Close();
                            return;
                        }
                    }
                    else
                    {
                        client.Close();
                        return;
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return;
                }
            }
            else
            {
                client.Close();
                return;
            }

            var y = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    tries = 10;
                    await Task.Delay(60000);
                }
            });
            
            while (!cancellationToken.IsCancellationRequested && client.Connected)
            {
                Console.WriteLine($"[{DateTime.Now:HH-mm-ss}] - {string.Join(';', allowedUsers)}");
                int bytesRead = await stream.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0) break;
                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                logger.Log($"Сообщение от клиента: {message}");
                await messageHandler.HandleMessageAsync(client, message);
                tries--;
                
                if (tries < 0)
                {
                    client.Close();
                    Task.Run(async () =>
                    {
                        await Task.Delay(60000);
                        allowedUsers.Add(t[0], t[1]);
                    });
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Ошибка обработки клиента: {ex.Message}");
        }
        finally
        {
            client.Close();
            logger.Log("Клиент отключился.");
            Console.WriteLine($"[{DateTime.Now:HH-mm-ss}] - {string.Join(';', allowedUsers)}");
        }
    }
}

class Program
{
    static async Task Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "SERVER SIDE";
        var logger = new ConsoleLogger();
        var messageHandler = new EchoMessageHandler(logger);
        var server = new TcpNetworkServer(27015, messageHandler, logger);

        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            server.Stop();
        };

        await server.StartAsync(CancellationToken.None);
    }
}