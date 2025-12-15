using System.Collections.Concurrent;
using System.Numerics;
using ENet;
using SmugglerLib.Commons;

namespace TestBot;

public class BotManager
{
    //private Dictionary<int, Host> m_DicClient;
    //private Dictionary<int, Peer> m_DicPeer;

    private Dictionary<int, Bot> m_DicBot;
    private Address m_address;

    private int BotCount;

    public BotManager(int nCount = 10)
    {
        BotCount = nCount;
        m_DicBot = new Dictionary<int, Bot>(nCount);
    }

    public bool InitConnect()
    {
        Library.Initialize();

        Log.PrintLog($"Start: Library.Initialize");

        m_address = new Address();
        m_address.SetHost("127.0.0.1");
        m_address.Port = 7775;

        Log.PrintLog($"Connect Bot: {BotCount}");

        int nConnectedCount = 0;

        for (int i = 0; i < BotCount; i++)
        {
            var bot = new Bot(i);
            if (false == bot.Connect(m_address, 2))
            {
                continue;
            }

            m_DicBot.Add(nConnectedCount++, bot);
        }

        return true;
    }

    public void Running(CancellationToken token)
    {
        long updateTime = 0;

        float m_lastUpdateTime = Library.Time;
        float m_lastStatusTime = Library.Time;

        while (!token.IsCancellationRequested)
        {
            float now = Library.Time;

            var deltatime = now - m_lastUpdateTime;
            m_lastUpdateTime = now;

            foreach (var bot in m_DicBot)
            {
                bot.Value.UpdateRunning(deltatime);
            }

            var statusElapsed = now - m_lastUpdateTime;

            if (statusElapsed > 5.0f)
            {
                PrintStatus();
                statusElapsed = now;
            }

            Thread.Sleep(10);
        }
    }

    private void PrintStatus()
    {
        var total = m_DicBot.Count;
        int connecting = 0, authenticating = 0, waitingRoom = 0, loading = 0, playing = 0, disconnected = 0;

        foreach (var bot in m_DicBot)
        {
            switch (bot.Value.GetState())
            {
                case BotState.Disconnected: ++disconnected; break;
                case BotState.Connecting: ++connecting; break;
                case BotState.Authenticating: ++authenticating; break;
                case BotState.WaitingRoom: ++waitingRoom; break;
                case BotState.Loading: ++loading; break;
                case BotState.Playing: ++playing; break;
            }
        }
        Log.PrintLog($"[Status] Total: {total} Conn: {connecting} Auth: {authenticating} Wait: {waitingRoom} Load:{loading} Play: {playing} Disc: {disconnected}");
    }

    public void StopConnect()
    {
        Log.PrintLog("Stop Connect. Library.Deinitialize");
        Library.Deinitialize();
    }
}