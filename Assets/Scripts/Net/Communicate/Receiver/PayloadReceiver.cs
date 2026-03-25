using System;
using System.Diagnostics;
using System.Threading;
using NetMQ;
using NetMQ.Sockets;
using RuntimeInspectorNamespace;
using UnityEngine;
using UnityEngine.Events;
using VInspector;
using Debug = UnityEngine.Debug;

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
public class RawPayloadEvent : UnityEvent<RawPayload> { }

public class PayloadReceiver : MonoBehaviour
{
    private const string ReceiverIPPrefKey = "PayloadReceiver.ServerIP";
    private const string ReceiverPortPrefKey = "PayloadReceiver.ServerPort";
    private const string ReceiverUseTopicPrefKey = "PayloadReceiver.UseTopic";
    private const string ReceiverTopicPrefKey = "PayloadReceiver.Topic";
    private const string ReceiveHighWatermarkPrefKey = "PayloadReceiver.ReceiveHighWatermark";
    private const string SocketLingerMsPrefKey = "PayloadReceiver.SocketLingerMs";
    private const string ReceivePollTimeoutMsPrefKey = "PayloadReceiver.ReceivePollTimeoutMs";

    private SubscriberSocket _socket;
    private Thread _receiveThread;
    private volatile bool _running;
    private readonly Stopwatch _stopwatch = new Stopwatch();

    private readonly object _lock = new object();
    private RawPayload _latestPayload;
    private bool _hasNewPayload;

    [ShowInInspector]
    public string ServerIP
    {
        get => PlayerPrefs.GetString(ReceiverIPPrefKey, "127.0.0.1");
        set => PlayerPrefs.SetString(ReceiverIPPrefKey, value);
    }

    [ShowInInspector]
    public int ServerPort
    {
        get => PlayerPrefs.GetInt(ReceiverPortPrefKey, 5556);
        set => PlayerPrefs.SetInt(ReceiverPortPrefKey, value);
    }

    [ShowInInspector]
    public bool UseTopic
    {
        get => PlayerPrefs.GetInt(ReceiverUseTopicPrefKey, 1) != 0;
        set => PlayerPrefs.SetInt(ReceiverUseTopicPrefKey, value ? 1 : 0);
    }

    [ShowInInspector]
    public string Topic
    {
        get => PlayerPrefs.GetString(ReceiverTopicPrefKey, "payload");
        set => PlayerPrefs.SetString(ReceiverTopicPrefKey, value);
    }

    [ShowInInspector]
    public int ReceiveHighWatermark
    {
        get => PlayerPrefs.GetInt(ReceiveHighWatermarkPrefKey, 1);
        set => PlayerPrefs.SetInt(ReceiveHighWatermarkPrefKey, value);
    }

    [ShowInInspector]
    public int SocketLingerMs
    {
        get => PlayerPrefs.GetInt(SocketLingerMsPrefKey, 0);
        set => PlayerPrefs.SetInt(SocketLingerMsPrefKey, value);
    }

    [ShowInInspector]
    public int ReceivePollTimeoutMs
    {
        get => PlayerPrefs.GetInt(ReceivePollTimeoutMsPrefKey, 100);
        set => PlayerPrefs.SetInt(ReceivePollTimeoutMsPrefKey, value);
    }

    public bool IsConnected => _running && _socket != null;
    public string ServerAddress => $"tcp://{ServerIP}:{ServerPort}";

    [Header("Events")]
    public RawPayloadEvent OnPayloadReceived = new RawPayloadEvent();

    private void Awake()
    {
        if (OnPayloadReceived == null)
        {
            OnPayloadReceived = new RawPayloadEvent();
        }
    }

    private void Start()
    {
        if (!_stopwatch.IsRunning)
        {
            _stopwatch.Start();
        }

        Connect();
    }

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

        OnPayloadReceived?.Invoke(payload);
    }

    public void Connect()
    {
        if (_running)
        {
            return;
        }

        AsyncIO.ForceDotNet.Force();

        _socket = new SubscriberSocket();
        _socket.Options.ReceiveHighWatermark = ReceiveHighWatermark;
        _socket.Options.Linger = TimeSpan.FromMilliseconds(SocketLingerMs);
        _socket.Connect(ServerAddress);
        _socket.Subscribe(UseTopic ? Topic : string.Empty);

        _running = true;
        _receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        _receiveThread.Start();

        string subscribeDesc = UseTopic ? $"topic: {Topic}" : "topic: <none>";
        Debug.Log($"[PayloadReceiver] Connected to {ServerAddress}, {subscribeDesc}");
    }

    [Button("Reconnect")]
    [RuntimeInspectorButton("Reconnect", false, ButtonVisibility.InitializedObjects)]
    private void Reconnect()
    {
        Disconnect();
        Connect();
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
        Debug.Log("[PayloadReceiver] Disconnected");
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
                if (!_socket.TryReceiveMultipartMessage(TimeSpan.FromMilliseconds(ReceivePollTimeoutMs), ref message))
                {
                    continue;
                }

                int minFrames = UseTopic ? 2 : 1;
                if (message.FrameCount < minFrames)
                {
                    continue;
                }

                int startFrame = UseTopic ? 1 : 0;
                string receivedTopic = UseTopic ? message[0].ConvertToString() : string.Empty;

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

    private void OnDestroy()
    {
        Disconnect();
        NetMQConfig.Cleanup(false);
    }

    private void OnApplicationQuit()
    {
        Disconnect();
    }
}
