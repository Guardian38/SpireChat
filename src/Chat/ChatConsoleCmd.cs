using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;

namespace SpireChat.Chat;

/// <summary>
/// 콘솔 명령 <c>chat</c> — 채팅 오버레이를 토글한다.
///
/// 게임이 모드 어셈블리의 <see cref="AbstractConsoleCmd"/> 서브타입을 자동 등록하므로
/// 별도 등록 코드가 필요 없다. public + 매개변수 없는 생성자라야 인스턴스화된다.
///
/// **열림 조건을 일부러 우회한다.** <c>T</c>는 멀티 세션에서만 열리지만
/// 이 명령은 세션 밖에서도 연다 — 레이아웃·폰트를 세션 없이 확인하는 개발 수단이기 때문이다.
/// <c>AbstractConsoleCmd.DebugOnly</c>가 기본 <c>true</c>라 일반 사용자에게는 노출되지 않는다.
/// </summary>
public class ChatConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "chat";

    public override string Args => "";

    public override string Description => "Toggle the chat overlay.";

    // 로컬 UI 토글일 뿐이다. 네트워크로 전파되면 다른 플레이어 화면에도 뜬다.
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        return new CmdResult(success: true, ChatOverlay.Toggle());
    }
}
