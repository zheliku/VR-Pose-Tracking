using System;
using System.Diagnostics;
using System.Threading;
using NetMQ;
using NetMQ.Sockets;
using UnityEngine;
using UnityEngine.Events;
using Debug = UnityEngine.Debug;

/// <summary>
/// 传输层原始数据包。
///
/// 设计说明：
/// 1) Receiver 只做网络收包，不在这里做业务解码。
/// 2) Parts 由 Decoder 解释其协议语义（RGBD/Tracking/Stereo 等）。
/// 3) TimestampMs 使用本地 Stopwatch 时间，便于统计帧间隔与端内延迟。
/// </summary>
public readonly struct RawPayload
{
    public byte[][] Parts { get; }
    public string Topic { get; }
    public double TimestampMs { get; }

    public RawPayload(byte[][] parts, string topic, double timestampMs)
    {
        Parts = parts;
        Topic = topic;
        TimestampMs = timestampMs;
    }
}

[Serializable]
public class StringEvent : UnityEvent<string> { }

[Serializable]
public class RawPayloadEvent : UnityEvent<RawPayload> { }

/// <summary>
/// Unity 侧通用 Payload 接收器。
///
/// 职责边界：
/// - 负责 ZMQ 订阅连接、收包、线程间传递、主线程事件派发。
/// - 不负责业务协议解析（解析交给 Decoder）。
///
/// 线程模型：
/// - 后台线程 ReceiveLoop 持续收包并写入 _latestPayload。
/// - 主线程 Update 读取最新包并触发 OnPayloadReceived。
/// - 采用“只保留最新一包”的策略，优先实时性而非完整性。
/// </summary>
public class PayloadReceiver : MonoBehaviour
{
    [Header("Network Settings")]
    [SerializeField] private string serverIP = "127.0.0.1";
    [SerializeField] private int serverPort = 5556;
    [SerializeField] private bool useTopic = true;
    [SerializeField] private string topic = "payload";
    [SerializeField] private int receiveHighWatermark = 1;
    [SerializeField] private int socketLingerMs = 0;
    [SerializeField] private int receivePollTimeoutMs = 100;

    private SubscriberSocket _socket;
    private Thread _receiveThread;
    private volatile bool _running;
    private readonly Stopwatch _stopwatch = new Stopwatch();

    private readonly object _lock = new object();
    private RawPayload _latestPayload;
    private bool _hasNewPayload;

    public bool IsConnected => _running && _socket != null;
    public string ServerAddress => $"tcp://{serverIP}:{serverPort}";
    public string CurrentServerIP => serverIP;
    public int CurrentServerPort => serverPort;
    public bool UseTopic => useTopic;
    public string Topic => topic;
    public int CurrentReceiveHighWatermark => receiveHighWatermark;
    public int CurrentSocketLingerMs => socketLingerMs;
    public int CurrentReceivePollTimeoutMs => receivePollTimeoutMs;

    [Header("Events")]
    public StringEvent OnServerIPChanged = new StringEvent();
    public RawPayloadEvent OnPayloadReceived = new RawPayloadEvent();

    /// <summary>
    /// 初始化可序列化事件与 topic 默认值。
    /// </summary>
    protected virtual void Awake()
    {
        if (OnServerIPChanged == null)
            OnServerIPChanged = new StringEvent();
        if (OnPayloadReceived == null)
            OnPayloadReceived = new RawPayloadEvent();

        if (useTopic && string.IsNullOrWhiteSpace(topic))
        {
            topic = "payload";
        }

        NotifyServerIPChanged();
    }

    /// <summary>
    /// 启动本地计时器并建立网络连接。
    /// </summary>
    protected virtual void Start()
    {
        if (!_stopwatch.IsRunning)
        {
            _stopwatch.Start();
        }

        Connect();
    }

