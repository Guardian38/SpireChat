using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using SpireChat.Chat.Ui;
using SpireChat.Ui;

namespace SpireChat.Chat;

/// <summary>표시 상태. 로비와 런이 **같은 상태 기계**를 쓰고 배치만 다르다.</summary>
public enum ChatViewState
{
    /// <summary>없음. 조용할 때의 기본.</summary>
    Hidden,

    /// <summary>최근 몇 줄만. 입력창 없음. 마지막 메시지로부터 5초 뒤 fade-out.</summary>
    Peek,

    /// <summary>전체 내역 + 입력창. T를 누른 동안.</summary>
    Expanded
}

/// <summary>
/// 채팅 UI. 메시지 목록·입력창의 구성과 표시 전환을 맡는다.
///
/// **전송 방식도, 세션 상태도 모른다** — <see cref="ChatService"/>에 보내달라고 하고,
/// 쌓인 것을 읽어 그릴 뿐이다. 수신·보관은 창이 닫혀 있는 동안에도 계속돼야 하므로
/// 오버레이가 아니라 서비스가 맡는다.
///
/// 커스텀 Node 서브클래스를 만들지 않고 순수 Godot 노드 조합 + C# 시그널로만 구성한다 —
/// 모드 어셈블리의 스크립트 클래스를 씬 트리에 등록하는 문제를 회피한다.
/// 같은 제약 때문에 <c>_Process</c>를 쓸 수 없어 타이머·fade는 SceneTreeTimer와 Tween으로 한다.
/// </summary>
public static class ChatOverlay
{
    /// <summary>Peek에서 보여줄 줄 수. 주변시로 흘려보는 것이라 적게 둔다.</summary>
    private const int PeekLines = 5;

    /// <summary>Expanded에서 보여줄 줄 수.</summary>
    private const int ExpandedLines = 12;

    /// <summary>마지막 메시지로부터 Peek이 유지되는 시간(초).</summary>
    private const float PeekSeconds = 5f;

    /// <summary>fade-out에 걸리는 시간(초).</summary>
    private const float FadeSeconds = 0.6f;

    private const int FontSize = 20;

    private static CanvasLayer? _root;
    private static Control? _frame;
    private static VBoxContainer? _rows;
    private static LineEdit? _input;
    private static Tween? _fade;

    /// <summary>보관하지 않는 일회성 안내. 다음 갱신까지만 화면에 남는다.</summary>
    private static string? _notice;

    /// <summary>
    /// Peek 타이머 세대. 메시지가 오면 증가시켜 **이전 타이머의 만료를 무효화**한다.
    /// 타이머를 취소할 수단이 없어(SceneTreeTimer) 세대 비교로 대신한다.
    /// </summary>
    private static int _peekGeneration;

    /// <summary>
    /// 지금 우리가 스스로 입력창을 내리는 중인가. <see cref="OnInputFocusExited"/>의 재귀를 막는다.
    ///
    /// 닫기 경로는 예외 없이 <c>ReleaseFocus()</c>를 부르는데(포커스를 쥔 채 두면 게임 단축키가
    /// 먹통이 되므로), 그 자체가 다시 <c>FocusExited</c>를 발화해 **닫기가 스스로를 재호출한다.**
    /// </summary>
    private static bool _hidingInput;

    public static ChatViewState State { get; private set; } = ChatViewState.Hidden;

    public static bool IsOpen => State == ChatViewState.Expanded;

    /// <summary>모드 초기화 시 한 번. 메시지가 오면 창을 열지 않아도 Peek이 떠야 한다.</summary>
    public static void Initialize()
    {
        ChatService.LineAdded -= OnLineAdded;
        ChatService.LineAdded += OnLineAdded;
        ChatService.SessionChanged -= OnSessionChanged;
        ChatService.SessionChanged += OnSessionChanged;
        ChatService.NoticeRaised -= OnNoticeRaised;
        ChatService.NoticeRaised += OnNoticeRaised;
        ChatService.SendersChanged -= OnSendersChanged;
        ChatService.SendersChanged += OnSendersChanged;
    }

