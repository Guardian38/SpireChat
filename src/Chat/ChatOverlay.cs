using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using SpireChat.Ui;

namespace SpireChat.Chat;

/// <summary>
/// 최소 채팅 UI. 입력창 하나와 메시지 목록 하나가 전부다.
///
/// **전송 방식도, 세션 상태도 모른다** — <see cref="ChatService"/>에 보내달라고 하고,
/// 쌓인 것을 읽어 그릴 뿐이다. 수신과 보관은 오버레이가 닫혀 있는 동안에도 계속돼야 하므로
/// 창이 아니라 서비스가 맡는다.
///
/// ImeProbe와 마찬가지로 커스텀 Node 서브클래스를 만들지 않고 순수 Godot 노드 조합 +
/// C# 시그널로만 구성한다 — 모드 어셈블리의 스크립트 클래스를 씬 트리에 등록하는 문제를
/// 회피할 수 있다.
/// </summary>
public static class ChatOverlay
{
    /// <summary>화면에 보여줄 최대 줄 수. 보관 자체는 <see cref="ChatService"/>가 더 길게 한다.</summary>
    private const int MaxLines = 12;

    private static CanvasLayer? _root;
    private static LineEdit? _input;
    private static Label? _log;

    public static bool IsOpen => _root != null && GodotObject.IsInstanceValid(_root);

    /// <summary>오버레이를 토글한다. 반환값은 콘솔에 출력할 상태 설명.</summary>
    public static string Toggle()
    {
        if (IsOpen)
        {
            Close();
            return "chat: CLOSED";
        }

        Open();
        if (!IsOpen)
        {
            return "chat: FAILED to open (scene tree unavailable) — check the log.";
        }

        return ChatService.CanSend
            ? "chat: OPEN — type and press Enter. ESC closes."
            : "chat: OPEN (no multiplayer session — messages cannot be sent yet).";
    }

    private static void Open()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            Log.Error($"[{ModEntry.ModId}] ChatOverlay: could not reach the scene tree.");
            return;
        }

        // 노드가 외부에서 파괴돼 Close()를 못 거친 경우 구독이 남아 있을 수 있다.
        // 먼저 떼고 붙여 중복 구독을 막는다(없는 것을 빼도 무해하다).
        ChatService.LineAdded -= OnLineAdded;
        ChatService.LineAdded += OnLineAdded;

        _root = new CanvasLayer { Layer = 100, Name = "SpireChatOverlay" };

        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(center);

        var panel = new PanelContainer();
        center.AddChild(panel);

        var margin = new MarginContainer();
        foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
        {
            margin.AddThemeConstantOverride(side, 20);
        }
        panel.AddChild(margin);

        var box = new VBoxContainer { CustomMinimumSize = new Vector2(760f, 0f) };
        margin.AddChild(box);

        box.AddChild(new Label { Text = "spire_chat" });

        _log = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0f, 260f),
            VerticalAlignment = VerticalAlignment.Top
        };
        box.AddChild(_log);

        _input = new LineEdit
        {
            PlaceholderText = "메시지 입력 후 Enter",
            CustomMinimumSize = new Vector2(0f, 44f)
        };
        _input.TextSubmitted += OnSubmitted;
        _input.GuiInput += OnInputGuiEvent;
        box.AddChild(_input);

        tree.Root.AddChild(_root);
        KoreanFont.Apply(_input, _log);

        // 창이 닫혀 있던 동안에도 메시지는 쌓인다. 열자마자 그것부터 그린다.
        Redraw();

        // 게임 UI가 아직 포커스를 쥐고 있을 수 있으므로 프레임 종료 후에 잡는다.
        _input.CallDeferred(Control.MethodName.GrabFocus);

        Log.Info($"[{ModEntry.ModId}] ChatOverlay opened (canSend={ChatService.CanSend}).");
    }

    private static void Close()
    {
        ChatService.LineAdded -= OnLineAdded;

        if (_root != null && GodotObject.IsInstanceValid(_root))
        {
            _root.QueueFree();
        }

        _root = null;
        _input = null;
        _log = null;

        Log.Info($"[{ModEntry.ModId}] ChatOverlay closed.");
    }

    private static void OnInputGuiEvent(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
        }
    }

    /// <summary>Enter로 확정했을 때. 보내기만 하고, 화면 반영은 수신 이벤트가 담당한다.</summary>
    private static void OnSubmitted(string text)
    {
        if (_input == null)
        {
            return;
        }

        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        ChatService.Send(text);
        _input.Clear();

        // Enter로 확정하면 LineEdit이 편집 모드를 푼다. GrabFocus만으로는 포커스만 돌아오고
        // IsEditing()이 false로 남는데, 그러면 두 가지가 곤란하다:
        //   1. 연속으로 메시지를 치려면 매번 입력창을 다시 클릭해야 한다.
        //   2. NHotkeyManager가 IsEditing()으로 단축키 차단을 판단하므로(NHotkeyManager.cs:177)
        //      T가 다시 게임 단축키로 먹힌다 — 채팅 중 T를 글자로 입력할 수 없게 된다.
        // 시그널 처리 도중의 상태 변경은 되돌려질 수 있어 프레임 종료 후에 편집을 재개한다.
        _input.CallDeferred(LineEdit.MethodName.Edit);
    }

    /// <summary>
    /// 새 줄이 쌓였을 때. 자기가 보낸 것도 서비스를 거쳐 여기로 돌아오므로
    /// 화면 갱신 경로가 하나로 통일된다.
    /// </summary>
    private static void OnLineAdded(ChatService.ChatLine line)
    {
        // 씬 전환 등으로 노드가 이미 죽었을 수 있다. 그때는 여기서 구독을 끊는다 —
        // Close()를 거치지 않고 사라진 경우라 아무도 정리해주지 않기 때문이다.
        if (_root != null && !GodotObject.IsInstanceValid(_root))
        {
            Log.Info($"[{ModEntry.ModId}] ChatOverlay node was freed externally — detaching.");
            ChatService.LineAdded -= OnLineAdded;
            _root = null;
            _input = null;
            _log = null;
            return;
        }

        Redraw();
    }

    /// <summary>보관된 것 중 마지막 <see cref="MaxLines"/>줄을 그린다.</summary>
    private static void Redraw()
    {
        if (_log == null || !GodotObject.IsInstanceValid(_log))
        {
            return;
        }

        var history = ChatService.History;
        if (history.Count == 0)
        {
            _log.Text = "(메시지 없음)";
            return;
        }

        var lines = new List<string>(MaxLines);
        for (int i = Math.Max(0, history.Count - MaxLines); i < history.Count; i++)
        {
            lines.Add($"{history[i].Sender}: {history[i].Text}");
        }

        _log.Text = string.Join("\n", lines);
    }
}
