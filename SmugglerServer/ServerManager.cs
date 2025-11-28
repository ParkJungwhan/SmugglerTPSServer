using System.Diagnostics;
using ENet;

namespace TPSServer;

internal class ServerManager
{
    private string bindIP;
    private ushort port;
    private int maxClients;

    private Host? server;
    private Address address;

    public ServerManager(string bindIP, ushort port, int maxClients)
    {
        this.bindIP = bindIP;
        this.port = port;
        this.maxClients = maxClients;
    }

    internal bool Initialize(ushort port, int maxClients, int channels = 2)
    {
        ENet.Library.Initialize();

        server = new Host();
        address = new Address();
        address.SetIP(bindIP);
        address.Port = port;

        server.Create(address, maxClients, channels);

        Debug.Assert(server != null);

        return true;
    }

    internal void Run(System.Threading.CancellationToken cancellationToken)
    {
        Debug.Assert(server != null);

        Event netEvent;
        while (!cancellationToken.IsCancellationRequested)
        {
            var polled = false;
            while (!polled)
            {
                if (server!.CheckEvents(out netEvent) <= 0)
                {
                    if (server.Service(15, out netEvent) <= 0)
                        break;
                    polled = true;
                }
                // 서버 루프 구현
            }
        }

        // End
        server!.Flush();
        server.Dispose();
        ENet.Library.Deinitialize();
    }
}