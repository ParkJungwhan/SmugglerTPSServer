using System.Buffers.Binary;
using ENet;
using Google.FlatBuffers;
using Protocol;
using SmugglerLib.Commons;

namespace SmugglerServer;

internal class PacketHandler
{
    public delegate bool HandlerFunc(Peer peer, byte[] buffer);

    private readonly Dictionary<int, HandlerFunc> _handlers = new();

    public void RegisterHandler<TMessage>(
        Func<Peer, TMessage, bool> handler,               // 실제 비즈니스 로직
                                                          // Func<ByteBuffer, bool> verifyBuffer,                  // 예: LoginRequest.VerifyLoginRequest
        Func<ByteBuffer, TMessage> getRoot,                   // 예: LoginRequest.GetRootAsLoginRequest
        EProtocol messageId)
    {
        bool Invoker(Peer peer, byte[] bb)
        {
            // 1) FlatBuffer 검증
            //if (!verifyBuffer(bb))
            //    return false;

            // bb: byte[] 형태
            // fbDataOffset: 보통 4
            //Verifier verifier = new Verifier(new ByteBuffer(bb,4));
            //Verifier verifier = new Verifier(new ByteBuffer(bb));
            //Verifier verifier = new Verifier();
            //verifier.VerifyBuffer(typeof(TMessage).Name, bb.Length, )

            //Verifier verifier = new Verifier(new ByteBuffer(bb, 4));
            //if (CLAuthRequestVerify.Verify(verifier, 0))
            //{
            //    return false;
            //}

            //if (!MessageType.VerifyMessageType(bbs))
            //{
            //    return false;
            //}

            // CLAuthRequest.GetRootAsCLAuthRequest

            // 2) 루트 객체 읽기
            var msg = getRoot(new ByteBuffer(bb));

            // struct 타입이면 null 체크 무의미하지만, C++ 구조를 맞춰두자
            if (msg == null)
                return false;

            // 3) 실제 핸들러 호출
            return handler(peer, msg);
        }

        _handlers[(int)messageId] = Invoker;
    }

    public bool Dispatch(Peer peer, byte[] data, int dataSize)
    {
        if (data == null || dataSize == 0)
        {
            Log.PrintLog("Invalid packet data", MsgLevel.Error);
            Console.Error.WriteLine("Invalid packet data");
            return false;
        }

        int messageId = ExtractMessageId(data, dataSize);
        if (messageId == 0)
        {
            Log.PrintLog("Failed to extract message ID", MsgLevel.Error);
            return false;
        }

        if (!_handlers.TryGetValue(messageId, out var handler))
        {
            Log.PrintLog($"No handler registered for message ID: {messageId}", MsgLevel.Error);
            return false;
        }

        if (dataSize < 4)
        {
            Log.PrintLog("Packet too small", MsgLevel.Error);
            return false;
        }

        ByteBuffer bb = new ByteBuffer(data);
        ReadOnlySpan<byte> fbData = new ReadOnlySpan<byte>(data, 4, dataSize - 4);
        return handler(peer, fbData.ToArray());
    }

    internal int ExtractMessageId(byte[] data, int dataSize)
    {
        // 패킷 구조: [4 bytes EProtocol:int][FlatBuffer 데이터]
        // 앞 4바이트를 읽어서 프로토콜 ID를 추출
        if (data == null || dataSize < 4) return 0;

        int protocolId = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(0, 4));

        return protocolId;
    }
}