using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace SpireChat.Chat;

/// <summary>
/// 발신자 표시에 필요한 값(닉네임·캐릭터·색)의 캐시.
///
/// **두 값의 성질이 달라 캐싱 시점도 다르다:**
/// <list type="bullet">
/// <item>닉네임 — 한 번 잡으면 끝. 세션 내내 바뀌지 않는다.</item>
/// <item>캐릭터 — <b>방 진입마다 다시 잡는다.</b> vanilla는 런 중 변경을 금지하지만
///       (<c>Player.cs:411</c>) 일부 모드가 그 경로를 우회해 바꾼다.</item>
/// </list>
///
/// 방 안에서 바뀌는 경우까지는 못 잡지만 다음 방에서 복구되므로 어긋남이 누적되지 않는다.
///
/// <see cref="ChatSession"/>이 소유한다 — 수명이 같아야 등반이 끝날 때 함께 사라진다.
/// </summary>
internal sealed class SenderRegistry : IDisposable
{
    private readonly INetGameService _netService;

    /// <summary>senderId → 닉네임. 한 번 채우면 지우지 않는다.</summary>
    private readonly Dictionary<ulong, string> _nicknames = new();

    /// <summary>senderId → 캐릭터. 방 진입마다 통째로 다시 만든다.</summary>
    private Dictionary<ulong, CharacterTag> _characters = new();

    /// <summary>
    /// 런 상태. <c>RunManager.State</c>가 private이라 이벤트로 받아 둔다.
    /// </summary>
    private RunState? _runState;

    private bool _subscribed;

    public SenderRegistry(INetGameService netService)
    {
        _netService = netService;
        Subscribe();
    }

    /// <summary>
    /// 캐시가 갱신됐다. 표시 문자열과 열 폭이 함께 바뀌므로 UI는 다시 그려야 한다
    /// (재계산은 캐시 갱신과 같은 시점).
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// 지금 아는 참가자들의 senderId. 열 폭 산출의 기준이다.
    ///
    /// **런에서는 실제 참가자 명단**이라 발언하지 않은 사람도 포함된다 — 이것이
    /// "내역에 등장한 발신자"로 대신하던 임시 방식과의 차이다. 참가자는 런 중 늘지 않으므로
    /// 폭이 출렁이지 않는다. 로비에서는 명단 경로가 없어 빈 목록이고, 그때는 호출부가
    /// 내역으로 대신한다.
    /// </summary>
    public IReadOnlyCollection<ulong> KnownParticipants => _characters.Keys;

    /// <summary>
    /// 표시용 발신자 정보. 캐릭터를 모르면(로비 등) 캐릭터명·색이 없는 형태로 돌아온다.
    /// </summary>
    public SenderDisplay Describe(ulong senderId)
    {
        string nickname = ResolveNickname(senderId);

        return _characters.TryGetValue(senderId, out var tag)
            ? new SenderDisplay(nickname, tag.Title, tag.Color)
            : new SenderDisplay(nickname, null, null);
    }

    public void Dispose()
    {
        Unsubscribe();
        _nicknames.Clear();
        _characters.Clear();
        _runState = null;
    }

    /// <summary>
    /// 닉네임은 senderId당 한 번만 조회한다.
    ///
    /// **부수 이점: 런 중 플랫폼 조회 실패에 영향받지 않는다.** 조회 자체는
    /// 플랫폼이 이미 폴백을 갖고 있어 빈 값을 주지 않는다 — Steam·ENet 모두 실패 시
    /// id 문자열을 돌려준다. 그래서 여기서는 예외만 막는다.
    /// </summary>
    private string ResolveNickname(ulong senderId)
    {
        if (_nicknames.TryGetValue(senderId, out var cached))
        {
            return cached;
        }

        string name;
        try
        {
            name = PlatformUtil.GetPlayerNameRaw(_netService.Platform, senderId);
        }
        catch (Exception e)
        {
            Log.Warn($"[{ModEntry.ModId}] could not resolve name for peer {senderId}: {e.Message}");
            name = senderId.ToString();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = senderId.ToString();
        }

        _nicknames[senderId] = name;
        return name;
    }

    /// <summary>
    /// 런 이벤트를 구독한다.
    ///
    /// **`RoomEntered`가 공식 공개 이벤트라 Harmony 패치가 필요 없다**(`RunManager.cs:268`).
    /// 설계 단계에서는 <c>EnterRoom</c>·<c>EnterRoomWithoutExitingCurrentRoom</c> 둘을 각각
    /// 패치할 생각이었으나, 실제 발화 지점이 두 경로의 공통 내부(<c>EnterRoomInternal:1210</c>)라
    /// 이벤트 하나가 맵 복귀까지 포함해 전부 덮는다.
    ///
    /// <c>RunManager.Instance</c>는 <c>= new RunManager()</c>라 항상 non-null이다.
    /// </summary>
    private void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        try
        {
            var runManager = RunManager.Instance;
            runManager.RunStarted += OnRunStarted;
            runManager.RoomEntered += OnRoomEntered;
            _subscribed = true;

            // 세션이 런 도중에 만들어질 수도 있으므로 현재 상태를 한 번 읽는다.
            RefreshCharacters();
        }
        catch (Exception e)
        {
            Log.Error($"[{ModEntry.ModId}] SenderRegistry could not subscribe to run events: {e}");
        }
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        _subscribed = false;

