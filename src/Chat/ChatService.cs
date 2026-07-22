using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using SpireChat.Chat.Net;

namespace SpireChat.Chat;

/// <summary>
/// 채팅의 진입점. **상태는 갖지 않고 <see cref="ChatSession"/>에 위임한다.**
///
/// **왜 UI와 수명을 분리했나:** 예전에는 <c>ChatOverlay.Open()</c>이 트랜스포트를 만들고
/// 닫을 때 버렸다. 루프백에서는 문제가 없었지만 실제 통신에서는 치명적이다 —
/// **채팅창을 열어둔 동안에만 메시지를 받게 되어** 남이 보낸 말이 대부분 사라진다.
/// 그래서 수신·보관은 세션이 맡고, 오버레이는 이미 쌓인 것을 보여주는 창 역할만 한다.
///
/// 세션 수명주기는 <see cref="NetServiceTracker"/>가 알려준다 — 로비가 열리면 세션을
/// 만들고, 연결이 끊기면 버린다. 히스토리도 그때 함께 사라진다.
/// </summary>
public static class ChatService
{
    private static readonly IReadOnlyList<ChatLine> _empty = Array.Empty<ChatLine>();

    private static ChatSession? _session;
    private static bool _initialized;

    /// <summary>새 줄이 쌓였을 때. 오버레이가 열려 있으면 갱신하라는 신호다.</summary>
    public static event Action<ChatLine>? LineAdded;

    /// <summary>
    /// 세션이 바뀌었을 때(= 히스토리가 통째로 갈렸을 때). 오버레이가 남은 줄을 치우도록 알린다.
    /// </summary>
    public static event Action? SessionChanged;

    /// <summary>
    /// 보관할 것이 아닌 일회성 안내. 세션 밖에서는 히스토리 자체가 없으므로 이쪽으로 나간다.
    /// </summary>
    public static event Action<string>? NoticeRaised;

    /// <summary>
    /// 발신자 표시 재료가 갱신됐을 때(방 진입으로 캐릭터가 다시 잡혔을 때).
    /// 표시 문자열과 열 폭이 함께 바뀌므로 오버레이는 다시 그려야 한다.
    /// </summary>
    public static event Action? SendersChanged;

    /// <summary>지금까지 쌓인 메시지. 세션이 없으면 빈 목록이다.</summary>
    public static IReadOnlyList<ChatLine> History => _session?.History ?? _empty;

    /// <summary>
    /// 발신자 표시 재료. 세션 밖이면 닉네임 자리에 id만 담아 돌려준다 —
    /// 호출부가 null을 다루지 않게 하기 위함이다.
    /// </summary>
    public static SenderDisplay DescribeSender(ulong senderId)
    {
        return _session?.Senders.Describe(senderId)
               ?? new SenderDisplay(senderId.ToString(), null, null);
    }

    /// <summary>
    /// 열 폭 산출의 기준이 될 참가자 목록. 런 밖에서는 비어 있고,
    /// 그때는 호출부가 내역에 등장한 발신자로 대신한다.
    /// </summary>
    public static IReadOnlyCollection<ulong> KnownParticipants =>
        _session?.Senders.KnownParticipants ?? Array.Empty<ulong>();

    /// <summary>보낼 수 있는 상태인지. 멀티 세션 밖이면 false다.</summary>
    public static bool CanSend => _session != null;

    /// <summary>
    /// 채팅창을 **열 자격**이 있는 상태인지.
    ///
    /// <see cref="CanSend"/>와 다른 개념이다 — 그쪽은 "지금 보낼 수 있는가"이고
    /// 이쪽은 "창이 뜰 만한 상황인가"다.
    ///
    /// **"세션이 있는가"로 판별하면 부족하다.** <c>PeerInputSynchronizer</c>는 싱글플레이·
    /// 리플레이 경로도 지나므로(<see cref="NetServiceTracker"/>), 세션 유무만 보면
    /// **솔로 등반에서도 채팅창이 열린다.** 대화 상대가 없는데 입력창이 뜨면 보낼 수 있는
    /// 것처럼 오인되므로 <c>Type</c>까지 본다.
    /// </summary>
    public static bool CanChat
    {
        get
        {
            var netService = NetServiceTracker.Current;
            return netService != null
                   && netService.Type is NetGameType.Host or NetGameType.Client;
        }
    }

    /// <summary>모드 초기화 시 한 번 호출한다.</summary>
    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        NetServiceTracker.Changed += OnNetServiceChanged;

        // 모드가 세션 도중에 초기화될 일은 없지만, 이미 잡혀 있다면 놓치지 않는다.
        OnNetServiceChanged(NetServiceTracker.Current);
    }

    public static void Send(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (_session == null)
        {
            Notice("멀티플레이 세션이 아닙니다 — 보낼 상대가 없습니다.");
            return;
        }

        _session.Send(text);
    }

    /// <summary>
    /// 일회성 안내를 띄운다. **히스토리에 넣지 않는다** — 보존할 대화가 아니라 즉시
    /// 알려주려는 상태 안내이고, 세션 밖에서는 담을 히스토리도 없다.
    /// </summary>
    public static void Notice(string text)
    {
        Log.Info($"[{ModEntry.ModId}] chat notice | {text}");
        Raise(() => NoticeRaised?.Invoke(text), nameof(NoticeRaised));
    }

    private static void OnNetServiceChanged(INetGameService? netService)
    {
        if (_session != null)
        {
            _session.Senders.Changed -= OnSendersChanged;
            _session.Dispose();
        }

        _session = null;

        if (netService != null)
        {
            _session = new ChatSession(netService, OnLineAdded);
            _session.Senders.Changed += OnSendersChanged;
        }

        Raise(() => SessionChanged?.Invoke(), nameof(SessionChanged));
    }

    private static void OnSendersChanged()
    {
        Raise(() => SendersChanged?.Invoke(), nameof(SendersChanged));
    }

    private static void OnLineAdded(ChatLine line)
    {
        Raise(() => LineAdded?.Invoke(line), nameof(LineAdded));
    }

    /// <summary>구독자가 던진 예외가 채팅 파이프라인을 끊지 않도록 가둔다.</summary>
    private static void Raise(Action invoke, string eventName)
    {
        try
        {
            invoke();
        }
        catch (Exception e)
        {
            Log.Error($"[{ModEntry.ModId}] ChatService.{eventName} subscriber threw: {e}");
        }
    }
}