    /// <summary>
    /// 在主线程派发最新 payload。
    /// 注意：UnityEvent 监听通常涉及 Unity API，因此必须在主线程调用。
    /// </summary>
    private void Update()
    {
        RawPayload payload;

        lock (_lock)
        {
            if (!_hasNewPayload)
            {
                return;
            }

            payload = _latestPayload;
            _hasNewPayload = false;
        }

        try
        {
            OnPayloadReceived?.Invoke(payload);
        }
        catch (Exception e)
        {
            Debug.LogError($"[PayloadReceiver] PayloadReceived callback error: {e.Message}");
        }
    }

    /// <summary>
    /// 一次性设置接收端连接配置（单次重连）。
    /// </summary>
    public bool TryApplyConnectionConfig(string newServerIP, int newServerPort, bool newUseTopic, string newTopic)
    {
        string normalizedIP = NormalizeServerIP(newServerIP);
        if (!IsValidServerAddress(normalizedIP))
        {
            Debug.LogWarning($"[PayloadReceiver] Invalid server IP/host: '{newServerIP}'");
            return false;
        }

        if (!IsValidPort(newServerPort))
        {
            Debug.LogWarning($"[PayloadReceiver] Invalid server port: '{newServerPort}'");
            return false;
        }

        string normalizedTopic = NormalizeTopic(newTopic);
        if (newUseTopic && string.IsNullOrEmpty(normalizedTopic))
        {
            Debug.LogWarning("[PayloadReceiver] Topic cannot be empty when useTopic is enabled.");
            return false;
        }

        bool unchanged =
            string.Equals(serverIP, normalizedIP, StringComparison.OrdinalIgnoreCase) &&
            serverPort == newServerPort &&
            useTopic == newUseTopic &&
            string.Equals(topic, normalizedTopic, StringComparison.Ordinal);
        if (unchanged)
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
        serverPort = newServerPort;
        useTopic = newUseTopic;
        topic = normalizedTopic;

        if (wasRunning)
        {
            Connect();
        }

        NotifyServerIPChanged();
        Debug.Log($"[PayloadReceiver] Server switched to {ServerAddress}");
        return true;
    }

    /// <summary>
    /// 一次性设置接收策略参数。
    /// </summary>
    public bool TryApplyReceiveSettings(int newReceiveHighWatermark, int newSocketLingerMs, int newReceivePollTimeoutMs)
    {
        if (!IsValidHighWatermark(newReceiveHighWatermark))
        {
            Debug.LogWarning($"[PayloadReceiver] Invalid receiveHighWatermark: '{newReceiveHighWatermark}'");
            return false;
        }

        if (!IsValidLingerMs(newSocketLingerMs))
        {
            Debug.LogWarning($"[PayloadReceiver] Invalid socketLingerMs: '{newSocketLingerMs}'");
            return false;
        }

        if (!IsValidPollTimeoutMs(newReceivePollTimeoutMs))
        {
            Debug.LogWarning($"[PayloadReceiver] Invalid receivePollTimeoutMs: '{newReceivePollTimeoutMs}'");
            return false;
        }

        bool socketOptionChanged =
            receiveHighWatermark != newReceiveHighWatermark ||
            socketLingerMs != newSocketLingerMs;

        bool unchanged =
            receivePollTimeoutMs == newReceivePollTimeoutMs &&
            !socketOptionChanged;
        if (unchanged)
        {
            return true;
        }

        receiveHighWatermark = newReceiveHighWatermark;
        socketLingerMs = newSocketLingerMs;
        receivePollTimeoutMs = newReceivePollTimeoutMs;

        if (_socket != null && socketOptionChanged)
        {
            bool wasRunning = _running;
            if (wasRunning)
            {
                Disconnect();
                Connect();
            }
            else
            {
                DisconnectSocket();
            }
        }

        return true;
    }

