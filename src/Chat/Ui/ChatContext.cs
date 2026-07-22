using SpireChat.Chat.Net;
using SpireChat.Game;

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
    /// 현재 맥락을 구한다. 런 상태는 <see cref="RunTracker"/>에만 묻는다.
    ///
    /// **전투를 노드 존재로 판별하지 않는다.** <c>NPlayerHand.Instance</c>로 보면 방 진입
    /// 시점에는 노드가 아직 없어 전투 밖으로 읽히는데, 재배치가 필요한 시점이 바로 그때다.
    /// 전투 배치는 뷰포트만 쓰므로 노드가 없어도 계산 결과가 맞다.
    /// </summary>
    public static ChatContext Resolve()
    {
        if (RunTracker.IsInCombatRoom)
        {
            return ChatContext.Combat;
        }

        if (RunTracker.IsInRun)
        {
            return ChatContext.RunOutOfCombat;
        }

        return NetServiceTracker.Current != null ? ChatContext.Lobby : ChatContext.None;
    }
}
