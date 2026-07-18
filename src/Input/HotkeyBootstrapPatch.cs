using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace SpireChat.Input;

/// <summary>
/// 단축키 콜백을 등록할 시점을 잡기 위한 패치.
///
/// <c>NHotkeyManager</c>에는 <c>_Ready</c>가 없고, <c>NHotkeyManager.Instance</c>는
/// <c>NGame.Instance</c>가 준비되기 전까지 null이다. 모드 초기화 시점에 등록할 수 없다는 뜻이다.
///
/// 그래서 입력 처리 진입점에 붙어 **인스턴스를 직접 받아** 최초 1회 등록한다.
/// 매 입력마다 호출되지만 참조 비교 한 번이라 비용은 무시할 수 있다.
///
/// Prefix인 이유: Postfix는 <c>_UnhandledInput</c>이 조기 return한 경우에도 실행되므로
/// 진입 여부와 무관하게 불린다. 어느 쪽이든 등록만 하면 되지만, 등록을 가능한 이른
/// 프레임에 끝내려고 Prefix를 쓴다. 원본 동작은 건드리지 않는다(void, 항상 통과).
/// </summary>
[HarmonyPatch(typeof(NHotkeyManager), "_UnhandledInput")]
internal static class HotkeyBootstrapPatch
{
    private static void Prefix(NHotkeyManager __instance)
    {
        ChatHotkey.EnsureRegistered(__instance);
    }
}
