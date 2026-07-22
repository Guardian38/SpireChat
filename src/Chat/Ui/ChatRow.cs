using Godot;

namespace SpireChat.Chat.Ui;

/// <summary>
/// 채팅 한 줄 — **발신자 열과 본문 열을 나눈 2열 구조**.
///
/// **미관이 아니라 가독성 결함의 수정이다.** 한 Label에 통으로 넣으면 줄바꿈된 둘째 줄이
/// 발신자 자리에서 시작해 다음 발언의 발신자와 세로로 섞인다. Peek은 주변시로 흘려보는
/// 물건이라 이 혼동 비용이 크다.
///
/// **경계선·배경을 넣지 않는다.** 정렬 자체가 구분자로 작동하고, 선을 그으면 게임 UI에 없는
/// 조형이라 이질감이 생긴다. 발신자 열을 **우측 정렬**해 콜론이 세로로 맞아떨어지게 한다.
/// </summary>
public static class ChatRow
{
    /// <summary>발신자 열과 본문 열 사이 간격.</summary>
    private const int ColumnGap = 10;

    /// <summary>
    /// 한 줄을 만든다. <paramref name="senderColor"/>가 null이면 채색하지 않는다
    /// (로비는 캐릭터가 확정 전이라 채색하지 않는다).
    /// </summary>
    public static HBoxContainer Create(string sender, string body, Color? senderColor, Font? font, int fontSize)
    {
        var row = new HBoxContainer
        {
            // MouseFilter는 자식에게 상속되지 않는다. 행을 만드는 이 한 곳에서
            // 컨테이너와 양쪽 Label 전부에 지정해 빠뜨림을 막는다.
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        row.AddThemeConstantOverride("separation", ColumnGap);

        var senderLabel = new Label
        {
            Text = sender,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            CustomMinimumSize = new Vector2(SenderColumn.Width, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,

            // 글자 수가 아니라 렌더 폭으로 자른다 — 혼용 스크립트에서 정확하다.
            // 열 폭이 참가자 접두사에 맞춰져 있어 평상시에는 발동하지 않는다.
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };

        if (senderColor.HasValue)
        {
            senderLabel.AddThemeColorOverride("font_color", senderColor.Value);
        }

        var bodyLabel = new Label
        {
            Text = body,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            VerticalAlignment = VerticalAlignment.Top,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        ApplyFont(senderLabel, font, fontSize);
        ApplyFont(bodyLabel, font, fontSize);

        row.AddChild(senderLabel);
        row.AddChild(bodyLabel);
        return row;
    }

    private static void ApplyFont(Label label, Font? font, int fontSize)
    {
        if (font != null)
        {
            label.AddThemeFontOverride("font", font);
        }

        label.AddThemeFontSizeOverride("font_size", fontSize);
    }
}
