using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 追踪阶段枚举
/// </summary>
public enum TrackingPhase
{
    Detecting = 0,
    Tracking = 1
}

/// <summary>
/// 追踪 payload 接收器
/// </summary>
public class TrackingPayloadReceiver : PayloadReceiverBase
{
    [Header("Events")]
    [Tooltip("当收到图像数据时触发")]
    public RawDataEvent OnImageReceived = new RawDataEvent();

    [Tooltip("当收到位姿数据时触发")]
    public PoseDataEvent OnPoseReceived = new PoseDataEvent();

    [Tooltip("当从检测切换到追踪时触发")]
    public UnityEvent OnTrackingStarted = new UnityEvent();

    [Tooltip("当追踪丢失时触发")]
    public UnityEvent OnTrackingLost = new UnityEvent();

    private readonly object _lock = new object();
    private readonly ITrackingPayloadDecoder _payloadDecoder = new TrackingPayloadDecoder();

    private RawData _latestImageData;
    private PoseData _latestPoseData;
    private TrackingPhase _latestPhase;
    private bool _hasNewData;
    private TrackingPhase _lastPhase = TrackingPhase.Detecting;

    protected override string ServerIPPreferenceKey => "TrackingPayloadReceiver.ServerIP";
    protected override string DefaultTopic => "tracking";

    public TrackingPhase CurrentPhase => _lastPhase;

    protected override void Awake()
    {
        if (OnImageReceived == null)
            OnImageReceived = new RawDataEvent();
        if (OnPoseReceived == null)
            OnPoseReceived = new PoseDataEvent();
        if (OnTrackingStarted == null)
            OnTrackingStarted = new UnityEvent();
        if (OnTrackingLost == null)
            OnTrackingLost = new UnityEvent();

        base.Awake();
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
            if (_latestPoseData.HasPose)
            {
                OnPoseReceived?.Invoke(_latestPoseData);
            }
            _hasNewData = false;
        }
    }

    protected override void HandlePayload(byte[][] payloadParts, double timestampMs)
    {
        if (!_payloadDecoder.TryDecode(payloadParts, timestampMs, out TrackingPayload payload))
        {
            return;
        }

        lock (_lock)
        {
            _latestImageData = payload.ImageData;
            _latestPoseData = payload.PoseData;
            _latestPhase = payload.Phase;
            _hasNewData = true;
        }
    }
}
