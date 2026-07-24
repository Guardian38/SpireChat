namespace SpireChat.Chat;

/// <summary>
/// 채팅 길이 제한.
///
/// **입력 상한과 수신 상한이 다른 것은 의도다.** 프로토콜에는 기술적 제한이 없으므로
/// (문자열 길이 접두사가 32비트) 두 값 모두 우리가 정한다.
/// </summary>
public static class ChatLimits
{
    /// <summary>
    /// 입력창이 받는 최대 글자 수. 가독성 기준 — Peek의 좁은 폭에서 줄 수가 터지지 않게 한다.
    /// 다국어 확장 시 재검토 대상이라 상수 하나로 둔다(영어 50자는 8~10단어라 답답하다).
    /// </summary>
    public const int MaxInputChars = 50;

    /// <summary>
    /// 수신 문자열을 잘라내는 하드 상한. <see cref="MaxInputChars"/>보다 넉넉한 것이 핵심이다.
    ///
    /// **악의적 피어 방어가 아니라 견고성 장치다** — 우리 쪽 송신 검증에 구멍이 생기거나
    /// 길이 정책이 다른 버전이 섞였을 때 UI가 조용히 깨지는 것을 막는다.
    ///
    /// 두 값을 같게 두면 안 된다: <c>string.Length</c>는 UTF-16 코드 유닛이라 이모지가 2로
    /// 세지는 반면 Godot <c>LineEdit.MaxLength</c>는 UTF-32 문자 기준이다. 같은 값이면
    /// **송신 측이 통과시킨 문자열이 수신 측에서 잘리는** 어긋남이 생긴다.
    /// 상한은 "정상 메시지를 거르는 선"이 아니라 "비정상을 막는 선"이다.
    /// </summary>
    public const int MaxWireChars = 200;

    /// <summary>수신 문자열을 하드 상한으로 자른다. 상한 이하면 원본을 그대로 돌려준다.</summary>
    public static string ClampWire(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= MaxWireChars)
        {
            return text;
        }

        return text.Substring(0, MaxWireChars);
    }
}
