using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;
using SpireChat.Chat.Net;

namespace SpireChat.Chat;

/// <summary>
/// 채팅의 전역 상태. **오버레이보다 오래 산다.**
///
/// **왜 UI와 수명을 분리했나:** 예전에는 <c>ChatOverlay.Open()</c>이 트랜스포트를 만들고
/// 닫을 때 버렸다. 루프백에서는 문제가 없었지만 실제 통신에서는 치명적이다 —
/// **채팅창을 열어둔 동안에만 메시지를 받게 되어** 남이 보낸 말이 대부분 사라진다.
/// 그래서 수신·보관은 여기가 맡고, 오버레이는 이미 쌓인 것을 보여주는 창 역할만 한다.
///
/// 세션 수명주기는 <see cref="NetServiceTracker"/>가 알려준다 — 로비가 열리면 트랜스포트를
/// 붙이고, 연결이 끊기면 뗀다.
/// </summary>
public static class ChatService
{
    /// <summary>보관할 최대 줄 수. 넘으면 오래된 것부터 버린다.</summary>
    private const int MaxHistory = 100;

    private static readonly List<ChatLine> _history = new();
    private static NetMessageChatTransport? _transport;
    private static bool _initialized;

    /// <summary>메시지 한 줄. 표시 형식은 UI가 정한다.</summary>
    public readonly struct ChatLine
    {
        public ChatLine(ulong senderId, string sender, string text)
        {
            SenderId = senderId;
            Sender = sender;
            Text = text;
        }

        public ulong SenderId { get; }

        /// <summary>표시용 발신자 이름. 이름을 못 구하면 peer id 문자열이다.</summary>
        public string Sender { get; }

        public string Text { get; }
    }

    /// <summary>새 줄이 쌓였을 때. 오버레이가 열려 있으면 갱신하라는 신호다.</summary>
    public static event Action<ChatLine>? LineAdded;

    /// <summary>지금까지 쌓인 메시지. 오버레이가 열릴 때 이걸로 목록을 채운다.</summary>
    public static IReadOnlyList<ChatLine> History => _history;

    /// <summary>보낼 수 있는 상태인지. 멀티 세션 밖이면 false다.</summary>
    public static bool CanSend => _transport != null;

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

        if (_transport == null)
        {
            AddSystemLine("멀티플레이 세션이 아닙니다 — 보낼 상대가 없습니다.");
            return;
        }

        _transport.Send(text.Trim());
    }

    /// <summary>시스템 안내를 목록에 끼워 넣는다. 네트워크로 나가지 않는다.</summary>
    public static void AddSystemLine(string text)
    {
        Append(new ChatLine(0uL, "시스템", text));
    }

    private static void OnNetServiceChanged(INetGameService? netService)
    {
        if (_transport != null)
        {
            _transport.Received -= OnReceived;
            _transport.Stop();
            _transport = null;
        }

        if (netService == null)
        {
            return;
        }

        _transport = new NetMessageChatTransport(netService);
        _transport.Received += OnReceived;
        _transport.Start();
    }

    private static void OnReceived(ulong senderId, string text)
    {
        Append(new ChatLine(senderId, DescribeSender(senderId), text));
    }

    /// <summary>
    /// peer id를 사람이 읽을 이름으로. 자기 자신은 "나"로 표시해 눈에 띄게 한다.
    ///
    /// vanilla도 로비 표시에 <c>GetPlayerNameRaw</c>를 쓴다(NMultiplayerTest.cs:469).
    /// 플랫폼이 이름을 못 주는 경우(ENet 테스트 등)가 있어 실패하면 id로 되돌린다.
    /// </summary>
    private static string DescribeSender(ulong senderId)
    {
        var netService = NetServiceTracker.Current;
        if (netService == null)
        {
            return senderId.ToString();
        }

        if (senderId == netService.NetId)
        {
            return "나";
        }

        try
        {
            string name = PlatformUtil.GetPlayerNameRaw(netService.Platform, senderId);
            return string.IsNullOrWhiteSpace(name) ? senderId.ToString() : name;
        }
        catch (Exception e)
        {
            Log.Warn($"[{ModEntry.ModId}] could not resolve name for peer {senderId}: {e.Message}");
            return senderId.ToString();
        }
    }

    private static void Append(ChatLine line)
    {
        _history.Add(line);
        if (_history.Count > MaxHistory)
        {
            _history.RemoveRange(0, _history.Count - MaxHistory);
        }

        Log.Info($"[{ModEntry.ModId}] chat | {line.Sender}: {line.Text}");

        try
        {
            LineAdded?.Invoke(line);
        }
        catch (Exception e)
        {
            Log.Error($"[{ModEntry.ModId}] ChatService.LineAdded subscriber threw: {e}");
        }
    }
}
