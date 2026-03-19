using UnityEngine;

/// <summary>
/// RGBD payload 接收器
/// </summary>
public class RGBDPayloadReceiver : PayloadReceiverBase
{
    [Header("Events")]
    [Tooltip("当收到图像数据时触发")]
    public RawDataEvent OnImageReceived = new RawDataEvent();

    [Tooltip("当收到深度数据时触发")]
    public RawDataEvent OnDepthReceived = new RawDataEvent();

    private readonly object _lock = new object();
    private RawData _latestImageData;
    private RawData _latestDepthData;
    private bool _hasNewData;

    protected override string ServerIPPreferenceKey => "RGBDPayloadReceiver.ServerIP";
    protected override string DefaultTopic => "rgbd";

    protected override void Awake()
    {
        if (OnImageReceived == null)
            OnImageReceived = new RawDataEvent();
        if (OnDepthReceived == null)
            OnDepthReceived = new RawDataEvent();

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

            OnImageReceived?.Invoke(_latestImageData);
            OnDepthReceived?.Invoke(_latestDepthData);
            _hasNewData = false;
        }
    }

    protected override void HandlePayload(byte[][] payloadParts, double timestampMs)
    {
        if (payloadParts == null || payloadParts.Length != 2)
        {
            return;
        }

        lock (_lock)
        {
            _latestImageData = new RawData(payloadParts[0], timestampMs);
            _latestDepthData = new RawData(payloadParts[1], timestampMs);
            _hasNewData = true;
        }
    }
}
