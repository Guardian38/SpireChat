using Godot;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;

namespace SpireChat.Chat.Ui;

/// <summary>채팅창이 자라는 방향. 배치가 함께 결정한다.</summary>
public enum GrowDirection
{
    /// <summary>아래에서 위로. 런 — 아래로 자라면 손패를 덮는다.</summary>
    Up,

    /// <summary>위에서 아래로. 로비 — 상단에서 시작하므로 위로는 여백이 없다.</summary>
    Down
}

/// <summary>배치 결과. 기준점과 성장 방향, 그리고 창의 고정 폭.</summary>
public readonly struct ChatPlacementResult
{
    public ChatPlacementResult(Vector2 anchor, GrowDirection grow, float width)
    {
        Anchor = anchor;
        Grow = grow;
        Width = width;
    }

    /// <summary>창의 기준 모서리(화면 좌표). Up이면 하단 좌측, Down이면 상단 좌측.</summary>
    public Vector2 Anchor { get; }

    public GrowDirection Grow { get; }

    public float Width { get; }
}

/// <summary>
/// 컨텍스트별 배치 계산. **상태 기계에서 떼어낸 이유**는 배치가 실사용 피드백으로 바뀔
/// 여지가 크기 때문이다. 위치 계산은 전부 여기 모인다.
///
/// 기준점을 잡는 방식이 **경로마다 다르다** — 씬 파일(pck 내부)의 노드 위치를 디컴파일
/// 소스로 알 수 없다는 점은 같지만, 노드 Rect가 쓸 만한지가 갈린다.
///
/// <list type="bullet">
/// <item>로비 — 플레이어 목록 노드를 <b>런타임에 찾아 Rect를 읽는다.</b></item>
/// <item>전투 — 손패 Rect는 <b>쓰지 않는다.</b> 화면 하단 기준 상수를 쓴다
///       (<see cref="CombatBottomOffset"/> 주석).</item>
/// </list>
/// </summary>
public static class ChatPlacement
{
    /// <summary>창 고정 폭. 손패 폭은 장수에 따라 변하므로 따라가면 흔들린다.</summary>
    public const float Width = 560f;

    /// <summary>
    /// 전투 중 화면 하단에서 띄울 거리 — 손패를 비켜 가는 높이.
    ///
    /// **손패 노드의 Rect를 기준으로 삼지 않는다.** <c>NPlayerHand</c>는 Control이지만 자기
    /// Position을 애니메이션에 쓰고(_showPosition = Zero, 숨김 (0,500)) 카드 좌표는
    /// HandPosHelper가 원점 주변으로 흩뿌리는 상대 오프셋이라, Rect가 화면상 손패 영역과
    /// 일치하지 않는다. 실제로 Rect 기준으로 잡았더니 y가 음수가 되어 창이 화면 밖으로 나갔다.
    ///
    /// 그래서 **화면 하단 기준의 보정값**으로 둔다. 손패 위치는 해상도에 따라 비례하므로
    /// 이 방식이 오히려 안정적이다. 값은 인게임에서 눈으로 맞춘다.
    /// </summary>
    private const float CombatBottomOffset = 300f;

    /// <summary>전투 밖 폴백에서 화면 하단으로부터의 오프셋.</summary>
    private const float BottomFallbackOffset = 140f;

    /// <summary>로비에서 플레이어 목록 오른쪽으로 띄울 여백.</summary>
    private const float LobbyGap = 32f;

    /// <summary>세션·화면을 못 찾았을 때 화면 가장자리에서 띄울 여백.</summary>
    private const float ScreenMargin = 48f;

    /// <summary>지금 맥락에 맞는 배치를 구한다.</summary>
    public static ChatPlacementResult Resolve(Control reference)
    {
        var viewport = reference.GetViewportRect().Size;

        switch (ChatContextResolver.Resolve())
        {
            case ChatContext.Combat:
                return ResolveCombat(viewport);

            case ChatContext.Lobby:
                return ResolveLobby(viewport);

            default:
                return BottomFallback(viewport);
        }
    }

    /// <summary>
    /// 전투 — 손패 위, 가로 중앙. 위로 자란다(아래로 자라면 손패를 덮는다).
    ///
    /// 손패 노드는 **존재 여부로 전투를 판별하는 데만** 쓰고, 세로 위치는
    /// <see cref="CombatBottomOffset"/>으로 잡는다 — 이유는 그 상수 주석 참조.
    /// </summary>
    private static ChatPlacementResult ResolveCombat(Vector2 viewport)
    {
        float x = (viewport.X - Width) * 0.5f;
        float y = viewport.Y - CombatBottomOffset;

        return new ChatPlacementResult(new Vector2(x, y), GrowDirection.Up, Width);
    }

    /// <summary>
    /// 로비 — 플레이어 목록 오른쪽. 목록과 채팅이 한 시야에 들어와야 이름↔플레이어가 이어진다.
    ///
    /// <c>NCharacterSelectScreen</c>에는 static Instance가 없고 컨테이너 필드도 private이라
    /// **씬 트리에서 타입으로 찾는다.** 커스텀런·데일리런 화면은 목록 노드가 미조사라,
    /// 못 찾으면 화면 기준 폴백으로 떨어진다.
    /// </summary>
    private static ChatPlacementResult ResolveLobby(Vector2 viewport)
    {
        var container = FindLobbyPlayerContainer();
        if (container == null)
        {
            return new ChatPlacementResult(
                new Vector2(ScreenMargin, ScreenMargin), GrowDirection.Down, Width);
        }

        var rect = container.GetGlobalRect();
        return new ChatPlacementResult(
            new Vector2(rect.End.X + LobbyGap, rect.Position.Y), GrowDirection.Down, Width);
    }

    /// <summary>전투 밖(맵·상점·이벤트)과 세션 밖의 공통 폴백 — 화면 하단 중앙.</summary>
    private static ChatPlacementResult BottomFallback(Vector2 viewport)
    {
        float x = (viewport.X - Width) * 0.5f;
        float y = viewport.Y - BottomFallbackOffset;

        return new ChatPlacementResult(new Vector2(x, y), GrowDirection.Up, Width);
    }

    /// <summary>
    /// 계산된 위치를 화면 안으로 밀어 넣는다.
    ///
    /// **없으면 배치 오류가 "안 보이는 창"으로 나타난다.** 실제로 손패 기준점을 잘못 잡아
    /// y가 음수가 되면서 창이 화면 위로 사라진 적이 있다 — 크기 0과 구분되지 않아 진단이
    /// 오래 걸렸다. 어긋나더라도 보이는 편이 낫다.
    /// </summary>
    public static Vector2 ClampToScreen(Vector2 position, Vector2 size, Vector2 viewport)
    {
        float x = Mathf.Clamp(position.X, 0f, Mathf.Max(0f, viewport.X - size.X));
        float y = Mathf.Clamp(position.Y, 0f, Mathf.Max(0f, viewport.Y - size.Y));
        return new Vector2(x, y);
    }

    /// <summary>배치 기준으로 쓰는 로비 노드. 진단 덤프(<see cref="PlacementProbe"/>)도 같은 것을 본다.</summary>
    internal static NRemoteLobbyPlayerContainer? FindLobbyPlayerContainer()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            return null;
        }

        return FindDescendant<NRemoteLobbyPlayerContainer>(tree.Root);
    }

    /// <summary>씬 트리를 훑어 타입이 맞는 첫 노드를 찾는다. 못 찾으면 null이다.</summary>
    private static T? FindDescendant<T>(Node node) where T : class
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match)
            {
                return match;
            }

            var found = FindDescendant<T>(child);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
