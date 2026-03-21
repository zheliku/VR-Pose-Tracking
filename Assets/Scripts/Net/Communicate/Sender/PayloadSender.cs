using System;
using System.Collections;
using NetMQ;
using NetMQ.Sockets;
using UnityEngine;

/// <summary>
/// Unity 侧通用 Payload 发送器。
///
/// 设计目标：
/// - 将业务编码（Encoder）与网络发送（Sender）解耦。
/// - 以固定帧率循环发送，网络拥塞时允许丢帧，优先实时性。
/// - 支持服务器地址持久化与运行中切换。
/// </summary>
public class PayloadSender : MonoBehaviour
{
    [Header("Data Encoder")]
    [SerializeField] private EncoderBase payloadEncoder;

    [Header("Network")]
    [SerializeField] private string serverIP = "127.0.0.1";
    [SerializeField] private int serverPort = 5557;

    [Header("Send Settings")]
    [Range(1, 90)]
    [SerializeField] private int targetFps = 60;
    [Range(1, 90)]
    [SerializeField] private int logInterval = 30;
    [Range(1, 100)]
    [SerializeField] private int sendHighWatermark = 1;
    [SerializeField] private int socketLingerMs = 0;

    private PushSocket _socket;
    private Coroutine _sendCoroutine;

    private int _sentFrameCount;
    private int _droppedFrameCount;
    private int _lastStatTotal;
    private int _lastStatSent;
    private double _lastStatTime;
    private double _encodeTimeAcc;
    private double _sendTimeAcc;

    private string Endpoint => $"tcp://{serverIP}:{serverPort}";
    public string CurrentServerIP => serverIP;
    public int CurrentServerPort => serverPort;
    public int CurrentTargetFps => targetFps;
    public int CurrentLogInterval => logInterval;
    public int CurrentSendHighWatermark => sendHighWatermark;
    public int CurrentSocketLingerMs => socketLingerMs;

    [Header("Events")]
    public StringEvent OnServerIPChanged = new StringEvent();

    /// <summary>
    /// 初始化事件并自动查找同对象编码器。
    /// </summary>
    private void Awake()
    {
        if (OnServerIPChanged == null)
        {
            OnServerIPChanged = new StringEvent();
        }

        if (payloadEncoder == null)
        {
            payloadEncoder = GetComponent<EncoderBase>();
        }
        NotifyServerIPChanged();
    }

    /// <summary>
    /// 启动发送循环。
    /// 若未配置编码器则中止，并打印错误。
    /// </summary>
    private IEnumerator Start()
    {
        if (payloadEncoder == null)
        {
            Debug.LogError("[PayloadSender] Payload encoder is not assigned.");
            yield break;
        }

        Connect();
        _lastStatTime = Time.realtimeSinceStartupAsDouble;
        _sendCoroutine = StartCoroutine(SendLoop());
    }

    /// <summary>
    /// 一次性设置发送端连接配置（单次重连）。
    /// </summary>
    public bool TryApplyServerConfig(string newServerIP, int newServerPort)
    {
        string normalizedIP = NormalizeServerIP(newServerIP);
        if (!IsValidServerAddress(normalizedIP))
        {
            Debug.LogWarning($"[PayloadSender] Invalid server IP/host: '{newServerIP}'");
            return false;
        }

        if (!IsValidPort(newServerPort))
        {
            Debug.LogWarning($"[PayloadSender] Invalid server port: '{newServerPort}'");
            return false;
        }

        bool unchanged =
            string.Equals(serverIP, normalizedIP, StringComparison.OrdinalIgnoreCase) &&
            serverPort == newServerPort;
        if (unchanged)
        {
            NotifyServerIPChanged();
            return true;
        }

        serverIP = normalizedIP;
        serverPort = newServerPort;

        if (_socket != null)
        {
            Reconnect();
        }

        NotifyServerIPChanged();
        Debug.Log($"[PayloadSender] Server switched to {Endpoint}");
        return true;
    }

    /// <summary>
    /// 一次性设置发送策略参数。
    /// </summary>
    public bool TryApplySendSettings(int newTargetFps, int newLogInterval, int newSendHighWatermark, int newSocketLingerMs)
    {
        if (!IsValidTargetFps(newTargetFps))
        {
            Debug.LogWarning($"[PayloadSender] Invalid targetFps: '{newTargetFps}'");
            return false;
        }

        if (!IsValidLogInterval(newLogInterval))
        {
            Debug.LogWarning($"[PayloadSender] Invalid logInterval: '{newLogInterval}'");
            return false;
        }

        if (!IsValidHighWatermark(newSendHighWatermark))
        {
            Debug.LogWarning($"[PayloadSender] Invalid sendHighWatermark: '{newSendHighWatermark}'");
            return false;
        }

        if (!IsValidLingerMs(newSocketLingerMs))
        {
            Debug.LogWarning($"[PayloadSender] Invalid socketLingerMs: '{newSocketLingerMs}'");
            return false;
        }

        bool socketOptionChanged =
            sendHighWatermark != newSendHighWatermark ||
            socketLingerMs != newSocketLingerMs;

        bool unchanged =
            targetFps == newTargetFps &&
            logInterval == newLogInterval &&
            !socketOptionChanged;
        if (unchanged)
        {
            return true;
        }

        targetFps = newTargetFps;
        logInterval = newLogInterval;
        sendHighWatermark = newSendHighWatermark;
        socketLingerMs = newSocketLingerMs;

        if (_socket != null && socketOptionChanged)
        {
            Reconnect();
        }

        return true;
    }

