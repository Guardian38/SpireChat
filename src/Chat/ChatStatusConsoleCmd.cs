using System.Text;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using SpireChat.Chat.Net;
using SpireChat.Chat.Ui;

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

        // 배치 기준 덤프는 오버레이가 없어도 찍는다 — 창을 띄우지 않고도 기하를 재기 위함이다.
        sb.Append($"\n  anchors: {PlacementProbe.Describe()}");

        string dump = sb.ToString();
        LogDump(dump);

        return new CmdResult(success: true, dump);
    }

    /// <summary>
    /// 덤프를 <c>godot.log</c>에도 남긴다. 콘솔 출력은 화면에만 있어 눈으로 옮겨 적어야 하는데,
    /// 배치 실측은 픽셀 단위 대조라 그 과정에서 값이 틀어지면 그대로 잘못된 상수가 된다.
    ///
    /// **줄마다 따로 남기는 것이 요점이다** — 한 덩어리로 남기면 <c>[spire_chat]</c>으로 훑을 때
    /// 첫 줄만 걸리고 정작 좌표가 있는 나머지 줄이 빠진다.
    /// </summary>
    private static void LogDump(string dump)
    {
        foreach (string line in dump.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                Log.Info($"[{ModEntry.ModId}] chatstatus | {trimmed}");
            }
        }
    }
}
