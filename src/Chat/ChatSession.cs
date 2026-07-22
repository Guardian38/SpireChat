using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using SpireChat.Chat.Net;

namespace SpireChat.Chat;

/// <summary>
/// 한 세션(= 한 등반)의 채팅 상태. **히스토리를 세션이 소유한다.**
///
/// **왜 소유권으로 묶었나:** 예전에는 히스토리가 프로세스 수명의 static 리스트라, 등반이
/// 끝나도 남아 **다음 등반까지 이전 대화가 따라왔다**(2026-07-20 인게임 확인). "세션 경계에서
/// 비운다"로 고칠 수도 있지만, 그러면 경계마다 비우는 것을 기억해야 한다.
/// 세션이 소유하면 세션이 사라질 때 함께 사라져 **누락이 구조적으로 불가능**해진다.
///
/// 발신자 표시 캐시들(닉네임·캐릭터·발신자 열 폭)도 수명이 같으므로 여기 둔다 —
/// 각각에 대해 "언제 비울지"를 따로 기억하지 않기 위해서다.
///
/// 세션의 수명은 <see cref="NetServiceTracker"/>가 정한다. 로비에서 만들어져 런까지
/// 그대로 이어지므로(PeerInputSynchronizer 인스턴스가 유지된다), 로비에서 나눈 대화는
/// 런으로 넘어가고 다음 등반에서만 사라진다.
/// </summary>
internal sealed class ChatSession : IDisposable
{
    /// <summary>보관할 최대 줄 수. 넘으면 오래된 것부터 버린다.</summary>
    private const int MaxHistory = 100;

    private readonly List<ChatLine> _history = new();
    private readonly NetMessageChatTransport _transport;
    private readonly Action<ChatLine> _onLineAdded;

    public ChatSession(INetGameService netService, Action<ChatLine> onLineAdded)
    {
        _onLineAdded = onLineAdded;

        Senders = new SenderRegistry(netService);

        _transport = new NetMessageChatTransport(netService);
        _transport.Received += OnReceived;
        _transport.Start();
    }

    public IReadOnlyList<ChatLine> History => _history;

    /// <summary>발신자 표시 캐시. 세션과 수명이 같다.</summary>
    public SenderRegistry Senders { get; }

    public void Send(string text)
    {
        _transport.Send(text.Trim());
    }

    public void Dispose()
    {
        _transport.Received -= OnReceived;
        _transport.Stop();
        Senders.Dispose();
        _history.Clear();
    }

    private void OnReceived(ulong senderId, string text)
    {
        // 수신 측에서도 자른다. 악의적 피어 방어라기보다 **견고성 장치**다 — 근거는 ChatLimits.
        Append(new ChatLine(senderId, DescribeSender(senderId), ChatLimits.ClampWire(text)));
    }

    /// <summary>
    /// peer id를 사람이 읽을 이름으로. 조회·캐싱은 <see cref="SenderRegistry"/>가 맡는다.
    ///
    /// **여기서 잡는 것은 닉네임뿐이고 캐릭터명은 넣지 않는다.** 캐릭터는 런 도중 바뀔 수
    /// 있어(일부 모드) 줄에 박아 두면 낡은 값이 굳는다. 완성된 표시 형식은 그릴 때마다
    /// 캐시에서 다시 조립한다 — 그래야 방 진입 갱신이 지난 줄에도 반영된다.
    ///
    /// **자기 자신도 남과 같은 형식으로 표시한다** — "나"로 줄이지 않는다.
    /// 채팅 화면은 스크린샷으로 공유되는 물건이라, 자기 줄만 "나"면 제3자가 볼 때 그게 누구인지
    /// 알 수 없어 인증 자료로 성립하지 않는다.
    /// </summary>
    private string DescribeSender(ulong senderId)
    {
        return Senders.Describe(senderId).Nickname;
    }

    private void Append(ChatLine line)
    {
        _history.Add(line);
        if (_history.Count > MaxHistory)
        {
            _history.RemoveRange(0, _history.Count - MaxHistory);
        }

        Log.Info($"[{ModEntry.ModId}] chat | {line.Sender}: {line.Text}");
        _onLineAdded(line);
    }
}
