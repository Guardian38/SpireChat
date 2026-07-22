using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using SpireChat.Ui;

namespace SpireChat.Diagnostics;

/// <summary>
/// IME 한글 입력 검증용 오버레이.
///
/// **이 파일은 채팅 기능의 일부가 아니다.** 프로젝트 최대 위험 요소인
/// "게임 안에서 한글 조합 입력이 되는가"를 확인하기 위한 일회성 진단 도구이며,
/// 검증이 끝나면 제거하거나 디버그 전용으로 남긴다.
///
/// 확인하려는 것 3가지 — 셋은 원인이 다르므로 반드시 구분해서 판정한다:
///  1. IME 조합이 붙는가      → 타이핑 중 글자가 입력창에 나타나는가 (눈으로)
///  2. 완성 문자가 들어오는가  → 코드포인트 덤프에 U+AC00~U+D7A3이 찍히는가
///  3. 폰트가 렌더하는가       → 글자가 두부(□)로 보이지 않는가
///
/// 특히 2번이 되는데 3번이 안 되면 "IME는 정상, 폰트만 문제"이므로 해결이 쉽다.
/// 이 구분을 위해 폰트 적용을 런타임에 토글할 수 있게 했다(`imeprobe font`).
///
/// 커스텀 Node 서브클래스를 만들지 않고 순수 Godot 노드 조합 + C# 시그널로만 구성한다
/// — 모드 어셈블리의 스크립트 클래스를 씬 트리에 등록하는 문제를 통째로 회피할 수 있다.
/// </summary>
public static class ImeProbe
{
    private static CanvasLayer? _root;
    private static LineEdit? _input;
    private static Label? _dump;
    private static Label? _status;
    private static bool _fontApplied = true;

    public static bool IsOpen => _root != null && GodotObject.IsInstanceValid(_root);

    /// <summary>오버레이를 토글한다. 반환값은 토글 후 상태 설명(콘솔 출력용).</summary>
    public static string Toggle()
    {
        if (IsOpen)
        {
            Close();
            return "IME probe: CLOSED";
        }

        Open();
        return IsOpen
            ? "IME probe: OPEN — switch to Korean IME and type. ESC closes."
            : "IME probe: FAILED to open (scene tree unavailable) — check the log.";
    }

    /// <summary>한글 폰트 적용을 토글한다. IME 문제와 폰트 문제를 구분하기 위한 것.</summary>
    public static string ToggleFont()
    {
        _fontApplied = !_fontApplied;
        ApplyFont();
        return $"IME probe font override: {(_fontApplied ? "ON (kor)" : "OFF (game default)")}";
    }

    private static void Open()
    {
        // 콘솔 명령 컨텍스트에는 Node가 없으므로 메인 루프에서 직접 씬 트리를 얻는다.
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            Log.Error("[spire_chat] ImeProbe: could not reach the scene tree.");
            return;
        }

        // layer를 높게 두어 게임 UI 위에 올린다. 개발자 콘솔보다는 낮게 잡아
        // 콘솔로 다시 명령을 칠 수 있게 한다.
        _root = new CanvasLayer { Layer = 100, Name = "SpireChatImeProbe" };

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        // 배경이 클릭을 먹으면 입력창 포커스가 흔들리므로 통과시킨다.
        dim.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(dim);

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        var panel = new PanelContainer();
        center.AddChild(panel);

        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
        {
            margin.AddThemeConstantOverride(side, 24);
        }
        panel.AddChild(margin);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(720f, 0f) };
        margin.AddChild(box);

        box.AddChild(new Label { Text = "spire_chat — IME input probe" });
        box.AddChild(new Label
        {
            Text = "Switch to the Korean IME and type. Watch whether composing letters appear.\n"
                 + "ESC closes. Console: `imeprobe font` toggles the Korean font override."
        });

        _input = new LineEdit
        {
            PlaceholderText = "type here / 여기에 입력",
            CustomMinimumSize = new Vector2(0f, 44f)
        };
        _input.TextChanged += OnTextChanged;
        _input.GuiInput += OnInputGuiEvent;
        box.AddChild(_input);

        _dump = new Label { Text = "(empty)", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_dump);

        _status = new Label();
        box.AddChild(_status);

        tree.Root.AddChild(_root);
        ApplyFont();

        // 포커스를 즉시 주면 게임 UI가 아직 포커스를 쥐고 있어 밀릴 수 있으므로
        // 프레임 종료 후에 잡는다.
        _input.CallDeferred(Control.MethodName.GrabFocus);

        Log.Info("[spire_chat] ImeProbe opened.");
    }

    private static void Close()
    {
        if (_root != null && GodotObject.IsInstanceValid(_root))
        {
            _root.QueueFree();
        }

        _root = null;
        _input = null;
        _dump = null;
        _status = null;
        Log.Info("[spire_chat] ImeProbe closed.");
    }

    private static void OnInputGuiEvent(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
        }
    }

    private static void OnTextChanged(string text)
    {
        if (_dump == null)
        {
            return;
        }

        _dump.Text = Describe(text);

        // 화면을 못 보는 상황에서도 결과를 회수할 수 있도록 로그에도 남긴다.
        Log.Info($"[spire_chat] ImeProbe text: {Describe(text)}");
    }

    /// <summary>
    /// 입력 문자열을 코드포인트 단위로 풀어 쓴다.
    /// 완성형 한글(U+AC00~U+D7A3)과 낱자모(U+1100~U+11FF, U+3130~U+318F)를 구분해 표시하는데,
    /// 낱자모가 찍힌다면 IME가 조합을 끝내지 않고 자모를 그대로 흘려보내고 있다는 뜻이라
    /// 대응 방법이 완전히 달라진다.
    /// </summary>
    private static string Describe(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "(empty)";
        }

        var sb = new StringBuilder();
        int syllables = 0;
        int jamo = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            int cp = rune.Value;
            sb.Append($"U+{cp:X4} ");

            if (cp is >= 0xAC00 and <= 0xD7A3)
            {
                syllables++;
            }
            else if (cp is (>= 0x1100 and <= 0x11FF) or (>= 0x3130 and <= 0x318F))
            {
                jamo++;
            }
        }

        var verdict = (syllables, jamo) switch
        {
            (> 0, 0) => "OK: composed Hangul syllables",
            (> 0, > 0) => "MIXED: syllables + loose jamo",
            (0, > 0) => "JAMO ONLY: IME is not composing",
            _ => "no Hangul"
        };

        return $"len={text.Length}  [{verdict}]\n{sb.ToString().TrimEnd()}";
    }

    /// <summary>
    /// 한글 폰트 적용을 토글한다. 폰트 획득 로직은 <see cref="LocaleFont"/>가 단일 출처다.
    /// </summary>
    private static void ApplyFont()
    {
        if (_input == null || _dump == null || _status == null)
        {
            return;
        }

        if (!_fontApplied)
        {
            LocaleFont.Remove(_input, _dump);
            _status.Text = "font: game default (Hangul may render as tofu)";
            return;
        }

        _status.Text = LocaleFont.Apply(_input, _dump)
            ? "font: bundled Korean font applied"
            : "font: FAILED to load the bundled Korean font";
    }
}
