using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Logging;

namespace SpireChat.Ui;

/// <summary>
/// 현재 로케일의 폰트를 Control에 적용한다. 게임 번들 폰트를 그대로 쓴다 — 동봉하지 않는다.
///
/// **언어를 하드코딩하지 않는다.** 게임은 한국어를 정식 지원하므로
/// <c>LocManager.Instance.Language</c>가 실제로 <c>kor</c>을 돌려준다. 특정 언어로 고정하면
/// 일본어·중국어·태국어 유저에게 잘못된 폰트가 적용된다.
///
/// **폴백 체인을 만들지 않는다.** 게임 자신이 <c>AddThemeFontOverride</c>로 라벨 폰트를 통째
/// 교체하므로(FontControlUtils.cs:13-19), 같은 방식이 곧 게임과 동일한 룩이다. 로케일과 다른
/// 문자는 vanilla와 똑같이 깨진다 — 게임도 처리하지 않는 영역이다.
/// </summary>
public static class LocaleFont
{
    /// <summary>Godot Control 테마의 폰트 항목 이름. LineEdit·Label 모두 "font"를 쓴다.</summary>
    private const string ThemeFontName = "font";

    /// <summary>
    /// 현재 로케일에 필요한 폰트를 얻는다. 치환이 필요 없는 로케일(영어 등)이면 null이며,
    /// 그때는 게임 기본 폰트를 그대로 쓰면 된다 — 오류가 아니다.
    ///
    /// FontManager가 ResourceLoader CacheMode.Reuse로 캐시하므로 반복 호출해도 싸다.
    /// **우리가 따로 캐시를 만들지 않는다.**
    /// </summary>
    public static Font? Get()
    {
        var locManager = LocManager.Instance;
        if (locManager == null)
        {
            return null;
        }

        string language = locManager.Language;
        if (!FontManager.NeedsFontSubstitution(language))
        {
            return null;
        }

        var font = FontManager.GetSubstituteFont(language, FontType.Regular);
        if (font == null)
        {
            Log.Error($"[{ModEntry.ModId}] Failed to load the substitute font for locale '{language}'.");
        }

        return font;
    }

    /// <summary>주어진 Control들에 로케일 폰트를 적용한다. 치환이 불필요하면 아무것도 하지 않는다.</summary>
    public static bool Apply(params Control[] controls)
    {
        var font = Get();
        if (font == null)
        {
            return false;
        }

        foreach (var control in controls)
        {
            control.AddThemeFontOverride(ThemeFontName, font);
        }

        return true;
    }

    /// <summary>적용을 되돌린다 (폰트 문제와 다른 문제를 구분할 때 쓴다).</summary>
    public static void Remove(params Control[] controls)
    {
        foreach (var control in controls)
        {
            control.RemoveThemeFontOverride(ThemeFontName);
        }
    }
}
