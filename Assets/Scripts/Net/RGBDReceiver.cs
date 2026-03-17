/*
 * RGBD 数据接收器
 * 
 * 从服务器接收 RGBD 数据，通过 UnityEvent 提供数据。
 * 
 * 使用方法：
 * 1. 将此脚本挂载到任意 GameObject
 * 2. 在 Inspector 中配置服务器地址
 * 3. 绑定事件处理函数
 */

using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using NetMQ;
using NetMQ.Sockets;
using Debug = UnityEngine.Debug;

/// <summary>
/// RGBD 数据接收器
/// </summary>
public class RGBDReceiver : MonoBehaviour
{
    [Header("Network Settings")]
    [SerializeField] private string serverIP = "172.24.244.81";
    [SerializeField] private int serverPort = 5556;
    [SerializeField] private string topic = "rgbd";

    [Header("Events")]
    [Tooltip("当收到图像数据时触发")]
    public RawDataEvent OnImageReceived = new RawDataEvent();

    [Tooltip("当收到深度数据时触发")]
    public RawDataEvent OnDepthReceived = new RawDataEvent();

    private SubscriberSocket _socket;
    private Thread _receiveThread;
    private volatile bool _running;

    // 线程安全的数据传递
    private RawData _latestImageData;
    private RawData _latestDepthData;
    private readonly object _lock = new object();
    private bool _hasNewData;

    // 高精度计时器（线程安全）
    private Stopwatch _stopwatch = new Stopwatch();

    // 公共属性
    public bool IsConnected => _running && _socket != null;
    public string ServerAddress => $"tcp://{serverIP}:{serverPort}";

    void Awake()
    {
        if (OnImageReceived == null)
            OnImageReceived = new RawDataEvent();
        if (OnDepthReceived == null)
            OnDepthReceived = new RawDataEvent();
    }

    void Start()
    {
        _stopwatch.Start();
        Connect();
    }

    public void Connect()
    {
        if (_running) return;

        AsyncIO.ForceDotNet.Force();

        _socket = new SubscriberSocket();
        _socket.Connect(ServerAddress);
        _socket.Subscribe(topic);

        Debug.Log($"[RGBDReceiver] Connected to {ServerAddress}, topic: {topic}");

        _running = true;
        _receiveThread = new Thread(ReceiveLoop);
        _receiveThread.Start();
    }

    public void Disconnect()
    {
        _running = false;

        if (_receiveThread != null && _receiveThread.IsAlive)
        {
            _receiveThread.Join(1000);
        }

        if (_socket != null)
        {
            _socket.Close();
            _socket.Dispose();
            _socket = null;
        }

        Debug.Log("[RGBDReceiver] Disconnected");
    }

    void Update()
    {
        lock (_lock)
        {
            if (_hasNewData)
            {
                OnImageReceived?.Invoke(_latestImageData);
                OnDepthReceived?.Invoke(_latestDepthData);
                _hasNewData = false;
            }
        }
    }

    private void ReceiveLoop()
    {
        while (_running)
        {
            try
            {
                if (_socket.TryReceiveFrameBytes(TimeSpan.FromMilliseconds(100), out byte[] topicBytes))
                {
                    byte[] colorData = _socket.ReceiveFrameBytes();
                    byte[] depthData = _socket.ReceiveFrameBytes();

                    double timestampMs = _stopwatch.Elapsed.TotalMilliseconds;

                    lock (_lock)
                    {
                        _latestImageData = new RawData(colorData, timestampMs);
                        _latestDepthData = new RawData(depthData, timestampMs);
                        _hasNewData = true;
                    }
                }
            }
            catch (Exception e)
            {
                if (_running)
                {
                    Debug.LogError($"[RGBDReceiver] Error: {e.Message}");
                }
            }
        }
    }

    void OnDestroy()
    {
        Disconnect();
        NetMQConfig.Cleanup();
    }

    void OnApplicationQuit()
    {
        Disconnect();
    }
}