    /// <summary>
    /// 建立 PUSH 连接。
    /// HWM=1：仅保留极少积压，降低延迟尾部。
    /// </summary>
    private void Connect()
    {
        if (_socket != null)
        {
            return;
        }

        AsyncIO.ForceDotNet.Force();

        _socket = new PushSocket();
        _socket.Options.SendHighWatermark = sendHighWatermark;
        _socket.Options.Linger = TimeSpan.FromMilliseconds(socketLingerMs);
        _socket.Connect(Endpoint);

        Debug.Log($"[PayloadSender] Connected to {Endpoint}");
    }

    /// <summary>
    /// 地址变更时的重连入口。
    /// </summary>
    private void Reconnect()
    {
        DisconnectSocket();
        Connect();
    }

    /// <summary>
    /// 关闭并释放 socket。
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
    /// 广播 IP 变化事件，供 UI 同步显示。
    /// </summary>
    private void NotifyServerIPChanged()
    {
        OnServerIPChanged?.Invoke(serverIP);
    }

    /// <summary>
    /// 校验 host/IP 的可用性。
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
    /// 归一化输入地址。
    /// </summary>
    private static string NormalizeServerIP(string value)
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

    /// <summary>
    /// 固定帧率发送循环。
    ///
    /// 流程：
    /// 1) 从 Encoder 拉取 payloadParts。
    /// 2) 非阻塞发送 multipart。
    /// 3) 成功计数 sent，失败计数 dropped。
    ///
    /// 说明：失败（TrySend 返回 false）通常表示网络拥塞。
    /// 此处选择丢帧，不回退重试，以维持实时感。
    /// </summary>
    private IEnumerator SendLoop()
    {
        while (true)
        {
            double frameStart = Time.realtimeSinceStartupAsDouble;

            if (payloadEncoder == null)
            {
                yield return null;
                continue;
            }

            double encodeStart = Time.realtimeSinceStartupAsDouble;
            bool encoded = payloadEncoder.TryEncodePayload(out byte[][] payloadParts) &&
                           payloadParts != null && payloadParts.Length > 0;
            _encodeTimeAcc += Time.realtimeSinceStartupAsDouble - encodeStart;

            if (encoded && _socket != null)
            {
                double sendStart = Time.realtimeSinceStartupAsDouble;
                bool sent = _socket.TrySendMultipartBytes(TimeSpan.Zero, payloadParts);
                _sendTimeAcc += Time.realtimeSinceStartupAsDouble - sendStart;

                if (sent)
                {
                    _sentFrameCount++;
                }
                else
                {
                    _droppedFrameCount++;
                }

                int total = _sentFrameCount + _droppedFrameCount;
                if (logInterval > 0 && total > 0 && total % logInterval == 0)
                {
                    double now = Time.realtimeSinceStartupAsDouble;
                    double intervalSec = now - _lastStatTime;
                    int deltaTotal = total - _lastStatTotal;
                    int deltaSent = _sentFrameCount - _lastStatSent;

                    float actualFps = intervalSec > 0d ? (float)(deltaSent / intervalSec) : 0f;
                    float dropRate = deltaTotal > 0 ? (float)(deltaTotal - deltaSent) / deltaTotal : 0f;
                    float avgEncodeMs = deltaTotal > 0 ? (float)(_encodeTimeAcc / deltaTotal * 1000d) : 0f;
                    float avgSendMs = deltaTotal > 0 ? (float)(_sendTimeAcc / deltaTotal * 1000d) : 0f;

                    Debug.Log($"[PayloadSender] Sent={_sentFrameCount}, Dropped={_droppedFrameCount}, ActualFPS={actualFps:F1}, DropRate={dropRate:P1}, Encode={avgEncodeMs:F2}ms, NetSend={avgSendMs:F3}ms, Interval={intervalSec:F2}s");

                    _lastStatTime = now;
                    _lastStatTotal = total;
                    _lastStatSent = _sentFrameCount;
                    _encodeTimeAcc = 0d;
                    _sendTimeAcc = 0d;
                }
            }

            float targetIntervalSeconds = 1f / Mathf.Max(1, targetFps);
            float elapsedSeconds = (float)(Time.realtimeSinceStartupAsDouble - frameStart);
            float remainingSeconds = targetIntervalSeconds - elapsedSeconds;

            if (remainingSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(remainingSeconds);
            }
            else
            {
                yield return null;
            }
        }
    }

    private static bool IsValidTargetFps(int value)
    {
        return value is >= 1 and <= 90;
    }

    private static bool IsValidLogInterval(int value)
    {
        return value >= 0;
    }

    private static bool IsValidHighWatermark(int value)
    {
        return value >= 1;
    }

    private static bool IsValidLingerMs(int value)
    {
        return value >= 0;
    }

    /// <summary>
    /// 统一回收协程与网络资源。
    /// </summary>
    private void Cleanup()
    {
        if (_sendCoroutine != null)
        {
            StopCoroutine(_sendCoroutine);
            _sendCoroutine = null;
        }

        if (_socket != null)
        {
            DisconnectSocket();
        }
    }

    /// <summary>
    /// 对象销毁时清理网络资源。
    /// </summary>
    private void OnDestroy()
    {
        Cleanup();
        NetMQConfig.Cleanup(false);
    }

    /// <summary>
    /// 应用退出时清理网络资源。
    /// </summary>
    private void OnApplicationQuit()
    {
        Cleanup();
    }
}
