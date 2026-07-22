using System;
using System.Collections.Generic;
using Godot;

namespace SpireChat.Chat.Ui;

/// <summary>
/// 발신자 열의 폭을 정한다.
///
/// **글자 수를 세지 않고 렌더 폭을 잰다.** 한글 16자는 라틴 16자의 약 2배 폭이라 글자 수는
/// 폭 상한으로 번역되지 않는다. <c>Font.GetStringSize</c>로 재면 어느 언어든 그대로 맞는다.
///
/// 폭은 **참가자 전원의 접두사 최대치**로 잡는다 — 고정 상수는 짧은 닉네임뿐일 때 낭비이고,
/// "표시 중인 줄의 최대치"는 줄이 만료될 때마다 출렁인다(Peek은 5초 후 줄이 사라진다).
/// </summary>
public static class SenderColumn
{
    /// <summary>닉네임 표시 상한(글자 수). 실사용 피드백으로 바뀔 수 있어 상수로 둔다.</summary>
    public const int MaxNicknameChars = 16;

    /// <summary>
    /// 열 폭의 절대 상한(픽셀). 한 명의 긴 닉네임이 본문 자리를 다 먹는 것을 막는다.
    /// <see cref="MaxNicknameChars"/> 절단이 이미 최악을 절반으로 묶으므로 실제로는 드물게 걸린다.
    /// </summary>
    private const float MaxWidth = 220f;

    /// <summary>열 폭의 하한. 참가자가 없거나 측정에 실패했을 때의 기본값.</summary>
    private const float MinWidth = 80f;

    private static float _width = MinWidth;

    /// <summary>현재 열 폭. 측정 전에는 하한값이다.</summary>
    public static float Width => _width;

    /// <summary>
    /// 참가자들의 접두사로 열 폭을 다시 잰다.
    ///
    /// **캐시가 갱신되는 시점에 함께 부른다** — 로비는 플레이어 입퇴장, 런은 방 진입
    /// (<see cref="SenderRegistry"/>와 동일 훅). 줄어드는 방향도 허용한다: 재계산이 그 시점에만
    /// 일어나므로 방 안에서 흔들릴 일이 없고, 방향을 제한하면 넓어진 열이 계속 남기만 한다.
    ///
    /// **폰트 적용 이후에 불러야 한다.** 폰트가 다르면 글자 폭이 달라진다.
    /// </summary>
    public static void Recalculate(Font? font, int fontSize, IEnumerable<string> prefixes)
    {
        if (font == null)
        {
            _width = MinWidth;
            return;
        }

        float widest = 0f;
        foreach (var prefix in prefixes)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                continue;
            }

            float w = font.GetStringSize(prefix, HorizontalAlignment.Left, -1f, fontSize).X;
            if (w > widest)
            {
                widest = w;
            }
        }

        _width = Math.Clamp(widest, MinWidth, MaxWidth);
    }

    /// <summary>표시용으로 닉네임을 자른다. 저장된 원본은 손대지 않는다.</summary>
    public static string TruncateNickname(string nickname)
    {
        if (string.IsNullOrEmpty(nickname) || nickname.Length <= MaxNicknameChars)
        {
            return nickname;
        }

        return nickname.Substring(0, MaxNicknameChars) + "…";
    }
}
