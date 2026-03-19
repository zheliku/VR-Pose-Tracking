using System;
using System.Diagnostics;
using System.Threading;
using NetMQ;
using NetMQ.Sockets;
using UnityEngine;
using Debug = UnityEngine.Debug;

public abstract class PayloadReceiverBase : MonoBehaviour
{
    [Header("Network Settings")]
    [SerializeField] protected string serverIP = "127.0.0.1";
    [SerializeField] protected int serverPort = 5556;
    [SerializeField] protected string topic = string.Empty;

    private SubscriberSocket _socket;
    private Thread _receiveThread;
    private volatile bool _running;
    private readonly Stopwatch _stopwatch = new Stopwatch();

    protected abstract string ServerIPPreferenceKey { get; }
    protected virtual string DefaultTopic => "payload";
    protected virtual string LogPrefix => $"[{GetType().Name}]";

    public bool IsConnected => _running && _socket != null;
    public string ServerAddress => $"tcp://{serverIP}:{serverPort}";
    public string CurrentServerIP => serverIP;

    public event Action<string> ServerIPChanged;

    protected virtual void Awake()
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            topic = DefaultTopic;
        }

        LoadServerIPFromPrefs();
        NotifyServerIPChanged();
    }

    protected virtual void Start()
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
            Debug.LogWarning($"{LogPrefix} Invalid server IP/host: '{newServerIP}'");
            return false;
        }

        PlayerPrefs.SetString(ServerIPPreferenceKey, normalizedIP);
        PlayerPrefs.Save();
        return true;
    }

    public bool TrySetServerIP(string newServerIP)
    {
        string normalizedIP = NormalizeServerIP(newServerIP);
        if (!IsValidServerAddress(normalizedIP))
        {
            Debug.LogWarning($"{LogPrefix} Invalid server IP/host: '{newServerIP}'");
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
        Debug.Log($"{LogPrefix} Server switched to {ServerAddress}");
        return true;
    }

    public string GetSavedServerIP()
    {
        return NormalizeServerIP(PlayerPrefs.GetString(ServerIPPreferenceKey, string.Empty));
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

        Debug.Log($"{LogPrefix} Connected to {ServerAddress}, topic: {topic}");
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
        Debug.Log($"{LogPrefix} Disconnected");
    }

    protected abstract void HandlePayload(byte[][] payloadParts, double timestampMs);

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
                HandlePayload(payloadParts, timestampMs);
            }
            catch (Exception e)
            {
                if (_running)
                {
                    Debug.LogError($"{LogPrefix} Error: {e.Message}");
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
        string savedIP = NormalizeServerIP(PlayerPrefs.GetString(ServerIPPreferenceKey, string.Empty));
        if (IsValidServerAddress(savedIP))
        {
            serverIP = savedIP;
        }
    }

    private void SaveServerIPToPrefs()
    {
        PlayerPrefs.SetString(ServerIPPreferenceKey, serverIP);
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

    protected virtual void OnDestroy()
    {
        Disconnect();
        NetMQConfig.Cleanup(false);
    }

    protected virtual void OnApplicationQuit()
    {
        Disconnect();
    }
}
