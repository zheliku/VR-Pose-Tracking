/*
 * 图像显示与统计组件
 * 
 * 负责将接收到的图像数据渲染到 RawImage，并统计 FPS、帧间隔。
 * 订阅 Receiver 的 OnImageReceived 事件。
 * 
 * 使用方法：
 * 1. 将此脚本挂载到任意 GameObject
 * 2. 在 Receiver 的 OnImageReceived 事件中拖拽此组件的 OnImageReceived 方法
 * 3. 设置 Image Display 的 RawImage
 * 4. （可选）设置 Stats Text 用于 UI 显示
 */

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ImageViewer : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private RawImage imageDisplay;

    [Header("Stats Display")]
    [SerializeField] private TextMeshProUGUI statsText;
    [SerializeField] private int statsUpdateInterval = 30; // 每多少帧更新统计

    // 纹理
    private Texture2D _texture;

    // 统计数据
    private int _frameCount;
    private int _totalFrames;
    private float _lastStatsTime;
    private LatencyStats _intervalStats = new LatencyStats(100);
    private double _lastTimestampMs;

    // 公共属性
    public int TotalFrames => _totalFrames;
    public float CurrentFPS { get; private set; }
    public float AvgIntervalMs => _intervalStats.GetStats().avg;

    void Start()
    {
        // 初始化纹理
        _texture = new Texture2D(2, 2);
        if (imageDisplay != null)
            imageDisplay.texture = _texture;

        _lastStatsTime = Time.time;
        _lastTimestampMs = 0;
    }

    /// <summary>
    /// 处理图像数据（核心方法）
    /// </summary>
    public void OnImageReceived(RawData data)
    {
        // 显示图像
        if (data.Data != null && _texture != null)
        {
            _texture.LoadImage(data.Data);
        }

        // 统计
        UpdateStatsInternal(data.TimestampMs);
    }

    private void UpdateStatsInternal(double timestampMs)
    {
        _frameCount++;
        _totalFrames++;

        // 计算帧间隔
        if (_lastTimestampMs > 0)
        {
            float intervalMs = (float)(timestampMs - _lastTimestampMs);
            if (intervalMs > 0 && intervalMs < 1000)
            {
                _intervalStats.Record(intervalMs);
            }
        }
        _lastTimestampMs = timestampMs;

        // 定期更新统计显示
        if (_frameCount >= statsUpdateInterval)
        {
            UpdateStatsDisplay();
        }
    }

    private void UpdateStatsDisplay()
    {
        float elapsed = Time.time - _lastStatsTime;
        if (elapsed < 0.1f)
            return;

        CurrentFPS = _frameCount / elapsed;
        var stats = _intervalStats.GetStats();

        string statsStr = $"FPS: {CurrentFPS:F1} | Frames: {_totalFrames} | " +
                         $"Interval: {stats.avg:F2}ms (±{stats.std:F2})";

        Debug.Log($"[ImageViewer] {statsStr}");

        if (statsText != null)
        {
            statsText.text = statsStr;
        }

        _frameCount = 0;
        _lastStatsTime = Time.time;
    }

    void OnDestroy()
    {
        var finalStats = _intervalStats.GetStats();
        Debug.Log($"[ImageViewer] Final - Total Frames: {_totalFrames}, " +
                 $"Avg Interval: {finalStats.avg:F2}ms");
    }

    /// <summary>
    /// 简单的延迟统计类（滚动窗口）
    /// </summary>
    private class LatencyStats
    {
        private readonly Queue<float> _values;
        private readonly int _maxSize;
        private float _sum;

        public LatencyStats(int windowSize)
        {
            _maxSize = windowSize;
            _values = new Queue<float>(windowSize);
            _sum = 0;
        }

        public void Record(float value)
        {
            if (_values.Count >= _maxSize)
            {
                _sum -= _values.Dequeue();
            }
            _values.Enqueue(value);
            _sum += value;
        }

        public (float avg, float min, float max, float std) GetStats()
        {
            if (_values.Count == 0)
                return (0, 0, 0, 0);

            float avg = _sum / _values.Count;
            float min = float.MaxValue;
            float max = float.MinValue;
            float sumSq = 0;

            foreach (var v in _values)
            {
                if (v < min) min = v;
                if (v > max) max = v;
                sumSq += (v - avg) * (v - avg);
            }

            float std = _values.Count > 1
                ? (float)Math.Sqrt(sumSq / (_values.Count - 1))
                : 0;

            return (avg, min, max, std);
        }
    }
}
