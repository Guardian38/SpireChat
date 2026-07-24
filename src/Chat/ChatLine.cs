namespace SpireChat.Chat;

/// <summary>채팅 한 줄. **표시 형식은 UI가 정한다** — 여기는 원재료만 담는다.</summary>
public readonly struct ChatLine
{
    public ChatLine(ulong senderId, string sender, string text)
    {
        SenderId = senderId;
        Sender = sender;
        Text = text;
    }

    public ulong SenderId { get; }

    /// <summary>표시용 발신자 이름. 이름을 못 구하면 peer id 문자열이다.</summary>
    public string Sender { get; }

    public string Text { get; }
}
