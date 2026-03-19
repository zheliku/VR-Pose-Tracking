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
    private const string ServerIPPrefKey = "PayloadSender.ServerIP";

    [Header("Data Encoder")]
    [SerializeField] private EncoderBase payloadEncoder;

    [Header("Network")]
    [SerializeField] private string serverIP = "127.0.0.1";
    [SerializeField] private int serverPort = 5557;

    [Header("Send Settings")]
    [Range(1, 90)]
    [SerializeField] private int targetFps = 60;
    [SerializeField] private int logInterval = 30;

    private PushSocket _socket;
    private Coroutine _sendCoroutine;

    private int _sentFrameCount;
    private int _droppedFrameCount;

    private string Endpoint => $"tcp://{serverIP}:{serverPort}";
    public string CurrentServerIP => serverIP;

    [Header("Events")]
    public StringEvent OnServerIPChanged = new StringEvent();

    /// <summary>
    /// 初始化事件、恢复保存的 IP、自动查找同对象编码器。
    /// </summary>
    private void Awake()
    {
        if (OnServerIPChanged == null)
        {
            OnServerIPChanged = new StringEvent();
        }

        LoadServerIPFromPrefs();
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
        _sendCoroutine = StartCoroutine(SendLoop());
    }

    /// <summary>
    /// 保存 IP 到 PlayerPrefs，不影响当前连接状态。
    /// </summary>
    public bool SaveServerIPPreference(string newServerIP)
    {
        string normalizedIP = NormalizeServerIP(newServerIP);
        if (!IsValidServerAddress(normalizedIP))
        {
            Debug.LogWarning($"[PayloadSender] Invalid server IP/host: '{newServerIP}'");
            return false;
        }

        PlayerPrefs.SetString(ServerIPPrefKey, normalizedIP);
        PlayerPrefs.Save();
        return true;
    }

    /// <summary>
    /// 运行中切换服务器地址，必要时自动重连。
    /// </summary>
    public bool TrySetServerIP(string newServerIP)
    {
        string normalizedIP = NormalizeServerIP(newServerIP);
        if (!IsValidServerAddress(normalizedIP))
        {
            Debug.LogWarning($"[PayloadSender] Invalid server IP/host: '{newServerIP}'");
            return false;
        }

        if (string.Equals(serverIP, normalizedIP, StringComparison.OrdinalIgnoreCase))
        {
            NotifyServerIPChanged();
            return true;
        }

        serverIP = normalizedIP;
        SaveServerIPToPrefs();

        if (_socket != null)
        {
            Reconnect();
        }

        NotifyServerIPChanged();
        Debug.Log($"[PayloadSender] Server switched to {Endpoint}");
        return true;
    }

    /// <summary>
    /// 仅读取已保存 IP，不直接应用。
    /// </summary>
    public string GetSavedServerIP()
    {
        return NormalizeServerIP(PlayerPrefs.GetString(ServerIPPrefKey, string.Empty));
    }

    /// <summary>
    /// 应用本地保存 IP 到当前连接。
    /// </summary>
    public void ApplySavedServerIP()
    {
        string savedServerIP = GetSavedServerIP();
        if (!string.IsNullOrEmpty(savedServerIP))
        {
            TrySetServerIP(savedServerIP);
        }
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
        _socket.Options.SendHighWatermark = 1;
        _socket.Options.Linger = TimeSpan.Zero;
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
    /// 从本地配置加载 IP。
    /// </summary>
    private void LoadServerIPFromPrefs()
    {
        string savedIP = NormalizeServerIP(PlayerPrefs.GetString(ServerIPPrefKey, string.Empty));
        if (IsValidServerAddress(savedIP))
        {
            serverIP = savedIP;
        }
    }

    /// <summary>
    /// 持久化当前 IP。
    /// </summary>
    private void SaveServerIPToPrefs()
    {
        PlayerPrefs.SetString(ServerIPPrefKey, serverIP);
        PlayerPrefs.Save();
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
        float intervalSeconds = 1f / Mathf.Max(1, targetFps);
        WaitForSeconds wait = new WaitForSeconds(intervalSeconds);

        while (true)
        {
            if (payloadEncoder == null)
            {
                yield return null;
                continue;
            }

            if (payloadEncoder.TryEncodePayload(out byte[][] payloadParts) &&
                payloadParts != null && payloadParts.Length > 0 && _socket != null)
            {
                bool sent = _socket.TrySendMultipartBytes(TimeSpan.Zero, payloadParts);
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
                    Debug.Log($"[PayloadSender] Sent={_sentFrameCount}, Dropped={_droppedFrameCount}");
                }
            }

            yield return wait;
        }
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
