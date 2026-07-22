using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using SpireChat.Chat;
using SpireChat.Game;

namespace SpireChat;

/// <summary>
/// 모드 진입점.
///
/// 로더 동작(ModManager.TryLoadMod): DLL을 게임의 AssemblyLoadContext에 로드한 뒤
/// [ModInitializer]가 붙은 타입의 지정 메서드를 호출한다. 지정 메서드는 static이어야 한다.
/// [ModInitializer]가 없으면 로더가 알아서 Harmony 인스턴스를 만들고 PatchAll을 호출하지만,
/// 초기화 로그를 직접 통제하기 위해 명시적으로 둔다.
///
/// 조작: <c>T</c>로 채팅창 열기, <c>ESC</c>로 닫기.
/// 콘솔 명령: <c>chat</c>(채팅 토글), <c>imeprobe</c>(IME 진단, 검증 완료).
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public const string ModId = "spire_chat";

    public static void Initialize()
    {
        Log.Info($"[{ModId}] initializing...");

        // Harmony id는 모드별로 유일해야 한다. author.modId 규약을 따른다.
        var harmony = new Harmony($"GuardianGD.{ModId}");
        harmony.PatchAll(Assembly.GetExecutingAssembly());

        // 런 이벤트 구독은 가장 먼저 건다 — 이후 구성요소가 전부 이 창구를 통해 런을 본다.
        RunTracker.Initialize();

        // 세션 추적·수신 보관을 시작한다. 채팅창을 한 번도 열지 않아도 메시지는 쌓여야 하므로
        // UI가 아니라 여기서 켠다.
        ChatService.Initialize();

        // 창을 한 번도 열지 않아도 메시지가 오면 Peek이 떠야 하므로 여기서 구독을 건다.
        ChatOverlay.Initialize();

        int patched = harmony.GetPatchedMethods().Count();
        Log.Info($"[{ModId}] loaded. Applied {patched} Harmony patch(es). Press T to open chat.");
    }
}
