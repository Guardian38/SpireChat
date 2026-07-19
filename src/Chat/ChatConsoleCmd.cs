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
/// 지금은 개발용 진입점이다. 실제 사용 시에는 단축키(Enter 등)로 열게 되며,
/// 그때 게임 단축키와의 충돌 처리가 필요하다.
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
