using ENet;
using SmugglerLib.Commons;

namespace TestBot;

public class BotManager
{
    private Dictionary<int, Bot> m_DicBot;
    private Address m_address;

    private int BotCount;
    private bool m_running;
    private uint m_lastUpdateLibTime;
    private uint m_lastStatusLibTime;

    public BotManager(int nCount = 10)
    {
        BotCount = nCount;
        m_DicBot = new Dictionary<int, Bot>(nCount);
        m_running = false;
    }

    public bool InitConnect()
    {
        Library.Initialize();

        Log.PrintLog($"Start: Library.Initialize");

        m_address = new Address();
        m_address.SetHost("127.0.0.1");
        m_address.Port = 7775;

        Log.PrintLog($"Connect Bot: {BotCount}");

        for (int i = 0; i < BotCount; i++)
        {
            var bot = new Bot(i);
            if (false == bot.Connect(m_address, 2))
            {
                Log.PrintLog($"No Connected Bot : {i}");
                continue;
            }

            Log.PrintLog($"Connected Bot : {i}");
            m_DicBot.Add(i + 1, bot);
        }

        Thread.Sleep(20);

        return true;
    }

    public void Running(CancellationToken token)
    {
        m_running = true;

        m_lastUpdateLibTime = Library.Time;
        m_lastStatusLibTime = Library.Time;

        while (false == token.IsCancellationRequested)
        {
            var nowtime = Library.Time;
            var deltaLibtime = nowtime - m_lastUpdateLibTime;
            m_lastUpdateLibTime = nowtime;

            foreach (var bot in m_DicBot)
            {
                bot.Value.UpdateRunning(deltaLibtime);
            }

            // 5초간 계속 갱신이라는데...아닌듯?
            var statusLibElapsed = nowtime - m_lastStatusLibTime;
            if (statusLibElapsed >= 5000.0f)
            {
                Log.PrintLog($"call PrintStatus : {statusLibElapsed}");    // 10
                PrintStatus();
                m_lastStatusLibTime = nowtime;
            }

            // 10ms 주기로 돌기
            Thread.Sleep(10);
        }
        m_running = false;
    }

    private void PrintStatus()
    {
        var total = m_DicBot.Count;
        int connecting = 0, authenticating = 0, waitingRoom = 0, loading = 0, playing = 0, disconnected = 0;

        disconnected = m_DicBot.Values.Count(x => x.GetState() == BotState.Disconnected);
        connecting = m_DicBot.Values.Count(x => x.GetState() == BotState.Connecting);
        authenticating = m_DicBot.Values.Count(x => x.GetState() == BotState.Authenticating);
        waitingRoom = m_DicBot.Values.Count(x => x.GetState() == BotState.WaitingRoom);
        loading = m_DicBot.Values.Count(x => x.GetState() == BotState.Loading);
        playing = m_DicBot.Values.Count(x => x.GetState() == BotState.Playing);

        //foreach (var bot in m_DicBot)
        //{
        //    switch (bot.Value.GetState())
        //    {
        //        case BotState.Disconnected: ++disconnected; break;
        //        case BotState.Connecting: ++connecting; break;
        //        case BotState.Authenticating: ++authenticating; break;
        //        case BotState.WaitingRoom: ++waitingRoom; break;
        //        case BotState.Loading: ++loading; break;
        //        case BotState.Playing: ++playing; break;
        //    }
        //}
        Log.PrintLog($"[Status] Total: {total} Conn: {connecting} Auth: {authenticating} Wait: {waitingRoom} Load:{loading} Play: {playing} Disc: {disconnected}");
    }

    public void StopConnect()
    {
        Log.PrintLog("Stop Connect. Library.Deinitialize");
        Library.Deinitialize();
    }
}