using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;

namespace SpireChat.Chat.Net;

/// <summary>
/// 세션 시작을 감지하는 유일한 패치. 훅 지점을 여기로 고른 이유는
/// <see cref="NetServiceTracker"/> 주석 참조.
///
/// Postfix인 이유: 생성자가 끝난 뒤여야 서비스가 온전한 상태다. 원본은 건드리지 않는다.
/// </summary>
[HarmonyPatch(typeof(PeerInputSynchronizer), MethodType.Constructor, typeof(INetGameService))]
internal static class NetServiceTrackerPatch
{
    private static void Postfix(INetGameService netService)
    {
        NetServiceTracker.Adopt(netService);
    }
}
