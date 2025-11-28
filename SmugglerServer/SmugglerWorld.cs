using System.Text.Json;
using ENet;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NLog.Extensions.Logging;
using SmugglerServer;
using TPSServer.Lib;

namespace TPSServer;

public class SmugglerWorld
{
    private static void Main(string[] args)
    {
        //HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(args);

        //// config
        //IHostEnvironment env = builder.Environment;
        //builder.Configuration
        //    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
        //    .AddJsonFile($"appsettings.{env.EnvironmentName}.json", true, true);

        ////Logging
        //using ILoggerFactory factory = LoggerFactory.Create(builder =>
        //                       builder.AddSimpleConsole(options =>
        //                       {
        //                           options.IncludeScopes = true;
        //                           options.SingleLine = true;
        //                           options.TimestampFormat = "yy-MM-dd HH:mm:ss ";
        //                       }));
        //builder.Services.AddLogging(loggingBuilder =>
        //{
        //    loggingBuilder.ClearProviders();
        //    loggingBuilder.AddNLog();
        //});

        //// logger
        //ILogger logger = factory.CreateLogger("Program");
        //using (logger.BeginScope("[scope is enabled]"))
        //{
        //    logger.LogInformation("Hello World!");
        //    logger.LogDebug("This is a debug message.");
        //    logger.LogInformation("Logs contain timestamp and log level.");
        //    logger.LogInformation("Each log message is fit in a single line.");
        //}

        //builder.Services.AddSingleton<UDPServer>();

        //builder.Services.AddSingleton<RoomManager>();
        //builder.Services.AddSingleton<SessionStore>();
        //builder.Services.AddSingleton<ReadyHandler>();
        //builder.Services.AddSingleton<RoomService>(sp =>
        //{
        //    return new RoomService(
        //        sp.GetRequiredService<RoomManager>(),
        //        sp.GetRequiredService<ReadyHandler>(),
        //        sp.GetRequiredService<SessionStore>());
        //});
        //builder.Services.AddSingleton<StateHandler>(sp =>
        //{
        //    return new StateHandler(sp.GetRequiredService<RoomManager>());
        //});

        //builder.Services.AddSingleton<BroadcastService>(sp =>
        //{
        //    return new BroadcastService(sp.GetRequiredService<RoomManager>());
        //});

        //builder.Services.AddTransient<Room>();

        //builder.Services.AddSingleton<ProcessingPipeline>(sp =>
        //{
        //    return new ProcessingPipeline(256, 1024);
        //});

        //////////////////////////////////////////////////////////////////////////////////
        //// 객체 등록 완료 및 아래에서 메인내용 호출
        //IHost host = builder.Build();

        //// 초기 호출 부분

        //var server = host.Services.GetRequiredService<UDPServer>();
        //var rooms = host.Services.GetRequiredService<RoomManager>();
        //var manager = host.Services.GetRequiredService<ServerManager>();
        //manager.Initialize(7777, 1000);

        //using var pipeline = host.Services.GetRequiredService<ProcessingPipeline>();
        //var roomService = host.Services.GetRequiredService<RoomService>();
        //var stateHandler = host.Services.GetRequiredService<StateHandler>();
        //var broadcaster = host.Services.GetRequiredService<BroadcastService>();

        //pipeline.Start(
        //    lobbyHandler: packet => HandleLobby(packet, rooms, roomService),
        //    gameHandler: packet => HandleInGame(packet, stateHandler, broadcaster));

        //host.Run();

        //logger.LogInformation("Finish : Smuggler server");
    }

    //private static void HandleLobby(ReceivedPacket packet, RoomManager rooms, RoomService roomService)
    //{
    //    if (!TpsCodec.TryParsePacket(packet.Buffer, out var parsed))
    //        return;

    //    var header = parsed.Header.Value;
    //    if (header.Type != TPS.MsgType.RoomJoinReq)
    //        return;

    //    var peer = packet.Peer;
    //    var roomId = header.RoomId;
    //    var nickname = parsed.RoomJoinReq.Value.Nickname ?? $"peer{peer.ID}";

