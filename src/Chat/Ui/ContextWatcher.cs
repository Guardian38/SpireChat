using System;
using SpireChat.Game;

namespace SpireChat.Chat.Ui;

/// <summary>
/// **채팅창을 놓을 맥락이 실제로 바뀌었을 때만** 알린다.
///
/// 계기는 <see cref="RunTracker.RoomChanged"/> 하나다. 방이 바뀌어도 배치가 같은 경우
/// (맵→상점→휴식 등)가 대부분이라 그대로 흘리면 재배치가 헛돈다.
///
/// **로비↔세션 밖 전환은 보지 않는다** — 그때는 세션이 갈리면서 창이 통째로 닫히므로
/// 재배치할 창 자체가 없다.
///
/// 감시는 창이 보이는 동안만 돈다(<see cref="Start"/>/<see cref="Stop"/>).
/// </summary>
internal static class ContextWatcher
{
    private static Action? _onChanged;

    /// <summary>마지막으로 알린 맥락. 같은 값이면 계기로 삼지 않는다.</summary>
    private static ChatContext _last;

    public static bool IsWatching => _onChanged != null;

    /// <summary>
    /// 감시를 시작한다. 이미 돌고 있으면 기준만 다시 잡는다 — 창이 방금 그려졌다면
    /// 그 시점의 맥락이 새 기준이어야 한다.
    /// </summary>
    public static void Start(Action onContextChanged)
    {
        _last = ChatContextResolver.Resolve();

        if (_onChanged != null)
        {
            _onChanged = onContextChanged;
            return;
        }

        _onChanged = onContextChanged;
        RunTracker.RoomChanged += OnRoomChanged;
    }

    public static void Stop()
    {
        if (_onChanged == null)
        {
            return;
        }

        RunTracker.RoomChanged -= OnRoomChanged;
        _onChanged = null;
    }

    private static void OnRoomChanged()
    {
        var context = ChatContextResolver.Resolve();
        if (context == _last)
        {
            return;
        }

        _last = context;
        _onChanged?.Invoke();
    }
}
