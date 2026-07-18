using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using SpireChat.Ui;

namespace SpireChat.Chat;

/// <summary>
/// 최소 채팅 UI. 입력창 하나와 메시지 목록 하나가 전부다.
///
/// **전송 방식을 모른다** — <see cref="IChatTransport"/>만 통해 주고받는다.
/// 현재는 <see cref="LoopbackChatTransport"/>를 물려 멀티플레이 세션 없이 검증한다.
///
/// ImeProbe와 마찬가지로 커스텀 Node 서브클래스를 만들지 않고 순수 Godot 노드 조합 +
/// C# 시그널로만 구성한다 — 모드 어셈블리의 스크립트 클래스를 씬 트리에 등록하는 문제를
/// 회피할 수 있다.
/// </summary>
public static class ChatOverlay
{
    /// <summary>표시할 최대 줄 수. 넘으면 오래된 것부터 버린다.</summary>
    private const int MaxLines = 12;

    private static CanvasLayer? _root;
    private static LineEdit? _input;
    private static Label? _log;
    private static IChatTransport? _transport;

    private static readonly List<string> _lines = new();

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
        return IsOpen
            ? "chat: OPEN (loopback) — type and press Enter. ESC closes."
            : "chat: FAILED to open (scene tree unavailable) — check the log.";
    }

    private static void Open()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            Log.Error($"[{ModEntry.ModId}] ChatOverlay: could not reach the scene tree.");
            return;
        }

        // 전송 계층은 아직 루프백이다. 실제 송수신이 준비되면 여기만 교체한다.
        _transport = new LoopbackChatTransport();
        _transport.Received += OnReceived;
        _transport.Start();

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

        box.AddChild(new Label { Text = "spire_chat (loopback test)" });

        _log = new Label
        {
            Text = "(no messages)",
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

        // 게임 UI가 아직 포커스를 쥐고 있을 수 있으므로 프레임 종료 후에 잡는다.
        _input.CallDeferred(Control.MethodName.GrabFocus);

        Log.Info($"[{ModEntry.ModId}] ChatOverlay opened (loopback).");
    }

    private static void Close()
    {
        if (_transport != null)
        {
            _transport.Received -= OnReceived;
            _transport.Stop();
            _transport = null;
        }

        if (_root != null && GodotObject.IsInstanceValid(_root))
        {
            _root.QueueFree();
        }

        _root = null;
        _input = null;
        _log = null;
        _lines.Clear();

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
        if (_input == null || _transport == null)
        {
            return;
        }

        text = text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        _transport.Send(text);
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
    /// 전송 계층에서 메시지가 도착했을 때. 자기가 보낸 것도 여기로 돌아오므로
    /// 화면 갱신 경로가 하나로 통일된다 — 실제 전송으로 바꿔도 이 코드는 그대로다.
    /// </summary>
    private static void OnReceived(ulong senderId, string text)
    {
        if (_log == null)
        {
            return;
        }

        string who = senderId == LoopbackChatTransport.LocalPeerId ? "나" : senderId.ToString();
        _lines.Add($"{who}: {text}");

        if (_lines.Count > MaxLines)
        {
            _lines.RemoveRange(0, _lines.Count - MaxLines);
        }

        _log.Text = string.Join("\n", _lines);
        Log.Info($"[{ModEntry.ModId}] chat message from {senderId}: {text}");
    }
}
