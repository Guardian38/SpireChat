using System;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpireChat.Game;

/// <summary>
/// 게임 런 이벤트를 구독하는 **단일 창구.** 런 상태·현재 방을 묻는 경로는 전부 여기를 지난다.
///
/// **왜 모으는가.** 게임 API 접점이 흩어지면 업데이트 때 고칠 자리가 그만큼 늘고,
/// 한쪽만 폴백을 타면 구독자마다 서로 다른 런 상태를 보게 된다.
///
/// 모드 수명 내내 살아 있으므로 구독을 해제하지 않는다 — <c>RunManager.Instance</c>는
/// 교체되지 않는 정적 싱글턴이다(RunManager.cs:78).
/// </summary>
internal static class RunTracker
{
    /// <summary><c>RunManager.State</c>가 private이라 <c>RunStarted</c>로 받아 둔다.</summary>
    private static RunState? _state;

    private static bool _subscribed;

    /// <summary>
    /// 현재 방이 달라졌을 수 있다. **런 시작·방 진입·방 이탈 셋 다** 여기로 모인다 —
    /// 구독자에게 필요한 것은 어느 이벤트였는지가 아니라 "다시 봐야 한다"는 사실이다.
    ///
    /// 발화 시점의 <see cref="CurrentRoom"/>은 **이미 새 방**이다. 게임이 상태를 갱신한 뒤
    /// 이벤트를 낸다(PushRoom:1190 → RoomEntered:1210, PopCurrentRoom:1151 → RoomExited:1153).
    /// </summary>
    public static event Action? RoomChanged;

    /// <summary>모드 초기화 시 한 번. 이벤트를 놓치지 않도록 가장 먼저 건다.</summary>
    public static void Initialize()
    {
        Subscribe();
    }

    /// <summary>
    /// 현재 런 상태. 런 밖이면 null이다.
    ///
    /// 주 경로는 <c>RunStarted</c>로 받아 둔 참조이고 <c>DebugOnlyGetState</c>는
    /// **이벤트를 놓쳤을 때만** 쓰는 폴백이다 — 게임이 "테스트 전용"이라 표시해 둔 메서드라
    /// 의존을 최소화한다. 폴백이 없으면 런 도중에 끼어들었을 때 그 런 내내 상태를 못 본다.
    ///
    /// <c>IsInProgress</c>는 <c>State != null</c>과 같다(RunManager.cs:104).
    /// <c>RunManager.Instance</c> 자체는 <c>= new RunManager()</c>라 런 밖에서도 non-null이므로
    /// **인스턴스 유무로 런을 판별하면 안 된다.**
    /// </summary>
    public static RunState? State
    {
        get
        {
            try
            {
                var runManager = RunManager.Instance;

                // 런 종료를 알려 주는 이벤트가 없다. 낡은 참조를 들고 있지 않도록 여기서 버린다.
                if (!runManager.IsInProgress)
                {
                    _state = null;
                    return null;
                }

                return _state ??= runManager.DebugOnlyGetState();
            }
            catch (Exception e)
            {
                Log.Warn($"[{ModEntry.ModId}] RunTracker could not read run state: {e.Message}");
                return null;
            }
        }
    }

    public static bool IsInRun => State != null;

    /// <summary>지금 들어와 있는 방. 런 밖이면 null이다.</summary>
    public static AbstractRoom? CurrentRoom => State?.CurrentRoom;

    /// <summary>
    /// 지금이 전투방인가. **노드가 아니라 방 종류로 판별한다** —
    /// 방 진입 이벤트 시점에는 전투 노드가 아직 없어 노드 기준으로는 전투 밖으로 읽힌다.
    /// </summary>
    public static bool IsInCombatRoom =>
        CurrentRoom?.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss;

    /// <summary>
    /// 셋 다 공식 공개 이벤트라 Harmony 패치가 필요 없다(RunManager.cs:266-270).
    /// 방 진입은 <c>EnterRoom</c>·<c>EnterRoomWithoutExitingCurrentRoom</c> 두 경로의 공통
    /// 내부에서 발화하므로 이벤트 하나가 이벤트 중 전투까지 포함해 전부 덮는다.
    /// </summary>
    private static void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        try
        {
            var runManager = RunManager.Instance;
            runManager.RunStarted += OnRunStarted;
            runManager.RoomEntered += OnRoomChanged;
            runManager.RoomExited += OnRoomChanged;
            _subscribed = true;
        }
        catch (Exception e)
        {
            Log.Error($"[{ModEntry.ModId}] RunTracker could not subscribe to run events: {e}");
        }
    }

    private static void OnRunStarted(RunState state)
    {
        _state = state;
        OnRoomChanged();
    }

    /// <summary>구독자가 던진 예외가 게임의 이벤트 디스패치를 끊지 않도록 가둔다.</summary>
    private static void OnRoomChanged()
    {
        try
        {
            RoomChanged?.Invoke();
        }
        catch (Exception e)
        {
            Log.Error($"[{ModEntry.ModId}] RunTracker.RoomChanged subscriber threw: {e}");
        }
    }
}