    /// <summary>
    /// 방 진입으로 캐릭터 캐시가 갱신됐다. **이미 그려진 줄의 표시도 함께 바뀌므로**
    /// 다시 그린다(재계산은 캐시 갱신과 같은 시점).
    ///
    /// **상태를 올리지 않는다.** 조용할 때 방을 옮겼다고 채팅창이 떠오르면 방해가 된다 —
    /// 새 메시지가 아니라 표시 재료만 바뀐 것이므로 보이는 중일 때만 갱신한다.
    /// </summary>
    private static void OnSendersChanged()
    {
        if (State == ChatViewState.Hidden)
        {
            return;
        }

        if (_rows == null || !GodotObject.IsInstanceValid(_rows))
        {
            return;
        }

        Refresh();
    }

    /// <summary>
    /// 세션이 갈리면 화면에 남은 줄도 치운다 — 이전 등반의 대화가 잔상으로 남지 않게.
    /// 히스토리 자체는 세션이 소유하므로 여기서 지울 것은 화면뿐이다.
    /// </summary>
    private static void OnSessionChanged()
    {
        _notice = null;

        // **세션이 갈리면 창을 완전히 닫는다.** 히스토리가 세션 소유라 내용이 사라지는데
        // 입력창만 남으면 "칠 수 있는 것처럼" 보여 오해를 부른다. 새 세션이 열릴 때도
        // 같은 처리를 해 이전 등반의 잔상 없이 깨끗하게 시작한다.
        //
        // 로비→런 전환에서는 이 이벤트가 발생하지 않는다 — 같은 PeerInputSynchronizer가
        // 그대로 이어지므로(NetServiceTracker) 대화 중에 창이 닫히는 일은 없다.
        CloseCompletely();
    }

    /// <summary>
    /// 창을 닫고 입력 상태까지 되돌린다.
    ///
    /// **포커스 해제가 핵심이다.** 숨기기만 하면 <c>LineEdit</c>이 편집 상태로 남을 수 있고,
    /// <c>NHotkeyManager</c>는 편집 중이면 단축키를 전부 흘려보내므로(NHotkeyManager.cs:177)
    /// **T를 포함한 게임 조작이 먹통이 된다.**
    /// </summary>
    private static void CloseCompletely()
    {
        HideInput(clearText: true);
        Hide();
    }

    /// <summary>
    /// 입력창을 내린다. **닫는 경로는 전부 이곳을 지난다** — 포커스 해제가 한 곳에서라도 빠지면
    /// 게임 단축키가 먹통이 되므로(위 <see cref="CloseCompletely"/> 주석) 경로마다 따로 쓰면
    /// 갈라질 위험이 크다.
    ///
    /// 여기서 나는 <c>FocusExited</c>는 **우리가 유발한 것**이라 닫기를 다시 부르면 안 된다
    /// (<see cref="_hidingInput"/>). 숨기기(<c>Visible = false</c>)도 포커스를 놓게 하므로
    /// 두 줄 모두 가드 안에 둔다.
    /// </summary>
    /// <param name="clearText">
    /// 쓰던 내용까지 버릴지. **세션이 갈릴 때만 true다** — 그때는 보낼 곳이 사라지므로 초안을
    /// 남겨 둘 이유가 없다. 평소 닫기(ESC·T·포커스 해제)에서는 남겨 다시 열면 이어 쓸 수 있다.
    /// </param>
    private static void HideInput(bool clearText)
    {
        // 대상이 이미 죽었을 수도 있으므로 **null 검사보다 먼저** 감시를 내린다.
        OutsideClickWatcher.Stop();

        if (_input == null || !GodotObject.IsInstanceValid(_input))
        {
            return;
        }

        _hidingInput = true;
        try
        {
            if (clearText)
            {
                _input.Clear();
            }

            _input.Visible = false;
            _input.ReleaseFocus();
        }
        finally
        {
            _hidingInput = false;
        }
    }

