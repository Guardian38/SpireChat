using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace SpireChat.Chat.Net;

/// <summary>
/// 채팅 한 줄을 나르는 네트워크 메시지.
///
/// **등록 코드가 없는 것이 정상이다.** <c>MessageTypes.Initialize()</c>가
/// <c>ReflectionHelper.GetSubtypesInMods&lt;INetMessage&gt;()</c>로 모드 어셈블리를 스캔해
/// 자동으로 잡아간다(MessageTypes.cs:13-19).
///
/// 발신자 id 필드가 없는 이유: <c>NetMessageBus</c>가 수신 시 senderId를 자동으로 붙여준다
/// (NetMessageBus.cs:34-42). 굳이 실으면 위조 가능한 중복 정보가 될 뿐이다.
///
/// 설계 근거는 info_document/network_design.md §4.1. 템플릿은 vanilla <c>ReactionMessage</c>다.
/// </summary>
public struct ChatNetMessage : INetMessage, IPacketSerializable
{
    public string text;

    /// <summary>호스트가 나머지 전원에게 자동 중계한다. 단 **발신자 자신은 제외된다** — §5.2.</summary>
    public bool ShouldBroadcast => true;

    /// <summary>리액션과 달리 채팅은 유실되면 안 된다. Reliable을 쓴다.</summary>
    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.VeryDebug;

    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(text);
    }

    public void Deserialize(PacketReader reader)
    {
        text = reader.ReadString();
    }
}
