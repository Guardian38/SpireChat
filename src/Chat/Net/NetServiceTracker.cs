using System;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace SpireChat.Chat.Net;

/// <summary>
/// 지금 살아 있는 <see cref="INetGameService"/>를 추적한다.
///
/// **왜 별도 추적이 필요한가:** 서비스 인스턴스에 이르는 공개 경로가 상황마다 다르다.
/// 런 중에는 <c>RunManager.Instance.NetService</c>지만, 로비 단계에서는 아직 거기 없고
/// <c>StartRunLobby</c>/<c>LoadRunLobby</c>가 들고 있다. 채팅은 로비에서부터 필요하므로
/// 어느 한쪽만으로는 부족하다.
///
/// **훅 지점 선정:** <c>PeerInputSynchronizer</c>의 생성자를 잡는다. 생성자가 하나뿐이고
/// (PeerInputSynchronizer.cs:109) 세션을 만드는 **모든 경로가 반드시 이곳을 지난다** —
/// <c>StartRunLobby</c>(:117), <c>LoadRunLobby</c>(:86), 그리고 로비를 거치지 않는
/// 싱글플레이·리플레이 경로까지. 로비에서 만든 인스턴스가 런으로 그대로 넘어가므로
/// (<c>RunManager.InitializeShared</c>:440) 런 시작 시점에 재등록할 필요도 없다.
///
/// 개발용 <c>multiplayer test</c> 씬도 정규 로비를 거치므로(NMultiplayerTest.cs:418, :446)
/// 이 훅 하나로 실사용과 테스트가 모두 커버된다 — Host/Join 직후 바로 채팅이 붙는다.
/// </summary>
public static class NetServiceTracker
{
    private static INetGameService? _current;

    /// <summary>현재 활성 서비스. 멀티 세션 밖(메인 메뉴 등)에서는 null이다.</summary>
    public static INetGameService? Current => _current;

    /// <summary>활성 서비스가 바뀌었을 때. 인자가 null이면 세션이 끊긴 것이다.</summary>
    public static event Action<INetGameService?>? Changed;

    /// <summary>새 세션이 열렸다. Harmony 패치가 호출한다.</summary>
    internal static void Adopt(INetGameService netService)
    {
        if (ReferenceEquals(_current, netService))
        {
            return;
        }

        Release();

        _current = netService;
        netService.Disconnected += OnDisconnected;

        Log.Info($"[{ModEntry.ModId}] net service adopted: type={netService.Type}, netId={netService.NetId}");
        Raise();
    }

    private static void OnDisconnected(NetErrorInfo info)
    {
        Log.Info($"[{ModEntry.ModId}] net service disconnected ({info.GetReason()}).");
        Release();
        Raise();
    }

    private static void Release()
    {
        if (_current == null)
        {
            return;
        }

        _current.Disconnected -= OnDisconnected;
        _current = null;
    }

    /// <summary>구독자 예외가 추적 자체를 망가뜨리지 않게 격리한다.</summary>
    private static void Raise()
    {
        try
        {
            Changed?.Invoke(_current);
        }
        catch (Exception e)
        {
            Log.Error($"[{ModEntry.ModId}] NetServiceTracker.Changed subscriber threw: {e}");
        }
    }
}
