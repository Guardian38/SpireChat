using System;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace SpireChat.Chat.Net;

/// <summary>
/// 실제 네트워크 전송. 게임의 <c>INetMessage</c> 확장점을 그대로 쓴다.
///
/// vanilla <c>ReactionSynchronizer</c>와 같은 형태다 — 생성자에서 핸들러를 등록하고
/// <see cref="Stop"/>에서 해제한다. 설계 근거는 info_document/network_design.md §5.2.
///
/// 서비스 인스턴스는 **주입받는다.** 어디서 얻을지는 <see cref="NetServiceTracker"/>가 알고,
/// 이 클래스는 주어진 서비스로 주고받기만 한다.
/// </summary>
public sealed class NetMessageChatTransport : IChatTransport
{
    private readonly INetGameService _netService;
    private bool _running;

    public event Action<ulong, string>? Received;

    public NetMessageChatTransport(INetGameService netService)
    {
        _netService = netService ?? throw new ArgumentNullException(nameof(netService));
    }

    /// <summary>로컬 플레이어의 peer id. UI가 자기 메시지를 구분하는 데 쓴다.</summary>
    public ulong LocalPeerId => _netService.NetId;

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _netService.RegisterMessageHandler<ChatNetMessage>(HandleChatMessage);
        _running = true;

        Log.Info($"[{ModEntry.ModId}] chat transport started (netId={_netService.NetId}, type={_netService.Type}).");
    }

    public void Stop()
    {
        if (!_running)
        {
            return;
        }

        _netService.UnregisterMessageHandler<ChatNetMessage>(HandleChatMessage);
        _running = false;

        Log.Info($"[{ModEntry.ModId}] chat transport stopped.");
    }

    public void Send(string text)
    {
        if (!_running || string.IsNullOrEmpty(text))
        {
            return;
        }

        if (!_netService.IsConnected)
        {
            Log.Warn($"[{ModEntry.ModId}] chat send skipped — net service is not connected.");
            return;
        }

        _netService.SendMessage(new ChatNetMessage { text = text });

        // **로컬 에코는 우리가 만든다.** ShouldBroadcast=true라도 호스트는 발신자를 중계
        // 대상에서 제외하고(NetHostGameService.cs:120-142), 호스트가 보낼 때도 로컬 핸들러를
        // 부르지 않는다. 즉 자기 메시지는 절대 자기에게 돌아오지 않는다.
        // 여기서 직접 발생시켜야 UI가 루프백과 동일한 흐름을 보게 된다 — §5.2.
        Received?.Invoke(_netService.NetId, text);
    }

    /// <summary>
    /// 원격 메시지 수신. **자기 메시지는 여기로 오지 않는다** (위 <see cref="Send"/> 주석).
    ///
    /// 핸들러에서 던진 예외는 <c>NetMessageBus</c>가 삼키므로(NetMessageBus.cs:79-87)
    /// 여기서 직접 잡아 로그를 남긴다. 안 그러면 조용히 사라진다.
    /// </summary>
    private void HandleChatMessage(ChatNetMessage message, ulong senderId)
    {
        try
        {
            Received?.Invoke(senderId, message.text ?? string.Empty);
        }
        catch (Exception e)
        {
            Log.Error($"[{ModEntry.ModId}] chat receive handler threw: {e}");
        }
    }
}