    //    if (!rooms.TryGetRoom(roomId, out Room _))
    //    {
    //        var created = roomService.CreateRoom($"room{roomId}", capacity: 5);
    //        if (!created.ok)
    //        {
    //            var resBytes = TpsCodec.BuildRoomJoinRes(roomId, 0, false, created.reason ?? "room create failed");
    //            Packet resp = default; resp.Create(resBytes, PacketFlags.Reliable);
    //            peer.Send((byte)NetConstants.ReliableChannel, ref resp);
    //            return;
    //        }
    //    }

    //    var join = roomService.JoinRoom(peer.ID, roomId, nickname);
    //    var joinResBytes = TpsCodec.BuildRoomJoinRes(roomId, join.playerId, join.ok, join.reason ?? "");
    //    Packet jr = default; jr.Create(joinResBytes, PacketFlags.Reliable);
    //    peer.Send((byte)NetConstants.ReliableChannel, ref jr);

    //    if (join.ok && rooms.TryGetRoom(roomId, out Room room))
    //    {
    //        foreach (var pid in room.PlayerIds)
    //        {
    //            if (pid == join.playerId) continue;
    //            if (room.TryGetPeerId(pid, out var otherPeerId) && rooms.TryGetSession(otherPeerId, out var otherSess))
    //            {
    //                var existingState = TpsCodec.BuildMoveState(roomId, pid, otherSess.X, otherSess.Y, otherSess.Z);
    //                Packet p = default; p.Create(existingState, PacketFlags.Reliable);
    //                peer.Send((byte)NetConstants.ReliableChannel, ref p);

    //                var newState = TpsCodec.BuildMoveState(roomId, join.playerId, 0, 0, 0);
    //                Packet p2 = default; p2.Create(newState, PacketFlags.Reliable);
    //                otherSess.Peer.Send((byte)NetConstants.ReliableChannel, ref p2);
    //            }
    //        }
    //    }
    //}

    //private static void HandleInGame(ReceivedPacket packet, StateHandler stateHandler, BroadcastService broadcaster)
    //{
    //    if (!TpsCodec.TryParsePacket(packet.Buffer, out var parsed))
    //        return;

    //    var header = parsed.Header.Value;
    //    if (header.Type != TPS.MsgType.MoveInput)
    //        return;

    //    if (stateHandler.TryHandleMoveInput(packet.Peer.ID, parsed, out var stateBytes, out var reason))
    //    {
    //        var roomId = parsed.Header.Value.RoomId;
    //        broadcaster.BroadcastState(roomId, stateBytes, packet.Peer.ID);
    //    }
    //    else if (!string.IsNullOrEmpty(reason))
    //    {
    //        Console.WriteLine($"Drop MoveInput from {packet.Peer.ID}: {reason}");
    //    }
    //}

    //private record ServerConfig(string BindIP, ushort Port, int MaxClients)
    //{
    //    public static ServerConfig Defaults => new("0.0.0.0", NetConstants.DefaultPort, 100);
    //}

    //private static ServerConfig LoadConfig(string[] args)
    //{
    //    const string file = "appsettings.json";

    //    ServerConfig config = ServerConfig.Defaults;

    //    if (args is not null && args.Length > 1)
    //    {
    //        if (args.Length >= 2 &&
    //            args[0] is not null &&
    //            ushort.TryParse(args[1], out var port) &&
    //            int.TryParse(args[2], out var maxClients))
    //        {
    //            config = new ServerConfig(args[0], port, maxClients);
    //        }
    //        Console.WriteLine("Invalid command line arguments. Using config file or defaults.");
    //    }
    //    else  // null => default
    //    {
    //        try
    //        {
    //            if (File.Exists(file))
    //            {
    //                var json = File.ReadAllText(file);
    //                var cfg = JsonSerializer.Deserialize<ServerConfig>(json, new JsonSerializerOptions
    //                {
    //                    PropertyNameCaseInsensitive = true
    //                });
    //                if (cfg != null) config = cfg;
    //            }
    //        }
    //        catch (Exception ex)
    //        {
    //            Console.WriteLine($"Failed to read config: {ex.Message}. Using defaults.");
    //        }
    //        Console.WriteLine("Invalid command line arguments. Using defaults config");
    //    }

    //    return config;
    //}
}