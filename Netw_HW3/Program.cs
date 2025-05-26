using System.Net.Sockets;
using System.Text;

public interface INetworkClient
{
    Task ConnectAsync(string serverIp, int port, string username, string password);
    Task SendMessageAsync(string message);
    void Disconnect();
}

public interface ILoggerService
{
    void Log(string message);
    void LogError(string message);
    void LogClear();
    void LogSetCursorPosition(int x, int y);
}

public class ConsoleLogger : ILoggerService
{
    public void Log(string message) => Console.WriteLine(message);
    public void LogError(string message) => Console.Error.WriteLine(message);
    public void LogClear() => Console.Clear();
    public void LogSetCursorPosition(int x, int y) => Console.SetCursorPosition(x, y);
}

public class TcpNetworkClient : INetworkClient
{
    private readonly ILoggerService logger;
    private TcpClient? client;
    private string? ip;
    private int port;
    private bool isAuthenticated = false;

    public TcpNetworkClient(ILoggerService logger)
    {
        this.logger = logger;
    }

    public async Task ConnectAsync(string serverIp, int port, string username, string password)
    {
        ip = serverIp;
        this.port = port;

        while (true)
        {
            try
            {
                client = new TcpClient();
                await client.ConnectAsync(serverIp, port);
                logger.Log("Подключение к серверу прошло успешно.");

                var authMessage = $"{username};{password}";
                var stream = client.GetStream();
                var authBytes = Encoding.UTF8.GetBytes(authMessage);
                await stream.WriteAsync(authBytes, 0, authBytes.Length);

                isAuthenticated = true;
                break;
            }
            catch (Exception ex)
            {
                logger.LogError($"Ошибка подключения или аутентификации: {ex.Message}. Повторная попытка через 5 секунд...");
                await Task.Delay(5000);
            }
        }
    }

    public async Task SendMessageAsync(string message)
    {
        if (!isAuthenticated || client == null || !client.Connected)
        {
            logger.LogError("Клиент не аутентифицирован или отключён.");
            return;
        }

        try
        {
            var stream = client.GetStream();
            var data = Encoding.UTF8.GetBytes(message);
            await stream.WriteAsync(data, 0, data.Length);

            var buffer = new byte[512];
            int bytesRead = await stream.ReadAsync(buffer);
            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            logger.Log($"Ответ от сервера: {response}");
        }
        catch (Exception ex)
        {
            logger.LogError($"Ошибка отправки сообщения: {ex.Message}");
            isAuthenticated = false;
        }
    }

    public void Disconnect()
    {
        client?.Close();
        logger.Log("Клиент отключён.");
    }
}

class Program
{
    static async Task Main()
    {
        var logger = new ConsoleLogger();
        var client = new TcpNetworkClient(logger);

        logger.Log("Введите имя пользователя: ");
        string username = Console.ReadLine()?.Trim() ?? "";

        logger.Log("Введите пароль: ");
        string password = Console.ReadLine()?.Trim() ?? "";

        await client.ConnectAsync("127.0.0.1", 27015, username, password);
        logger.LogClear();

        while (true)
        {
            logger.LogSetCursorPosition(0,0);
            logger.Log("Нажмите на соответствующую клавишу");
            logger.Log("[1] - EUR USD\n[2] - USD EUR\n[_] - Выйти");
            string message = "4";
            switch (Console.ReadKey(true).KeyChar)
            {
                case '1': message = "EUR USD"; break;
                case '2': message = "USD EUR"; break;
                default: message = "_"; break;
            }
            if (message == "_") break;
            if (!string.IsNullOrEmpty(message))
            {
                await client.SendMessageAsync(message);
            }
        }

        client.Disconnect();
    }
}
