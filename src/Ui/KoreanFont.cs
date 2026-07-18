using Godot;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Logging;

namespace SpireChat.Ui;

/// <summary>
/// 게임에 번들된 한글 폰트를 가져와 Control에 적용한다.
///
/// 게임의 확장 메서드 <c>Control.ApplyLocaleFontSubstitution</c>은 현재 로케일이 kor일 때만
/// 동작하는데, 게임에 한국어 로케일이 없어 항상 무반응이다. 그래서 FontManager를
/// **로케일과 무관하게** 직접 호출한다.
///
/// 현재는 폰트를 통째로 덮어쓰므로 영문·숫자까지 경기천년바탕으로 렌더된다.
/// 게임 기본 폰트 + 한글 폴백 체인으로 바꾸는 것이 이상적이나, <c>.tres</c>가 FontFile인지
/// FontVariation인지에 따라 방법이 갈려 미조사 상태다. 우선 동작을 확보하고 뒤로 미룬다.
/// </summary>
public static class KoreanFont
{
    /// <summary>게임 3글자 언어 코드. FontManager의 폰트 경로 맵 키.</summary>
    private const string LanguageCode = "kor";

    /// <summary>Godot Control 테마의 폰트 항목 이름. LineEdit·Label 모두 "font"를 쓴다.</summary>
    private const string ThemeFontName = "font";

    /// <summary>
    /// 번들 한글 폰트를 얻는다. FontManager가 내부적으로 캐시하므로 반복 호출해도 싸다.
    /// </summary>
    public static Font? Get()
    {
        var font = FontManager.GetSubstituteFont(LanguageCode, FontType.Regular);
        if (font == null)
        {
            Log.Error($"[{ModEntry.ModId}] Failed to load the bundled Korean font.");
        }
        return font;
    }

    /// <summary>주어진 Control들에 한글 폰트를 적용한다. 실패하면 아무것도 하지 않는다.</summary>
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
