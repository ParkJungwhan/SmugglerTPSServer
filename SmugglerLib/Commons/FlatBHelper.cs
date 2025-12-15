using Google.FlatBuffers;
using Protocol;

namespace SmugglerLib.Commons;

public static class FlatBHelper
{
    /// <summary>
    /// Smuggler: Extend Method
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="objectInfo"></param>
    /// <returns></returns>
    public static VectorOffset AddBuilderOffsetToSingleMObj(
        this FlatBufferBuilder builder,
        Offset<MObjectInfo> objectInfo)
    {
        List<Google.FlatBuffers.Offset<MObjectInfo>> syncList = new();
        syncList.Add(objectInfo);
        return builder.CreateVectorOfTables(syncList.ToArray());
    }

    /// <summary>
    /// Smuggler: Extend Method
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="builder"></param>
    /// <param name="offset"></param>
    public static void Finish<T>(this FlatBufferBuilder builder, Offset<T> offset) where T : struct
    {
        builder.Finish(offset.Value);
    }
}