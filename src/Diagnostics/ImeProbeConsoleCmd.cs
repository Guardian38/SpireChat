using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;

namespace SpireChat.Diagnostics;

/// <summary>
/// 콘솔 명령 <c>imeprobe</c> — IME 입력 검증 오버레이를 토글한다.
///
/// 게임이 모드 어셈블리의 <see cref="AbstractConsoleCmd"/> 서브타입을 자동 등록하므로
/// (DevConsole 생성자 → <c>ReflectionHelper.GetSubtypesInMods&lt;AbstractConsoleCmd&gt;()</c>)
/// 별도 등록 코드가 필요 없다. <c>DebugOnly</c>는 기본 true지만 모드 구동 중
/// (<c>ModManager.IsRunningModded()</c>) 자동 허용된다.
///
/// public + 매개변수 없는 생성자라야 <c>Activator.CreateInstance</c>로 인스턴스화된다.
///
/// 사용법:
///   imeprobe        오버레이 토글
///   imeprobe font   한글 폰트 적용 토글 (IME 문제 / 폰트 문제 구분용)
/// </summary>
public class ImeProbeConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "imeprobe";

    public override string Args => "[font]";

    public override string Description => "Toggle the IME input probe overlay. 'font' toggles the Korean font override.";

    // 순수 로컬 진단 도구다. 네트워크로 전파되면 다른 플레이어 화면에도 뜬다.
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (args.Length > 0 && args[0].Equals("font", System.StringComparison.OrdinalIgnoreCase))
        {
            if (!ImeProbe.IsOpen)
            {
                return new CmdResult(success: false, "Open the probe first: imeprobe");
            }

            return new CmdResult(success: true, ImeProbe.ToggleFont());
        }

        return new CmdResult(success: true, ImeProbe.Toggle());
    }
}