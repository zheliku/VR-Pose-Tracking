/*
 * 位姿追踪数据接收器
 * 
 * 从服务器接收位姿追踪数据，通过 UnityEvent 提供数据。
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
using UnityEngine.Events;
using NetMQ;
using NetMQ.Sockets;
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
    [Header("Network Settings")]
    [SerializeField] private string serverIP = "172.24.244.81";
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

    // 线程安全的数据传递
    private RawData _latestImageData;
    private PoseData _latestPoseData;
    private TrackingPhase _latestPhase;
    private readonly object _lock = new object();
    private bool _hasNewData;

    private TrackingPhase _lastPhase = TrackingPhase.Detecting;
    private Stopwatch _stopwatch = new Stopwatch();

    // 公共属性
    public bool IsConnected => _running && _socket != null;
    public string ServerAddress => $"tcp://{serverIP}:{serverPort}";
    public TrackingPhase CurrentPhase => _lastPhase;

    void Awake()
    {
        if (OnImageReceived == null)
            OnImageReceived = new RawDataEvent();
        if (OnPoseReceived == null)
            OnPoseReceived = new PoseDataEvent();
        if (OnTrackingStarted == null)
            OnTrackingStarted = new UnityEvent();
        if (OnTrackingLost == null)
            OnTrackingLost = new UnityEvent();
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

        Debug.Log($"[PoseDataReceiver] Connected to {ServerAddress}, topic: {topic}");

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

        Debug.Log("[PoseDataReceiver] Disconnected");
    }

    void Update()
    {
        lock (_lock)
        {
            if (_hasNewData)
            {
                // 检测阶段转换
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
    }

    private void ReceiveLoop()
    {
        while (_running)
        {
            try
            {
                if (_socket.TryReceiveFrameBytes(TimeSpan.FromMilliseconds(100), out byte[] topicBytes))
                {
                    byte[] phaseData = _socket.ReceiveFrameBytes();
                    byte[] colorData = _socket.ReceiveFrameBytes();
                    byte[] poseData = _socket.ReceiveFrameBytes();

                    TrackingPhase phase = phaseData.Length > 0 && phaseData[0] == 1
                        ? TrackingPhase.Tracking
                        : TrackingPhase.Detecting;

                    Matrix4x4? poseMatrix = null;
                    if (poseData.Length > 0)
                    {
                        string poseJson = System.Text.Encoding.UTF8.GetString(poseData);
                        if (!string.IsNullOrEmpty(poseJson))
                        {
                            poseMatrix = ParsePoseMatrix(poseJson);
                        }
                    }

                    double timestampMs = _stopwatch.Elapsed.TotalMilliseconds;

                    lock (_lock)
                    {
                        _latestImageData = new RawData(colorData, timestampMs);
                        _latestPoseData = new PoseData(poseMatrix);
                        _latestPhase = phase;
                        _hasNewData = true;
                    }
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

    private Matrix4x4? ParsePoseMatrix(string json)
    {
        try
        {
            int matrixStart = json.IndexOf("[[");
            int matrixEnd = json.LastIndexOf("]]");
            if (matrixStart == -1 || matrixEnd == -1)
                return null;

            string matrixStr = json.Substring(matrixStart + 1, matrixEnd - matrixStart);
            Matrix4x4 m = new Matrix4x4();
            string[] rows = matrixStr.Split(new string[] { "], [", "],[" }, StringSplitOptions.None);

            if (rows.Length != 4)
                return null;

            for (int row = 0; row < 4; row++)
            {
                string rowStr = rows[row].Trim('[', ']', ' ');
                string[] values = rowStr.Split(',');

                if (values.Length != 4)
                    return null;

                for (int col = 0; col < 4; col++)
                {
                    if (float.TryParse(values[col].Trim(), out float val))
                    {
                        m[row, col] = val;
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            return m;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PoseDataReceiver] Failed to parse pose matrix: {e.Message}");
        }
        return null;
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
