using SmugglerLib.Commons;

namespace SmugglerServer;

public class SmugglerWorld
{
    public static void Main(string[] args)
    {
        Log.Print("Hello World");

        ThreadPool.GetAvailableThreads(out var count, out var iocount);
        Log.Print($"Start - Available ThreadPool Threads: Worker={count}, IO={iocount}");

        ushort port = NetConstants.DefaultPort;

        //Signal 서버 설정 끝

        ////////////////////////////////////////////////////////////////

        ServerManager server = new ServerManager();

        AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
        {
            server?.Stop();
        };

        try
        {
            if (!server.Initialize("127.0.0.1", port))
            {
                Log.Print("Server Initialize Failed!", MsgLevel.Error);
                return;
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            server.Run(cts.Token);

            // 정상 종료
            server.Stop();
            Log.Print("Server terminated successfully", MsgLevel.Information);
        }
        catch (Exception ex)
        {
            Log.Print($"Exception in Main: {ex.Message}\n{ex.StackTrace}", MsgLevel.Error);
        }
    }
}