        try
        {
            var runManager = RunManager.Instance;
            runManager.RunStarted -= OnRunStarted;
            runManager.RoomEntered -= OnRoomEntered;
        }
        catch (Exception e)
        {
            Log.Warn($"[{ModEntry.ModId}] SenderRegistry could not unsubscribe: {e.Message}");
        }
    }

    private void OnRunStarted(RunState state)
    {
        _runState = state;
        RefreshCharacters();
    }

    private void OnRoomEntered()
    {
        RefreshCharacters();
    }

    /// <summary>
    /// 참가자 전원의 캐릭터를 다시 잡는다.
    ///
    /// **통째로 새 사전을 만든다** — 개별 갱신으로 하면 나간 플레이어가 남는다.
    /// 실패하면 이전 캐시를 그대로 두는 편이 낫다: 캐릭터명이 잠깐 낡는 것보다
    /// 표시가 통째로 사라지는 쪽이 눈에 띄는 손해다.
    /// </summary>
    private void RefreshCharacters()
    {
        var state = ResolveRunState();
        if (state == null)
        {
            // 런 밖(로비·메인 메뉴)이다. 이전 런의 캐릭터가 남지 않게 비운다.
            if (_characters.Count > 0)
            {
                _characters = new Dictionary<ulong, CharacterTag>();
                Changed?.Invoke();
            }

            return;
        }

        var next = new Dictionary<ulong, CharacterTag>();

        try
        {
            foreach (var player in state.Players)
            {
                var character = player.Character;
                if (character == null)
                {
                    continue;
                }

                next[player.NetId] = new CharacterTag(
                    character.Title.GetRawText(),
                    ResolveColor(character));
            }
        }
        catch (Exception e)
        {
            Log.Error($"[{ModEntry.ModId}] SenderRegistry could not read run players: {e}");
            return;
        }

        if (!HasChanged(next))
        {
            return;
        }

        _characters = next;
        Changed?.Invoke();
    }

    /// <summary>
    /// <c>RunManager.State</c>가 private이라 두 경로로 얻는다. 둘 다 공개 API다.
    ///
    /// 주 경로는 <c>RunStarted</c> 이벤트로 받아 둔 참조다. <c>DebugOnlyGetState</c>는
    /// **이벤트를 놓쳤을 때만** 쓰는 폴백이다 — 게임이 "테스트 전용"이라 표시해 둔 메서드라
    /// 의존을 최소화한다. 이 폴백이 없으면 이벤트를 한 번 놓쳤을 때 그 런 내내 캐릭터
    /// 표시가 조용히 사라진다.
    /// </summary>
    private RunState? ResolveRunState()
    {
        if (_runState != null)
        {
            return _runState;
        }

        try
        {
            return RunManager.Instance.DebugOnlyGetState();
        }
        catch (Exception e)
        {
            Log.Warn($"[{ModEntry.ModId}] SenderRegistry could not read run state: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 캐릭터 대표색.
    ///
    /// <c>RemoteTargetingLineColor</c>는 <c>virtual</c>이고 **기본값이 검정**이라
    /// 오버라이드하지 않은 캐릭터(<c>Deprived</c>·<c>DeprecatedCharacter</c>)는 검정이 나온다.
    /// 검정이거나 투명하면 <c>NameColor</c>로 폴백한다 — 이쪽은 <c>abstract</c>라 모든
    /// 캐릭터가 반드시 구현한다.
    /// </summary>
    private static Color ResolveColor(CharacterModel character)
    {
        var color = character.RemoteTargetingLineColor;
        return IsUnusable(color) ? character.NameColor : color;
    }

    /// <summary>어두운 반투명 배경 위에서 읽히지 않는 색인지.</summary>
    private static bool IsUnusable(Color color)
    {
        const float epsilon = 0.01f;
        bool isTransparent = color.A <= epsilon;
        bool isBlack = color.R <= epsilon && color.G <= epsilon && color.B <= epsilon;
        return isTransparent || isBlack;
    }

    /// <summary>
    /// 실제로 달라졌을 때만 갱신 신호를 낸다. 방 진입마다 부르므로, 그대로인데도 알리면
    /// 화면이 매번 다시 그려진다.
    /// </summary>
    private bool HasChanged(Dictionary<ulong, CharacterTag> next)
    {
        if (next.Count != _characters.Count)
        {
            return true;
        }

        foreach (var pair in next)
        {
            if (!_characters.TryGetValue(pair.Key, out var old) || !old.Equals(pair.Value))
            {
                return true;
            }
        }

        return false;
    }

    private readonly struct CharacterTag : IEquatable<CharacterTag>
    {
        public CharacterTag(string title, Color color)
        {
            Title = title;
            Color = color;
        }

        public string Title { get; }

        public Color Color { get; }

        public bool Equals(CharacterTag other)
        {
            return Title == other.Title && Color == other.Color;
        }
    }
}

/// <summary>
/// 한 발신자의 표시 재료. **형식은 UI가 정한다** — 여기는 조각만 담는다.
/// </summary>
public readonly struct SenderDisplay
{
    public SenderDisplay(string nickname, string? characterTitle, Color? color)
    {
        Nickname = nickname;
        CharacterTitle = characterTitle;
        Color = color;
    }

    public string Nickname { get; }

    /// <summary>캐릭터명. 로비처럼 캐릭터가 확정 전이면 null이다.</summary>
    public string? CharacterTitle { get; }

    /// <summary>캐릭터 대표색. 캐릭터를 모르면 null이고, 그때는 채색하지 않는다.</summary>
    public Color? Color { get; }
}
