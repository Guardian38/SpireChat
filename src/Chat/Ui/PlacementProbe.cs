using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace SpireChat.Chat.Ui;

/// <summary>
/// 진단 전용 — 배치 기준으로 삼는 게임 노드들의 실제 기하를 덤프한다. <c>chatstatus</c>가 쓴다.
///
/// **씬이 pck 안에 있어 노드의 실제 위치·pivot·컨테이너 종류를 디컴파일 소스로 알 수 없다.**
/// 런타임 값을 직접 찍는 것이 유일한 확인 수단이라, 배치가 어긋날 때마다 여기서 출발한다.
/// 계산(<see cref="ChatPlacement"/>)에서 떼어낸 이유는 진단이 계산보다 훨씬 넓은 범위를
/// 훑기 때문이다 — 실제로 쓰는 값뿐 아니라 "쓸 수도 있었던 값"까지 나란히 찍어야 갈린다.
/// </summary>
internal static class PlacementProbe
{
    /// <summary>현재 화면의 배치 기준 후보들을 한 덩어리로 덤프한다.</summary>
    public static string Describe()
    {
        var viewport = GetViewportSize();
        var sb = new StringBuilder();

        sb.Append($"viewport={viewport} context={ChatContextResolver.Resolve()}");
        AppendCombat(sb, viewport);
        AppendLobby(sb);

        return sb.ToString();
    }

    /// <summary>
    /// 전투 — 손패 기하. 창을 손패 위로 올리려면 **카드의 화면상 위쪽 끝**이 필요하다.
    ///
    /// 좌표계 원점은 <c>NPlayerHand</c>가 아니라 자식 <c>CardHolderContainer</c>다.
    /// 손패는 자기 Position을 애니메이션에 쓰므로 숨김 중에 읽으면 500px 아래가 나온다.
    /// </summary>
    private static void AppendCombat(StringBuilder sb, Vector2 viewport)
    {
        var hand = NPlayerHand.Instance;
        if (hand == null)
        {
            sb.Append("\n    hand=none (전투 아님)");
            return;
        }

        sb.Append($"\n    hand: pos={hand.Position} size={hand.Size} globalRect={hand.GetGlobalRect()}");

        var container = hand.CardHolderContainer;
        if (container == null || !GodotObject.IsInstanceValid(container))
        {
            sb.Append("\n    cardHolderContainer=none");
            return;
        }

        sb.Append($"\n    cardHolderContainer: globalPos={container.GlobalPosition} pos={container.Position} " +
                  $"size={container.Size} scale={container.GetGlobalTransform().Scale}");

        var holders = hand.ActiveHolders;
        if (holders.Count == 0)
        {
            sb.Append("\n    holders: n=0 (손패가 비어 기준을 홀더에서 얻을 수 없다)");
            return;
        }

        // 부채꼴이라 화면상 가장 높은 카드는 대개 가운데다. pivot 판정은 이 한 장이면 갈린다 —
        // Position(부모 기준)과 hitbox의 실제 화면 rect를 나란히 놓고 비교한다.
        int mid = holders.Count / 2;
        var holder = holders[mid];

        sb.Append($"\n    holder[{mid}]: pos={holder.Position} size={holder.Size} scale={holder.Scale} " +
                  $"pivot={holder.PivotOffset} globalRect={holder.GetGlobalRect()} visual={VisualRect(holder)}");

        var hitbox = holder.Hitbox;
        sb.Append(hitbox == null || !GodotObject.IsInstanceValid(hitbox)
            ? "\n    hitbox=none"
            : $"\n    hitbox: pos={hitbox.Position} size={hitbox.Size} " +
              $"globalRect={hitbox.GetGlobalRect()} visual={VisualRect(hitbox)}");

        // 실제로 필요한 값 셋. 특히 fromScreenBottom은 CombatBottomOffset(300)과 직접 비교된다.
        float top = CardTopY(holders);
        sb.Append($"\n    holders: n={holders.Count} cardTopY={top:F1} " +
                  $"fromContainer={top - container.GlobalPosition.Y:F1} " +
                  $"fromScreenBottom={viewport.Y - top:F1}");
    }

