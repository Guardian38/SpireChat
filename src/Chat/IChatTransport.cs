using System;

namespace SpireChat.Chat;

/// <summary>
/// 채팅 메시지 송수신 경로. **채팅 UI는 이 인터페이스만 알고 전송 방식은 모른다.**
///
/// 분리하는 이유는 주로 테스트다 — <see cref="LoopbackChatTransport"/>가 있으면
/// 멀티플레이 세션 없이 혼자서 채팅 UI를 개발·검증할 수 있다. 부차적으로,
/// 전송 방식을 바꿔야 할 때 UI를 건드리지 않는다.
///
/// 구현체:
///  - <see cref="LoopbackChatTransport"/> — 개발·테스트용. 보낸 것을 자기에게 되돌려준다.
///  - NetMessageChatTransport — 본 구현(예정). 게임의 INetMessage 확장점 사용.
/// </summary>
public interface IChatTransport
{
    /// <summary>메시지를 보낸다. <see cref="Start"/> 전이나 <see cref="Stop"/> 후에는 무시된다.</summary>
    void Send(string text);

    /// <summary>
    /// 메시지를 받았을 때 발생한다. 인자는 (발신자 peer id, 본문).
    /// 자기가 보낸 메시지도 여기로 돌아온다 — UI는 이 이벤트만으로 목록을 갱신하면 된다.
    /// </summary>
    event Action<ulong, string>? Received;

    void Start();

    void Stop();
}