    /// <summary>일회성 안내. 히스토리에 없으므로 화면에만 덧붙여 그린다.</summary>
    private static void OnNoticeRaised(string text)
    {
        _notice = text;

        if (!EnsureNodes())
        {
            return;
        }

        if (State == ChatViewState.Hidden)
        {
            State = ChatViewState.Peek;
        }

        CancelFade();
        _frame!.Modulate = new Color(1f, 1f, 1f, 1f);
        Refresh();

        if (State != ChatViewState.Expanded)
        {
            StartPeekTimer();
        }
    }

    /// <summary>T 단축키·콘솔 명령의 진입점. 반환값은 콘솔에 출력할 상태 설명.</summary>
    public static string Toggle()
    {
        if (State == ChatViewState.Expanded)
        {
            Collapse();
            return "chat: collapsed";
        }

        Expand();
        if (State != ChatViewState.Expanded)
        {
            return "chat: FAILED to open (scene tree unavailable) — check the log.";
        }

        return ChatService.CanSend
            ? "chat: OPEN — type and press Enter. ESC closes."
            : "chat: OPEN (no multiplayer session — messages cannot be sent yet).";
    }

    private static void Expand()
    {
        if (!EnsureNodes())
        {
            return;
        }

        EnterExpanded();
    }

    /// <summary>
    /// Expanded 상태로 들어간다. **`T`로 열 때와 Enter로 보낸 뒤가 같은 경로를 지나게 하는 것이
    /// 이 메서드의 존재 이유다.** 전송 후에 상태 일부만 복구하면 "열었을 때"와 미묘하게
    /// 달라지므로 진입 경로를 하나로 모은다.
    ///
    /// **주의 — 이 통일은 ESC 누출과 무관하다.** 2026-07-20에는 ESC 누출의 원인을
    /// "전송 후 상태 복구가 어긋나서"로 보고 이 메서드를 만들었으나, **열자마자 ESC를 눌러도
    /// 재현되어 그 가설은 반증됐다.** 실제 원인은 이벤트를 소비하지 않은 것이고 수정은
    /// <see cref="OnInputGuiEvent"/>에 있다. 경로 통일 자체는 그것대로 유효하므로 남긴다.
    /// </summary>
    private static void EnterExpanded()
    {
        State = ChatViewState.Expanded;

        // Expanded에서는 타이머가 멈춘다 — 읽고 쓰는 중에 사라지면 안 된다.
        _peekGeneration++;
        CancelFade();

        _input!.Visible = true;
        Refresh();

        // 게임 UI가 아직 포커스를 쥐고 있을 수 있으므로 프레임 종료 후에 잡는다.
        // GrabFocus만으로는 IsEditing()이 false로 남아 NHotkeyManager가 단축키를
        // 차단하지 않게 되므로(NHotkeyManager.cs:177) Edit을 쓴다.
        _input.CallDeferred(LineEdit.MethodName.Edit);

        // 입력창 밖 클릭으로도 닫는다. **포커스로는 판정할 수 없어서** 좌표를 본다 —
        // 근거는 OutsideClickWatcher 주석. 감시는 입력창이 떠 있는 동안만 돈다.
        OutsideClickWatcher.Start(_input, Collapse);
    }

    /// <summary>Expanded를 닫는다. 내역이 있으면 Peek으로 내려가고 타이머를 다시 시작한다.</summary>
    private static void Collapse()
    {
        // 초안은 남긴다 — 다시 열면 이어 쓸 수 있다(HideInput 주석).
        HideInput(clearText: false);

        if (ChatService.History.Count == 0)
        {
            Hide();
            return;
        }

        State = ChatViewState.Peek;
        Refresh();
        StartPeekTimer();
    }

    private static void Hide()
    {
        State = ChatViewState.Hidden;
        CancelFade();

        if (_frame != null && GodotObject.IsInstanceValid(_frame))
        {
            _frame.Visible = false;
        }
    }

