using System.Text;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using SpireChat.Chat.Net;

namespace SpireChat.Chat;

/// <summary>
/// 콘솔 명령 <c>chatstatus</c> — 전송 계층의 현재 상태를 덤프한다.
///
/// **진단용이다.** 특히 로비→런 전환처럼 세션 상태가 바뀌는 구간에서, 채팅이 여전히
/// 같은 서비스에 붙어 있는지를 메시지를 주고받아 보지 않고도 즉시 확인하기 위한 것이다.
/// 전환 전후로 <c>netId</c>가 같으면 서비스가 유지된 것이고, 달라졌거나 <c>none</c>이면
/// 재등록 처리가 필요하다는 뜻이다.
/// </summary>
public class ChatStatusConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "chatstatus";

    public override string Args => "";

    public override string Description => "Dump spire_chat transport state (net service, history size).";

    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        var netService = NetServiceTracker.Current;
        var sb = new StringBuilder();

        if (netService == null)
        {
            sb.Append("net service: none (not in a multiplayer session)");
        }
        else
        {
            sb.Append($"net service: type={netService.Type} netId={netService.NetId} connected={netService.IsConnected}");
        }

        sb.Append($" | canSend={ChatService.CanSend}");
        sb.Append($" | history={ChatService.History.Count}");
        sb.Append($" | overlay={(ChatOverlay.IsOpen ? "open" : "closed")}");
        sb.Append($"\n  ui: {ChatOverlay.DescribeGeometry()}");

        return new CmdResult(success: true, sb.ToString());
    }
}
