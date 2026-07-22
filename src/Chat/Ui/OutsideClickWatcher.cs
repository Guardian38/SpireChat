using System;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace SpireChat.Chat.Ui;

/// <summary>
/// **입력창 밖을 클릭했는지**만 감시한다. 채팅을 연 것을 잊고 게임을 계속하면
/// <c>NHotkeyManager</c>가 단축키를 전부 흘려보내는 함정(NHotkeyManager.cs:177)을 없애기 위한 것.
///
/// **왜 포커스가 아니라 좌표인가.** 이 게임에서 Godot 포커스는 **컨트롤러 내비게이션 전용**이라
/// 마우스로 클릭해도 대부분의 UI가 포커스를 가져가지 않는다 — 게임 자신의 "focus"는 호버다
/// (<c>NClickableControl.cs:451-453</c>, <c>NodeUtil.TryGrabFocus:303-308</c>).
/// 그래서 <c>FocusExited</c>는 "딴 데를 클릭했다"가 아니라 "클릭 대상이 focusable이었다"를 뜻하고,
/// 실제로 카드·일반 인터페이스 클릭에서는 아예 발화하지 않았다(인게임 확인).
///
/// **클릭 대상을 보지 않는 것이 이 방식의 요점이다.** 좌표만 보므로 카드든 버튼이든 빈 공간이든
/// 균일하게 걸리고, 씬의 <c>FocusMode</c> 설정에 의존하지 않아 게임 업데이트에도 안 깨진다.
///
/// 커스텀 Node를 만들 수 없어 <c>_Process</c>를 쓰지 못하므로
/// <c>SceneTree.ProcessFrame</c>에 붙는다 — 게임 자신도 쓰는 경로다(<c>ActionExecutor.cs:154</c>).
/// **감시는 입력창이 떠 있는 동안만** 돈다(<see cref="Start"/>/<see cref="Stop"/>).
/// </summary>
internal static class OutsideClickWatcher
{
    private static LineEdit? _target;
    private static SceneTree? _tree;
    private static Action? _onOutsideClick;

    /// <summary>
    /// 직전 프레임의 버튼 눌림 상태. **누른 순간(에지)만** 계기로 삼기 위한 것이다.
    ///
    /// 이것이 곧 "드래그 시작이 입력창 안이면 닫지 않는다"의 구현이기도 하다 — 뗄 때가 아니라
    /// 누를 때의 좌표로 판정하므로, 입력창 안에서 눌러 밖에서 뗀 텍스트 드래그 선택은 걸리지 않는다.
    /// </summary>
    private static bool _wasPressed;

    public static bool IsWatching => _target != null;

    /// <summary>
    /// 감시를 시작한다. 이미 돌고 있으면 대상만 갈아끼운다.
    ///
    /// **시작 시점의 버튼 상태를 그대로 물려받는다** — 누르고 있는 채로 시작하면 그 버튼은
    /// 뗐다 다시 누를 때까지 계기가 되지 않는다.
    /// </summary>
    public static void Start(LineEdit target, Action onOutsideClick)
    {
        Stop();

        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            Log.Error($"[{ModEntry.ModId}] OutsideClickWatcher: could not reach the scene tree.");
            return;
        }

        _target = target;
        _tree = tree;
        _onOutsideClick = onOutsideClick;
        _wasPressed = AnyButtonPressed();

        tree.ProcessFrame += OnProcessFrame;
    }

    public static void Stop()
    {
        if (_tree != null && GodotObject.IsInstanceValid(_tree))
        {
            _tree.ProcessFrame -= OnProcessFrame;
        }

        _target = null;
        _tree = null;
        _onOutsideClick = null;
        _wasPressed = false;
    }

    /// <summary>
    /// 좌·우클릭 모두 계기다. 우클릭도 게임 조작(카드 확대 등)이라 함정에 빠지는 조건이 같다.
    ///
    /// <c>Godot.</c>을 붙이는 이유: 모드에 <c>SpireChat.Input</c> 네임스페이스(단축키 쪽)가 있어
    /// 그냥 <c>Input</c>이라고 쓰면 그쪽이 잡힌다.
    /// </summary>
    private static bool AnyButtonPressed()
    {
        return Godot.Input.IsMouseButtonPressed(MouseButton.Left)
            || Godot.Input.IsMouseButtonPressed(MouseButton.Right);
    }

    private static void OnProcessFrame()
    {
        // 씬 전환 등으로 입력창이 죽었다. 감시할 대상이 없으므로 스스로 내려온다.
        if (_target == null || !GodotObject.IsInstanceValid(_target))
        {
            Stop();
            return;
        }

        bool pressed = AnyButtonPressed();
        bool justPressed = pressed && !_wasPressed;
        _wasPressed = pressed;

        if (!justPressed)
        {
            return;
        }

        // **"안"의 기준은 입력창 Rect 하나뿐이다.** 메시지 영역은 MouseFilter.Ignore라 클릭이
        // 어차피 게임으로 흘러가므로(카드가 눌린다), 그쪽을 "안"으로 치면 게임은 반응했는데
        // 채팅만 안 닫히는 어긋남이 생긴다.
        if (_target.GetGlobalRect().HasPoint(_target.GetGlobalMousePosition()))
        {
            return;
        }

        // 클릭은 소비하지 않는다 — 이미 지나간 프레임의 상태를 읽을 뿐이라 소비할 수도 없다.
        // 카드를 클릭하면 카드가 나가면서 채팅도 함께 닫히는 것이 의도된 동작이다.
        Action? callback = _onOutsideClick;
        Stop();
        callback?.Invoke();
    }
}
