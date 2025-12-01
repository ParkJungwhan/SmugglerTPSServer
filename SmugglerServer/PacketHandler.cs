using ENet;
using Google.FlatBuffers;

namespace SmugglerServer;

internal class PacketHandler
{
    private Dictionary<int, Action<Peer>> HandlerDic;

    internal void RegisterHandler<T>(int protocol, Action<Peer, T> value) where T : struct, IFlatbufferObject
    {
    }

    //public virtual void RegisterHandler(int protocol, Action<Peer> action)
    //    => this.HandlerDic.Add(protocol, (Action<Peer>)(packet => action(packet)));

    //public delegate bool HandlerFunc(Peer peer, byte[] data, int dataSize);

    //private readonly Dictionary<int, HandlerFunc> _handlers = new();

    //// template <typename DispatcherType, typename MessageType>
    //public void RegisterHandler<TDispatcher, TMessage>(
    //    Func<TDispatcher, Peer, TMessage, bool> handler,   // (instance, peer, msg) -> bool
    //    TDispatcher instance,
    //    int messageId)
    //{
    //    bool Invoker(Peer peer, byte[] data, int dataSize)
    //    {
    //        // --- FlatBuffers 검증 부분 (라이브러리에 맞게 구현해야 함) ---

    //        // 예: data[0..dataSize]만 잘라서 쓴다고 가정
    //        // ReadOnlySpan<byte> span = new ReadOnlySpan<byte>(data, 0, dataSize);

    //        // TODO: 사용 중인 FlatBuffers C# API에 맞게 검증 구현
    //        // 예시 (FlatSharp 같은 걸 쓴다면):
    //        // if (!FlatBufferSerializer.Default.TryParse<TMessage>(span, out var msg))
    //        //     return false;

    //        // 여기서는 구조만 맞춰서 작성
    //        TMessage msg = GetRoot<TMessage>(data, dataSize);
    //        if (msg == null)
    //            return false;

    //        // C++: (instance->*handler)(peer, *msg);
    //        return handler(instance, peer, msg);
    //    }

    //    _handlers[messageId] = Invoker;
    //}

    //// C++의 flatbuffers::GetRoot<MessageType>(data) 에 해당하는 부분을
    //// 실제 사용하는 FlatBuffers C# 라이브러리에 맞게 구현해야 함.
    //private static TMessage GetRoot<TMessage>(byte[] data, int dataSize)
    //{
    //    // ↓↓ 여기는 실제 환경에 맞게 바꿔 써야 하는 자리 ↓↓

    //    // 예) FlatSharp 사용 시:
    //    // ReadOnlyMemory<byte> mem = new ReadOnlyMemory<byte>(data, 0, dataSize);
    //    // return FlatBufferSerializer.Default.Parse<TMessage>(mem);

    //    // 예) Google.FlatBuffers 사용 시 (MessageType에 맞는 GetRootAsXXX 호출 필요)
    //    // var bb = new ByteBuffer(data);
    //    // return MessageType.GetRootAsMessageType(bb); // 이 부분은 제너릭으로는 직접 호출 불가

    //    throw new NotImplementedException("FlatBuffers GetRoot<TMessage> 구현 필요");
    //}
}