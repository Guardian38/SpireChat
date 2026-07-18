using System;

namespace SpireChat.Chat;

/// <summary>
/// 보낸 메시지를 그대로 자기에게 되돌려주는 전송 계층. **네트워크를 쓰지 않는다.**
///
/// 멀티플레이 세션을 띄우지 않고 채팅 UI를 개발·검증하기 위한 것이다.
/// 실제 전송(NetMessageChatTransport)에서도 자기 메시지는 호스트 중계를 거쳐 되돌아오므로,
/// UI 입장에서 보이는 흐름은 같다 — 지연과 발신자 id만 다르다.
/// </summary>
public sealed class LoopbackChatTransport : IChatTransport
{
    /// <summary>
    /// 루프백에는 실제 peer id가 없다. 실제 Steam id와 겹치지 않도록 0을 쓰고,
    /// UI는 이 값을 "나"로 표시한다.
    /// </summary>
    public const ulong LocalPeerId = 0uL;

    private bool _running;

    public event Action<ulong, string>? Received;

    public void Start()
    {
        _running = true;
    }

    public void Stop()
    {
        _running = false;
    }

    public void Send(string text)
    {
        if (!_running || string.IsNullOrEmpty(text))
        {
            return;
        }

        // 실제 전송이라면 여기서 네트워크를 타고 나갔다가 돌아온다.
        // 루프백은 그 왕복을 생략하고 곧바로 수신으로 넘긴다.
        Received?.Invoke(LocalPeerId, text);
    }
}
