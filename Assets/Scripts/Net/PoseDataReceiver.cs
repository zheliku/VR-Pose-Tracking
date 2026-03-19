/*
 * 位姿追踪数据接收器
 *
 * 负责网络接收与事件分发，具体 payload 协议解析由 ITrackingPayloadParser 实现。
 * 当前默认解析器：TrackingPayloadParser（phase + color + pose_json）。
 */

using System;
using System.Diagnostics;
using System.Threading;
using NetMQ;
using NetMQ.Sockets;
using UnityEngine;
using UnityEngine.Events;
using Debug = UnityEngine.Debug;

/// <summary>
/// 追踪阶段枚举
/// </summary>
public enum TrackingPhase
{
    Detecting = 0,
    Tracking = 1
}

/// <summary>
/// 位姿追踪数据接收器
/// </summary>
public class PoseDataReceiver : MonoBehaviour
{
    private const string ServerIPPrefKey = "PoseDataReceiver.ServerIP";

    [Header("Network Settings")]
    [SerializeField] private string serverIP = "127.0.0.1";
    [SerializeField] private int serverPort = 5556;
    [SerializeField] private string topic = "tracking";

    [Header("Events")]
    [Tooltip("当收到图像数据时触发")]
    public RawDataEvent OnImageReceived = new RawDataEvent();

    [Tooltip("当收到位姿数据时触发")]
    public PoseDataEvent OnPoseReceived = new PoseDataEvent();

    [Tooltip("当从检测切换到追踪时触发")]
    public UnityEvent OnTrackingStarted = new UnityEvent();

    [Tooltip("当追踪丢失时触发")]
    public UnityEvent OnTrackingLost = new UnityEvent();

    private SubscriberSocket _socket;
    private Thread _receiveThread;
    private volatile bool _running;

    private readonly object _lock = new object();
    private readonly ITrackingPayloadParser _payloadParser = new TrackingPayloadParser();
    private readonly Stopwatch _stopwatch = new Stopwatch();

    private RawData _latestImageData;
    private PoseData _latestPoseData;
    private TrackingPhase _latestPhase;
    private bool _hasNewData;
    private TrackingPhase _lastPhase = TrackingPhase.Detecting;

    public bool IsConnected => _running && _socket != null;
    public string ServerAddress => $"tcp://{serverIP}:{serverPort}";
    public TrackingPhase CurrentPhase => _lastPhase;
    public string CurrentServerIP => serverIP;

    public event Action<string> ServerIPChanged;

    private void Awake()
    {
        if (OnImageReceived == null)
            OnImageReceived = new RawDataEvent();
        if (OnPoseReceived == null)
            OnPoseReceived = new PoseDataEvent();
        if (OnTrackingStarted == null)
            OnTrackingStarted = new UnityEvent();
        if (OnTrackingLost == null)
            OnTrackingLost = new UnityEvent();

        LoadServerIPFromPrefs();
        NotifyServerIPChanged();
    }

    private void Start()
    {
        if (!_stopwatch.IsRunning)
        {
            _stopwatch.Start();
        }

        Connect();
    }

    public bool SaveServerIPPreference(string newServerIP)
    {
        string normalizedIP = NormalizeServerIP(newServerIP);
        if (!IsValidServerAddress(normalizedIP))
        {
            Debug.LogWarning($"[PoseDataReceiver] Invalid server IP/host: '{newServerIP}'");
            return false;
        }

        PlayerPrefs.SetString(ServerIPPrefKey, normalizedIP);
        PlayerPrefs.Save();
        return true;
    }

    public bool TrySetServerIP(string newServerIP)
    {
        string normalizedIP = NormalizeServerIP(newServerIP);
        if (!IsValidServerAddress(normalizedIP))
        {
            Debug.LogWarning($"[PoseDataReceiver] Invalid server IP/host: '{newServerIP}'");
            return false;
        }

        if (string.Equals(serverIP, normalizedIP, StringComparison.OrdinalIgnoreCase))
        {
            NotifyServerIPChanged();
            return true;
        }

        bool wasRunning = _running;
        if (wasRunning)
        {
            Disconnect();
        }

        serverIP = normalizedIP;
        SaveServerIPToPrefs();

        if (wasRunning)
        {
            Connect();
        }

        NotifyServerIPChanged();
        Debug.Log($"[PoseDataReceiver] Server switched to {ServerAddress}");
        return true;
    }

