using SmugglerServer.Lib;
using TPSServer.Lib;

namespace SmugglerServer;

public class SmugglerWorld
{
    public static void Main(string[] args)
    {
        Log.PrintLog("Hello World");

        ThreadPool.GetAvailableThreads(out var count, out var iocount);
        Log.PrintLog($"Start - Available ThreadPool Threads: Worker={count}, IO={iocount}");

        ////////////////////////////////////////////////////////////////
        ushort port = NetConstants.DefaultPort;

        ServerManager server = new ServerManager();

        AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
        {
            server?.Stop();
        };

        try
        {
            if (!server.Initialize("127.0.0.1", port))
            {
                Log.PrintLog("Server Initialize Failed!", MsgLevel.Error);
                return;
            }

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            server.Run(cts.Token);

            // 정상 종료
            server.Stop();
            Log.PrintLog("Server terminated successfully", MsgLevel.Information);
        }
        catch (Exception ex)
        {
            Log.PrintLog($"Exception in Main: {ex.Message}\n{ex.StackTrace}", MsgLevel.Error);
        }

        // 1. config 파일 찾아서 설정값 불러오기
        // 2. UDP Server 생성
        // 3. 각종 관리/처리 객체들 호출
        // 4. Receive Packet을 받아서 처리하는 패킷 처리 파이프라인 프로세스
        // (내부에서 처리 후 다시 send하기 위한 부분은 내부에서 처리)
    }
}