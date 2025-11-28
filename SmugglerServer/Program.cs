using System.Text.Json;
using TPSServer.Lib;

namespace TPSServer;

public class Program
{
    private static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");

        var config = LoadConfig(args);

        // server manager 생성 및 동작
        ServerManager manager = new ServerManager(config.BindIP, config.Port, config.MaxClients);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        if (false == manager.Initialize(config.Port, config.MaxClients))
        {
            Console.WriteLine("Failed to initialize server manager.");
            return;
        }

        manager.Run(cts.Token);

        // 정상 종료

        Console.WriteLine("Server is shutting down gracefully.");
    }

    private record ServerConfig(string BindIP, ushort Port, int MaxClients)
    {
        public static ServerConfig Defaults => new("0.0.0.0", NetConstants.DefaultPort, 100);
    }

    private static ServerConfig LoadConfig(string[] args)
    {
        const string file = "appsettings.json";

        ServerConfig config = ServerConfig.Defaults;

        if (args is not null)
        {
            if (args.Length >= 2 &&
                args[0] is not null &&
                ushort.TryParse(args[1], out var port) &&
                int.TryParse(args[2], out var maxClients))
            {
                config = new ServerConfig(args[0], port, maxClients);
            }
            Console.WriteLine("Invalid command line arguments. Using config file or defaults.");
        }
        else  // null => default
        {
            try
            {
                if (File.Exists(file))
                {
                    var json = File.ReadAllText(file);
                    var cfg = JsonSerializer.Deserialize<ServerConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (cfg != null) config = cfg;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to read config: {ex.Message}. Using defaults.");
            }
        }

        return config;
    }
}