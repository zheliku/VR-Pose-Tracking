/*
 * Quest 双目图像发送器
 *
 * 从 Meta XR Passthrough 左右相机获取图像，压缩为 JPEG 后通过 NetMQ Push 发送到 Python 端。
 * 消息格式：multipart [left_jpg, right_jpg]
 */

using System;
using System.Collections;
using NetMQ;
using NetMQ.Sockets;
using UnityEngine;

public class NetMQPayloadSender : MonoBehaviour
{
    private const string ServerIPPrefKey = "NetMQPayloadSender.ServerIP";

    [Header("Data Provider")]
    [SerializeField] private NetMQPayloadProviderBase payloadProvider;

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
    public event Action<string> ServerIPChanged;

    private void Awake()
    {
        LoadServerIPFromPrefs();
        if (payloadProvider == null)
        {
            payloadProvider = GetComponent<NetMQPayloadProviderBase>();
        }
        NotifyServerIPChanged();
    }

    private IEnumerator Start()
    {
        if (payloadProvider == null)
        {
            Debug.LogError("[NetMQPayloadSender] Payload provider is not assigned.");
            yield break;
        }

        Connect();
        _sendCoroutine = StartCoroutine(SendLoop());
    }

    public bool SaveServerIPPreference(string newServerIP)
    {
        string normalizedIP = NormalizeServerIP(newServerIP);
        if (!IsValidServerAddress(normalizedIP))
        {
            Debug.LogWarning($"[NetMQPayloadSender] Invalid server IP/host: '{newServerIP}'");
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
            Debug.LogWarning($"[NetMQPayloadSender] Invalid server IP/host: '{newServerIP}'");
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
        Debug.Log($"[NetMQPayloadSender] Server switched to {Endpoint}");
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

        Debug.Log($"[NetMQPayloadSender] Connected to {Endpoint}");
    }

    private void Reconnect()
    {
        DisconnectSocket();
        Connect();
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

    private IEnumerator SendLoop()
    {
        float intervalSeconds = 1f / Mathf.Max(1, targetFps);
        WaitForSeconds wait = new WaitForSeconds(intervalSeconds);

        while (true)
        {
            if (payloadProvider == null)
            {
                yield return null;
                continue;
            }

            if (payloadProvider.TryGetPayload(out byte[][] payloadParts) &&
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
                    Debug.Log($"[NetMQPayloadSender] Sent={_sentFrameCount}, Dropped={_droppedFrameCount}");
                }
            }

            yield return wait;
        }
    }

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
