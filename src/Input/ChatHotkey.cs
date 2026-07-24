using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using SpireChat.Chat;

namespace SpireChat.Input;

/// <summary>
/// 채팅창을 여는 단축키(기본 T).
///
/// 두 단계로 등록된다:
///  1. Godot <c>InputMap</c>에 액션을 런타임 추가 — 게임의 project.godot에 없는 액션이라 필요하다.
///  2. <c>NHotkeyManager.PushHotkeyPressedBinding</c>으로 콜백을 붙인다 (public API).
///
/// **채팅 입력 중에는 이 키가 동작하지 않는다.** NHotkeyManager._UnhandledInput이 포커스를 쥔
/// LineEdit이 편집 중이면 모든 단축키를 흘려보내기 때문이다(NHotkeyManager.cs:169-180).
/// 그래서 T는 사실상 "열기" 전용이고, 닫기는 ESC가 담당한다 — 채팅 중 T는 글자로 입력된다.
/// 이 동작은 의도된 것이며, 덕분에 게임 단축키 충돌 처리를 따로 할 필요가 없다.
/// </summary>
public static class ChatHotkey
{
    /// <summary>게임 액션은 "mega_" 접두사를 쓴다. 우리는 모드 id로 충돌을 피한다.</summary>
    public const string ActionName = "spire_chat_toggle";

    /// <summary>
    /// 콜백을 붙여둔 매니저. NGame이 재생성되면 HotkeyManager도 새 인스턴스가 되므로,
    /// 단순 bool 플래그 대신 인스턴스를 비교해 그때 다시 등록한다.
    /// </summary>
    private static NHotkeyManager? _boundManager;

    /// <summary>
    /// 액션과 콜백이 등록돼 있는지 확인하고, 아니면 등록한다.
    /// NHotkeyManager에는 _Ready가 없고 Instance도 NGame 준비 전엔 null이라,
    /// 입력 처리 진입점에서 인스턴스를 직접 받아 등록한다(HotkeyBootstrapPatch).
    /// </summary>
    internal static void EnsureRegistered(NHotkeyManager manager)
    {
        if (ReferenceEquals(_boundManager, manager))
        {
            return;
        }

        EnsureInputMapAction();
        manager.PushHotkeyPressedBinding(ActionName, OnTogglePressed);
        _boundManager = manager;

        Log.Info($"[{ModEntry.ModId}] chat hotkey bound to '{ActionName}' (default: T).");
    }

    private static void EnsureInputMapAction()
    {
        if (InputMap.HasAction(ActionName))
        {
            return;
        }

        InputMap.AddAction(ActionName);

        // PhysicalKeycode를 쓰면 키보드 레이아웃·IME 상태와 무관하게 같은 물리 키를 잡는다.
        // 한글 입력 상태에서도 T 자리 키가 그대로 동작해야 하므로 이쪽이 맞다.
        InputMap.ActionAddEvent(ActionName, new InputEventKey { PhysicalKeycode = Key.T });
    }

    /// <summary>
    /// <c>T</c>가 눌렸을 때. **멀티 세션에서만 연다.**
    ///
    /// 조건에 맞지 않으면 **아무 일도 하지 않는다.** 안내 문구도 띄우지 않는다 —
    /// 메인 메뉴에서 T를 누른 사람은 대개 게임의 다른 조작을 의도한 것이라,
    /// 채팅 관련 안내가 뜨면 그쪽이 더 뜬금없다.
    ///
    /// 이미 열려 있으면 조건과 무관하게 닫을 수 있게 둔다 — 콘솔 <c>chat</c>으로 세션 밖에서
    /// 연 경우(개발 경로)에도 T로 닫히는 편이 자연스럽다.
    /// </summary>
    private static void OnTogglePressed()
    {
        if (!ChatService.CanChat && !ChatOverlay.IsOpen)
        {
            return;
        }

        ChatOverlay.Toggle();
    }
}