    /// <summary>
    /// 로비 — 플레이어 목록 기하. **바깥 Control과 자식 <c>Container</c>를 나란히 찍는다.**
    ///
    /// 지금 배치는 바깥 Rect의 오른쪽 끝을 쓰는데, 바깥이 레이아웃용으로 넓게 잡혀 있으면
    /// 목록의 시각적 오른쪽 끝보다 훨씬 오른쪽이 된다 — 창이 화면 중앙으로 밀린 유력한 원인이다.
    /// 자식 항목들의 합집합까지 찍어야 셋 중 어느 것이 시각적 경계인지 갈린다.
    /// </summary>
    private static void AppendLobby(StringBuilder sb)
    {
        var outer = ChatPlacement.FindLobbyPlayerContainer();
        if (outer == null)
        {
            sb.Append("\n    lobby=none (기준 노드 없음 → 폴백 좌표를 쓴다)");
            return;
        }

        sb.Append($"\n    lobbyOuter: pos={outer.Position} size={outer.Size} " +
                  $"globalRect={outer.GetGlobalRect()} visual={VisualRect(outer)}");

        // "Container"는 유니크 이름(%)이 아닌 평범한 자식 경로라 리플렉션 없이 잡힌다
        // (NRemoteLobbyPlayerContainer.cs:88). 형제인 %SoloLabel은 이 바깥에 있다.
        var inner = outer.GetNodeOrNull<Container>("Container");
        if (inner == null)
        {
            sb.Append("\n    lobbyInner=none (\"Container\" 자식을 못 찾음)");
            return;
        }

        sb.Append($"\n    lobbyInner: class={inner.GetClass()} children={inner.GetChildCount()} " +
                  $"pos={inner.Position} size={inner.Size} " +
                  $"globalRect={inner.GetGlobalRect()} visual={VisualRect(inner)}");

        Rect2? union = null;
        int counted = 0;
        foreach (var child in inner.GetChildren())
        {
            if (child is not Control control || !control.IsVisibleInTree())
            {
                continue;
            }

            var rect = VisualRect(control);
            union = union == null ? rect : union.Value.Merge(rect);
            counted++;
        }

        sb.Append(union == null
            ? "\n    lobbyItems: n=0"
            : $"\n    lobbyItems: n={counted} union={union.Value}");
    }

    /// <summary>화면상 가장 높은 카드의 위쪽 끝(global y). 손패에서 창까지의 간격 기준이다.</summary>
    private static float CardTopY(IReadOnlyList<NHandCardHolder> holders)
    {
        float top = float.MaxValue;

        foreach (var holder in holders)
        {
            var hitbox = holder.Hitbox;
            var target = hitbox != null && GodotObject.IsInstanceValid(hitbox) ? hitbox : (Control)holder;
            top = Mathf.Min(top, VisualRect(target).Position.Y);
        }

        return top;
    }

    /// <summary>
    /// 화면상 실제 영역.
    ///
    /// **<c>GetGlobalRect()</c>는 Scale을 반영하지 않는다** — Size가 로컬 값 그대로라
    /// 기본 0.8배로 줄어 있는 손패 카드에서는 실제보다 큰 rect가 나온다. 전역 변환에서
    /// 원점과 배율을 직접 꺼내 곱해야 눈에 보이는 영역과 일치한다.
    /// </summary>
    private static Rect2 VisualRect(Control control)
    {
        var transform = control.GetGlobalTransform();
        return new Rect2(transform.Origin, control.Size * transform.Scale);
    }

    private static Vector2 GetViewportSize()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            return Vector2.Zero;
        }

        return tree.Root.GetVisibleRect().Size;
    }
}