    /// <summary>
    /// 새 줄이 쌓였을 때. 자기가 보낸 것도 서비스를 거쳐 여기로 돌아오므로
    /// 화면 갱신 경로가 하나로 통일된다.
    /// </summary>
    private static void OnLineAdded(ChatLine line)
    {
        // 씬 전환 등으로 노드가 죽었을 수 있다. 그때는 다음 표시에서 다시 만든다.
        if (_root != null && !GodotObject.IsInstanceValid(_root))
        {
            Log.Info($"[{ModEntry.ModId}] ChatOverlay node was freed externally — rebuilding on next show.");
            ClearNodes();
        }

        if (State == ChatViewState.Expanded)
        {
            Refresh();
            return;
        }

        if (!EnsureNodes())
        {
            return;
        }

        State = ChatViewState.Peek;
        CancelFade();
        _frame!.Modulate = new Color(1f, 1f, 1f, 1f);
        Refresh();
        StartPeekTimer();
    }

    /// <summary>
    /// 타이머는 **줄 단위가 아니라 블록 단위**다 — 마지막 메시지로부터 5초를 잰다.
    /// 줄마다 개별 타이머를 두면 오가는 대화에서 앞 줄이 먼저 사라져 맥락이 끊긴다.
    /// </summary>
    private static void StartPeekTimer()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            return;
        }

        int generation = ++_peekGeneration;
        var timer = tree.CreateTimer(PeekSeconds);
        timer.Timeout += () =>
        {
            if (generation != _peekGeneration || State != ChatViewState.Peek)
            {
                return;
            }

            StartFadeOut();
        };
    }

    private static void StartFadeOut()
    {
        if (_frame == null || !GodotObject.IsInstanceValid(_frame))
        {
            return;
        }

        CancelFade();

        _fade = _frame.CreateTween();
        _fade.TweenProperty(_frame, "modulate:a", 0f, FadeSeconds);
        _fade.TweenCallback(Callable.From(Hide));
    }

    private static void CancelFade()
    {
        if (_fade != null && _fade.IsValid())
        {
            _fade.Kill();
        }

        _fade = null;
    }

    /// <summary>내역을 현재 상태에 맞게 다시 그리고 위치를 잡는다.</summary>
    private static void Refresh()
    {
        if (_rows == null || !GodotObject.IsInstanceValid(_rows) || _frame == null)
        {
            return;
        }

        _frame.Visible = true;
        _frame.Modulate = new Color(1f, 1f, 1f, 1f);

        // **QueueFree만으로는 부족하다.** 실제 제거가 프레임 끝에 일어나므로 바로 뒤에서 크기를
        // 재면 옛 행과 새 행이 함께 잡혀 창이 내용보다 커진다. 트리에서 먼저 떼어내
        // 크기 계산에서 즉시 빠지게 한다.
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }

        var font = LocaleFont.Get();
        var history = ChatService.History;
        int max = State == ChatViewState.Expanded ? ExpandedLines : PeekLines;
        int start = history.Count > max ? history.Count - max : 0;

        SenderColumn.Recalculate(font, FontSize, CollectSenderPrefixes());

        for (int i = start; i < history.Count; i++)
        {
            var line = history[i];

            // **표시 형식은 그릴 때마다 다시 조립한다.** 캐릭터는 런 도중 바뀔 수 있어
            // 줄에 박아 두면 낡은 값이 굳는다.
            var sender = ChatService.DescribeSender(line.SenderId);
            _rows.AddChild(ChatRow.Create(
                FormatSender(sender), line.Text, sender.Color, font, FontSize));
        }

        if (_notice != null)
        {
            _rows.AddChild(ChatRow.Create("!", _notice, null, font, FontSize));
        }

        // 두 번 잡는다. 지금 한 번 — deferred가 어떤 이유로든 실행되지 않아도 창이 보이도록.
        // 그리고 프레임 종료 후 한 번 — 컨테이너 최소 크기는 자식이 배치된 뒤에야 확정되므로
        // 지금 잰 값은 방금 추가한 행을 반영하지 못한다.
        // (C# static 메서드라 CallDeferred(name)이 아닌 Callable.From을 쓴다.)
        Reposition();
        Callable.From(Reposition).CallDeferred();
    }

    /// <summary>
    /// 배치를 다시 계산해 위치를 잡는다. 위치 계산 자체는 <see cref="ChatPlacement"/>가
    /// 단일 출처다 — 여기서는 성장 방향만 좌표로 옮긴다.
    ///
    /// **예외를 삼키지 않는다.** deferred 호출 안에서 터진 예외는 Godot이 조용히 흘려보내
    /// 크기가 0으로 남고, 그러면 창이 보이지 않는데 LineEdit은 포커스를 잡아 키만 먹는
    /// 진단하기 어려운 상태가 된다.
    /// </summary>
    private static void Reposition()
    {
        if (_frame == null || !GodotObject.IsInstanceValid(_frame))
        {
            return;
        }

        try
        {
            RepositionCore(_frame);
        }
        catch (System.Exception e)
        {
            Log.Error($"[{ModEntry.ModId}] ChatOverlay.Reposition failed: {e}");

            // 배치 계산이 실패해도 창은 보여야 한다. 화면 좌상단에 최소 크기로라도 띄운다.
            _frame.CustomMinimumSize = new Vector2(ChatPlacement.Width, 0f);
            _frame.Size = new Vector2(ChatPlacement.Width, _frame.GetCombinedMinimumSize().Y);
            _frame.Position = new Vector2(48f, 48f);
        }
    }

    private static void RepositionCore(Control frame)
    {
        var placement = ChatPlacement.Resolve(frame);
        frame.CustomMinimumSize = new Vector2(placement.Width, 0f);

        float height = frame.GetCombinedMinimumSize().Y;
        frame.Size = new Vector2(placement.Width, height);

        float y = placement.Grow == GrowDirection.Up
            ? placement.Anchor.Y - height
            : placement.Anchor.Y;

        // 배치가 어긋나도 창이 화면 밖으로 사라지지는 않게 한다 — 자세한 이유는 ClampToScreen.
        frame.Position = ChatPlacement.ClampToScreen(
            new Vector2(placement.Anchor.X, y),
            frame.Size,
            frame.GetViewportRect().Size);
    }

    /// <summary>진단용 기하 덤프. <c>chatstatus</c>가 쓴다.</summary>
    public static string DescribeGeometry()
    {
        if (_frame == null || !GodotObject.IsInstanceValid(_frame))
        {
            return "frame=none";
        }

        int rowCount = _rows != null && GodotObject.IsInstanceValid(_rows) ? _rows.GetChildCount() : -1;

        return $"state={State} visible={_frame.Visible} pos={_frame.Position} size={_frame.Size} " +
               $"minSize={_frame.GetCombinedMinimumSize()} alpha={_frame.Modulate.A:F2} rows={rowCount} " +
               $"context={ChatContextResolver.Resolve()} senderCol={SenderColumn.Width:F0} " +
               $"inTree={_frame.IsInsideTree()}";
    }

    /// <summary>
    /// 발신자 열의 문자열. 런은 <c>닉네임(캐릭터명):</c>, 로비는 <c>닉네임:</c>다.
    ///
    /// **로비에서 캐릭터명을 붙이지 않는 이유**는 그 시점에 캐릭터가 확정 전이라
    /// 캐릭터 기반 식별이 성립하지 않기 때문이다. 채색도 같은 이유로 하지 않는다.
    ///
    /// **자르는 것은 닉네임뿐이다** — 캐릭터명은 자르지 않는다. 캐릭터명은 종류가 적고
    /// 짧으며, 잘리면 `(아이언클…)`처럼 식별자 역할을 잃는다. 길이 방어는 닉네임 절단과
    /// 열 폭 상한이 이미 맡는다.
    /// </summary>
    private static string FormatSender(SenderDisplay sender)
    {
        string nickname = SenderColumn.TruncateNickname(sender.Nickname);

        return string.IsNullOrEmpty(sender.CharacterTitle)
            ? nickname + ":"
            : $"{nickname}({sender.CharacterTitle}):";
    }

    /// <summary>
    /// 열 폭을 잴 접두사들.
    ///
    /// **런에서는 참가자 명단**을 기준으로 한다 — 아직 발언하지 않은 사람의 몫까지 미리
    /// 잡아 두므로, 그 사람이 처음 말할 때 열 폭이 출렁이지 않는다. 참가자는 런 중 늘지
    /// 않으므로 이 값은 방 진입 사이에 고정이다.
    ///
    /// **로비에서는 명단 경로가 없어 내역으로 대신한다.** 로비는 캐릭터명이 없어 접두사가
    /// 짧고, 열 폭이 조금 변해도 손해가 작다.
    /// </summary>
    private static IEnumerable<string> CollectSenderPrefixes()
    {
        var seen = new HashSet<string>();

        foreach (var senderId in ChatService.KnownParticipants)
        {
            string prefix = FormatSender(ChatService.DescribeSender(senderId));
            if (seen.Add(prefix))
            {
                yield return prefix;
            }
        }

        foreach (var line in ChatService.History)
        {
            string prefix = FormatSender(ChatService.DescribeSender(line.SenderId));
            if (seen.Add(prefix))
            {
                yield return prefix;
            }
        }
    }

    /// <summary>
    /// 창 배경. **잠정이다** — 배경을 아예 두지 않는 안을 검토 중이므로(2026-07-20 사용자),
    /// 결정이 이 메서드 하나에만 모이게 한다. 빼려면 <see cref="ShowBackground"/>를 false로
    /// 두면 되고 호출부는 손대지 않는다.
    ///
    /// 배경을 없앨 때 함께 볼 것: 캐릭터 색으로 <c>RemoteTargetingLineColor</c>를 고른
    /// 근거가 **"어두운 반투명 배경 위에서 그대로 읽힌다"**였다. 배경이 사라지면 밝은 게임
    /// 화면 위에 밝은 글자가 얹혀 가독성 판단을 다시 해야 한다.
    /// </summary>
    private static readonly bool ShowBackground = true;

    private static StyleBox CreateBackgroundStyle()
    {
        if (!ShowBackground)
        {
            return new StyleBoxEmpty();
        }

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0f, 0f, 0f, 0.55f),
            ContentMarginLeft = 14f,
            ContentMarginRight = 14f,
            ContentMarginTop = 10f,
            ContentMarginBottom = 10f,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6
        };

        return style;
    }

    /// <summary>노드가 없으면 만든다. 이미 살아 있으면 그대로 쓴다.</summary>
    private static bool EnsureNodes()
    {
        if (_root != null && GodotObject.IsInstanceValid(_root))
        {
            return true;
        }

        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            Log.Error($"[{ModEntry.ModId}] ChatOverlay: could not reach the scene tree.");
            return false;
        }

        _root = new CanvasLayer { Layer = 100, Name = "SpireChatOverlay" };

        // 손패 위는 카드를 드래그할 때 마우스가 반드시 지나는 영역이다. 입력을 가로채면
        // **카드가 안 나가는 치명적 회귀**가 된다. MouseFilter는 상속되지 않으므로
        // 컨테이너마다 개별 지정한다.
        var frame = new PanelContainer
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };
        frame.AddThemeStyleboxOverride("panel", CreateBackgroundStyle());
        _frame = frame;
        _root.AddChild(_frame);

        var column = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.AddChild(column);

        // **여유 공간을 위로 몰아주는 스페이서.** 이게 없으면 창이 내용보다 클 때
        // VBoxContainer가 내용을 위로 붙이고 아래를 비워 **입력창이 창 위쪽에 보인다.**
        // 스페이서가 늘어나면 입력창은 항상 최하단, 메시지는 그 위로 쌓인다 —
        // 창 크기가 어긋나더라도 배치가 무너지지 않는다.
        column.AddChild(new Control
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        });

        _rows = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        column.AddChild(_rows);

        _input = new LineEdit
        {
            PlaceholderText = "메시지 입력 후 Enter",
            CustomMinimumSize = new Vector2(0f, 40f),
            MaxLength = ChatLimits.MaxInputChars,
            Visible = false,

            // 입력창만 입력을 받는다. 나머지는 전부 Ignore다.
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _input.TextSubmitted += OnSubmitted;
        _input.GuiInput += OnInputGuiEvent;
        _input.FocusExited += OnInputFocusExited;
        column.AddChild(_input);

        tree.Root.AddChild(_root);
        LocaleFont.Apply(_input);

        return true;
    }

    private static void ClearNodes()
    {
        OutsideClickWatcher.Stop();
        _root = null;
        _frame = null;
        _rows = null;
        _input = null;
        _fade = null;
        State = ChatViewState.Hidden;
    }

    /// <summary>
    /// ESC로 채팅창을 닫는다. **반드시 이벤트를 소비해야 한다.**
    ///
    /// 소비하지 않으면 같은 ESC가 <c>_UnhandledInput</c> 단계까지 흘러가 **그 화면의 vanilla
    /// ESC 동작이 함께 발동한다** — 전투=일시정지, 맵=맵 닫힘, **로비=로비 이탈**.
    /// 게임의 가드(<c>NHotkeyManager.cs:175-180</c>)는 "지금 포커스를 쥔 <c>LineEdit</c>이
    /// 편집 중인가"를 보는데, <see cref="Collapse"/>가 <c>ReleaseFocus()</c>까지 하므로
    /// **우리 스스로 그 가드를 걷어내 버린다.** 따라서 가드에 기대지 말고 직접 소비한다.
    ///
    /// 게임 자신도 자기 <c>LineEdit</c> 래퍼에서 같은 방식을 쓴다 —
    /// <c>NMegaLineEdit._GuiInput</c>이 cancel을 처리한 뒤 <c>SetInputAsHandled()</c>를
    /// 호출한다(NMegaLineEdit.cs:71-76). 이 구조에서는 소비가 정석이고 증상 처치가 아니다.
    /// 소비 대상이 ESC 하나뿐이라 다른 키로 번지지도 않는다.
    /// </summary>
    private static void OnInputGuiEvent(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            return;
        }

        // 닫기 전에 소비한다 — Collapse()가 포커스·가시성을 건드리기 전에 끝내 둔다.
        _input?.GetViewport()?.SetInputAsHandled();
        Collapse();
    }

    /// <summary>
    /// 입력창이 포커스를 잃었을 때 — **ESC로 닫은 것과 같게 취급한다.**
    ///
    /// **왜 필요한가.** 입력창을 열어둔 채 다른 곳을 클릭하면 창이 남는데, 포커스를 쥔 채
    /// 편집 중이면 <c>NHotkeyManager</c>가 **게임 단축키를 전부 흘려보낸다**
    /// (NHotkeyManager.cs:177). 채팅을 연 사실을 잊으면 "단축키가 먹통"인 함정 상태가 된다.
    ///
    /// **왜 클릭 감지가 아니라 포커스인가.** 우리가 실제로 신경 쓰는 상태가 포커스이고,
    /// 클릭은 그것을 만드는 여러 원인 중 하나일 뿐이다. 게다가 전역 클릭을 보려면
    /// <c>MouseFilter = Ignore</c> 원칙(카드 드래그 회귀 방지)을 흔들어야 하고,
    /// <c>_UnhandledInput</c>으로 우회해도 **게임 UI가 소비한 클릭은 도달하지 않아**
    /// 정작 잡아야 할 경우를 놓친다.
    /// </summary>
    private static void OnInputFocusExited()
    {
        // 우리가 닫으면서 놓은 포커스다. 그대로 두면 닫기가 스스로를 재호출한다.
        if (_hidingInput)
        {
            return;
        }

        if (State != ChatViewState.Expanded)
        {
            return;
        }

        Collapse();
    }

    /// <summary>Enter로 확정했을 때. 보내기만 하고, 화면 반영은 수신 이벤트가 담당한다.</summary>
    private static void OnSubmitted(string text)
    {
        if (_input == null)
        {
            return;
        }

        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        ChatService.Send(text);
        _input.Clear();

        // **보낸 뒤에는 "방금 T로 연 상태"로 되돌린다.** Enter는 LineEdit의 편집 모드를 풀고
        // 전송 과정에서 목록도 다시 그려지므로, 상태를 부분적으로만 손보면 열었을 때와
        // 어긋난다. 열기와 같은 경로를 한 번 더 지나게 해 차이를 없앤다.
        EnterExpanded();
    }
}
