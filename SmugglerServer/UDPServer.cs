using System.Diagnostics;
using ENet;

namespace SmugglerServer;

public class UDPServer
{
    private Host? server;
    private Address address;

    public delegate void UDPEvent(object sender, Event netEvent);

    public event UDPEvent? evNetEvent;

    public bool Make(ushort port = 54321, int maxClients = 100, string bindIp = "0.0.0.0", int channels = 2)
    {
        ENet.Library.Initialize();

        server = new Host();
        address = new Address();
        address.SetIP(bindIp);
        address.Port = port;

        //server.Create(address, maxClients, (uint)channels);
        server.Create(address, maxClients, channels);

        Debug.Assert(server != null);
        return server.IsSet;
    }

    public void Run(System.Threading.CancellationToken cancellationToken)
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

                switch (netEvent.Type)
                {
                    case EventType.None:
                        break;

                    case EventType.Connect:
                        Console.WriteLine($"Client connected - ID: {netEvent.Peer.ID}, IP: {netEvent.Peer.IP}");
                        evNetEvent?.Invoke(this, netEvent);
                        break;

                    case EventType.Disconnect:
                        Console.WriteLine($"Client disconnected - ID: {netEvent.Peer.ID}, IP: {netEvent.Peer.IP}");
                        evNetEvent?.Invoke(this, netEvent);
                        break;

                    case EventType.Timeout:
                        Console.WriteLine($"Client timeout - ID: {netEvent.Peer.ID}, IP: {netEvent.Peer.IP}");
                        evNetEvent?.Invoke(this, netEvent);
                        break;

                    case EventType.Receive:
                        evNetEvent?.Invoke(this, netEvent);
                        Console.WriteLine($"Packet received - Peer: {netEvent.Peer.ID}, Ch: {netEvent.ChannelID}, Len: {netEvent.Packet.Length}");
                        break;
                }
            }
        }

        server!.Flush();
        server.Dispose();
        ENet.Library.Deinitialize();
    }
}