    public string GetSavedServerIP()
    {
        return NormalizeServerIP(PlayerPrefs.GetString(ServerIPPrefKey, string.Empty));
    }

    public void ApplySavedServerIP()
    {
        string savedServerIP = GetSavedServerIP();
        if (!string.IsNullOrEmpty(savedServerIP))
        {
            TrySetServerIP(savedServerIP);
        }
    }

    public void Connect()
    {
        if (_running)
        {
            return;
        }

        AsyncIO.ForceDotNet.Force();

        _socket = new SubscriberSocket();
        _socket.Options.ReceiveHighWatermark = 1;
        _socket.Options.Linger = TimeSpan.Zero;
        _socket.Connect(ServerAddress);
        _socket.Subscribe(topic);

        _running = true;
        _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        _receiveThread.Start();

        Debug.Log($"[PoseDataReceiver] Connected to {ServerAddress}, topic: {topic}");
    }

    public void Disconnect()
    {
        _running = false;

        if (_receiveThread != null && _receiveThread.IsAlive)
        {
            _receiveThread.Join(1000);
        }
        _receiveThread = null;

        DisconnectSocket();
        Debug.Log("[PoseDataReceiver] Disconnected");
    }

    private void Update()
    {
        lock (_lock)
        {
            if (!_hasNewData)
            {
                return;
            }

            if (_lastPhase == TrackingPhase.Detecting && _latestPhase == TrackingPhase.Tracking)
            {
                OnTrackingStarted?.Invoke();
            }
            else if (_lastPhase == TrackingPhase.Tracking && _latestPhase == TrackingPhase.Detecting)
            {
                OnTrackingLost?.Invoke();
            }
            _lastPhase = _latestPhase;

            OnImageReceived?.Invoke(_latestImageData);
            OnPoseReceived?.Invoke(_latestPoseData);
            _hasNewData = false;
        }
    }

    private void ReceiveLoop()
    {
        while (_running)
        {
            try
            {
                if (_socket == null)
                {
                    Thread.Sleep(10);
                    continue;
                }

                NetMQMessage message = new NetMQMessage();
                if (!_socket.TryReceiveMultipartMessage(TimeSpan.FromMilliseconds(100), ref message))
                {
                    continue;
                }

                if (message.FrameCount < 2)
                {
                    continue;
                }

                byte[][] payloadParts = new byte[message.FrameCount - 1][];
                for (int i = 1; i < message.FrameCount; i++)
                {
                    payloadParts[i - 1] = message[i].ToByteArray();
                }

                double timestampMs = _stopwatch.Elapsed.TotalMilliseconds;
                if (!_payloadParser.TryParse(payloadParts, timestampMs, out TrackingPayload parsedPayload))
                {
                    continue;
                }

                lock (_lock)
                {
                    _latestImageData = parsedPayload.ImageData;
                    _latestPoseData = parsedPayload.PoseData;
                    _latestPhase = parsedPayload.Phase;
                    _hasNewData = true;
                }
            }
            catch (Exception e)
            {
                if (_running)
                {
                    Debug.LogError($"[PoseDataReceiver] Error: {e.Message}");
                }
            }
        }
    }

    private void DisconnectSocket()
    {
        if (_socket == null)
        {
            return;
        }

        _socket.Close();
        _socket.Dispose();
        _socket = null;
    }

    private void LoadServerIPFromPrefs()
    {
        string savedIP = NormalizeServerIP(PlayerPrefs.GetString(ServerIPPrefKey, string.Empty));
        if (IsValidServerAddress(savedIP))
        {
            serverIP = savedIP;
        }
    }

    private void SaveServerIPToPrefs()
    {
        PlayerPrefs.SetString(ServerIPPrefKey, serverIP);
        PlayerPrefs.Save();
    }

    private void NotifyServerIPChanged()
    {
        ServerIPChanged?.Invoke(serverIP);
    }

    private static bool IsValidServerAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Uri.CheckHostName(value) != UriHostNameType.Unknown;
    }

    private static string NormalizeServerIP(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private void Cleanup()
    {
        Disconnect();
    }

    private void OnDestroy()
    {
        Cleanup();
        NetMQConfig.Cleanup(false);
    }

    private void OnApplicationQuit()
    {
        Cleanup();
    }
}
