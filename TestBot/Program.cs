using SmugglerLib.Commons;

namespace TestBot;

internal class Program
{
    private static void Main(string[] args)
    {
        Log.PrintLog("Hello, World! - Client Bot");

        int botcount = 10;
        Log.PrintLog("봇 몇개?(숫자 입력, 기본 10: 그냥 엔터)");
        var botcountstr = Console.ReadLine();
        if (false == Int32.TryParse(botcountstr, out botcount))
        {
            Log.PrintLog("숫자가 아니므로 10으로 시작함");
        }

        BotManager conn = new BotManager();
        if (false == conn.InitConnect())
        {
            Log.PrintLog("초기화 실패");
            conn.StopConnect();
            return;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        Log.PrintLog("Ctrl+C 를 누르면 정상 종료됨.");
        Log.PrintLog("Start Bot");
        conn.Running(cts.Token);

        conn.StopConnect();
    }
}