    /// <summary>
    /// 建立订阅连接并启动接收线程。
    /// useTopic=false 时，订阅空字符串，相当于接收所有消息。
    /// </summary>
    public void Connect()
    {
        if (_running)
        {
            return;
        }

        AsyncIO.ForceDotNet.Force();

        _socket = new SubscriberSocket();
        _socket.Options.ReceiveHighWatermark = receiveHighWatermark;
        _socket.Options.Linger = TimeSpan.FromMilliseconds(socketLingerMs);
        _socket.Connect(ServerAddress);
        _socket.Subscribe(useTopic ? topic : string.Empty);

        _running = true;
        _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        _receiveThread.Start();

        string subscribeDesc = useTopic ? $"topic: {topic}" : "topic: <none>";
        Debug.Log($"[PayloadReceiver] Connected to {ServerAddress}, {subscribeDesc}");
    }

    /// <summary>
    /// 断开连接并安全回收接收线程与 socket 资源。
    /// </summary>
    public void Disconnect()
    {
        _running = false;

        if (_receiveThread != null && _receiveThread.IsAlive)
        {
            _receiveThread.Join(1000);
        }
        _receiveThread = null;

        DisconnectSocket();
        Debug.Log("[PayloadReceiver] Disconnected");
    }

    /// <summary>
    /// 后台线程收包循环。
    /// 协议约定：
    /// - useTopic=true  : 第 0 帧为 topic，后续帧为 payload。
    /// - useTopic=false : 所有帧均为 payload。
    /// </summary>
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
                if (!_socket.TryReceiveMultipartMessage(TimeSpan.FromMilliseconds(receivePollTimeoutMs), ref message))
                {
                    continue;
                }

                if (message.FrameCount < 2)
                {
                    continue;
                }

                int startFrame = useTopic ? 1 : 0;
                if (message.FrameCount <= startFrame)
                {
                    continue;
                }

                string receivedTopic = useTopic ? message[0].ConvertToString() : string.Empty;

                int payloadCount = message.FrameCount - startFrame;
                byte[][] payloadParts = new byte[payloadCount][];
                for (int i = 0; i < payloadCount; i++)
                {
                    payloadParts[i] = message[startFrame + i].ToByteArray();
                }

                double timestampMs = _stopwatch.Elapsed.TotalMilliseconds;
                lock (_lock)
                {
                    _latestPayload = new RawPayload(payloadParts, receivedTopic, timestampMs);
                    _hasNewPayload = true;
                }
            }
            catch (Exception e)
            {
                if (_running)
                {
                    Debug.LogError($"[PayloadReceiver] Error: {e.Message}");
                }
            }
        }
    }

    /// <summary>
    /// 关闭并释放底层 socket。
    /// </summary>
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

    /// <summary>
    /// 广播当前 IP，供 UI 或调试面板同步显示。
    /// </summary>
    private void NotifyServerIPChanged()
    {
        OnServerIPChanged?.Invoke(serverIP);
    }

    /// <summary>
    /// 校验 host/IP 格式是否可用于 URI 主机部分。
    /// </summary>
    private static bool IsValidServerAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Uri.CheckHostName(value) != UriHostNameType.Unknown;
    }

    /// <summary>
    /// 去除前后空白；空输入统一为 string.Empty。
    /// </summary>
    private static string NormalizeServerIP(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    /// <summary>
    /// 归一化 topic 输入。
    /// </summary>
    private static string NormalizeTopic(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    /// <summary>
    /// 校验端口范围。
    /// </summary>
    private static bool IsValidPort(int value)
    {
        return value is >= 1 and <= 65535;
    }

    private static bool IsValidHighWatermark(int value)
    {
        return value >= 1;
    }

    private static bool IsValidLingerMs(int value)
    {
        return value >= 0;
    }

    private static bool IsValidPollTimeoutMs(int value)
    {
        return value >= 1;
    }

    /// <summary>
    /// 生命周期结束时确保网络线程与资源被回收。
    /// </summary>
    protected virtual void OnDestroy()
    {
        Disconnect();
        NetMQConfig.Cleanup(false);
    }

    /// <summary>
    /// 应用退出前主动断开，减少 editor 下的残留连接风险。
    /// </summary>
    protected virtual void OnApplicationQuit()
    {
        Disconnect();
    }
}
