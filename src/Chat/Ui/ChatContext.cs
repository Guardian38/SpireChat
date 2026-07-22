using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;
using SpireChat.Chat.Net;

namespace SpireChat.Chat.Ui;

/// <summary>채팅창이 놓일 화면 맥락. 상태가 아니라 **배치**를 가른다.</summary>
public enum ChatContext
{
    /// <summary>세션 밖. 콘솔 명령으로 띄운 경우 등 — 화면 하단 폴백을 쓴다.</summary>
    None,

    /// <summary>로비(캐릭터 선택 화면). 좌측 상단, 플레이어 목록 오른쪽.</summary>
    Lobby,

    /// <summary>런 중 전투 밖(맵·상점·이벤트). 화면 하단 고정 오프셋.</summary>
    RunOutOfCombat,

    /// <summary>런 중 전투. 손패 바로 위.</summary>
    Combat
}

/// <summary>지금이 어떤 맥락인지 판별한다.</summary>
public static class ChatContextResolver
{
    /// <summary>
    /// 현재 맥락을 구한다.
    ///
    /// **`Instance == null`로 판별하면 안 된다.** <c>RunManager.Instance</c>(RunManager.cs:78)와
    /// <c>CombatManager.Instance</c>(CombatManager.cs:90)는 <c>= new ...()</c>로 즉시 초기화되는
    /// 정적 프로퍼티라 **런 밖·전투 밖에서도 항상 non-null**이다. 반드시 <c>State</c>(런) /
    /// <c>NCombatRoom.Instance</c>(전투)로 판단한다.
    /// </summary>
    public static ChatContext Resolve()
    {
        // NPlayerHand.Instance는 NCombatRoom.Instance?.Ui.Hand라 전투 밖에서 자연히 null이다
        // (NPlayerHand.cs:471). 손패 위치 획득과 전투 판별을 한 번에 해결한다.
        if (NPlayerHand.Instance != null)
        {
            return ChatContext.Combat;
        }

        // IsInProgress는 State != null과 같다(RunManager.cs:104). Instance 자체는 항상 non-null이다.
        if (RunManager.Instance.IsInProgress)
        {
            return ChatContext.RunOutOfCombat;
        }

        return NetServiceTracker.Current != null ? ChatContext.Lobby : ChatContext.None;
    }
}
