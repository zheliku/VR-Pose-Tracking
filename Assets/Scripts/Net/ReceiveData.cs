/*
 * 通用数据接口定义
 * 
 * 定义图像、位姿、原始字节等数据接口。
 * Receiver 发送实现这些接口的数据，处理器接收接口类型。
 */

using System;
using UnityEngine;
using UnityEngine.Events;

// ==================== 数据接口 ====================

/// <summary>
/// 原始字节数据接口（用于图像、深度等）
/// </summary>
public interface IRawData
{
    byte[] Data { get; }            // 原始字节数据
    double TimestampMs { get; }     // 接收时间戳（毫秒）
}

/// <summary>
/// 位姿数据接口
/// </summary>
public interface IPoseData
{
    Matrix4x4? PoseMatrix { get; }  // 4x4 位姿矩阵，null 表示无位姿
    bool HasPose { get; }           // 是否有有效位姿
}

/// <summary>
/// 文本/状态数据接口
/// </summary>
public interface ITextData
{
    string Text { get; }            // 文本内容
}

// ==================== 数据结构实现 ====================

/// <summary>
/// 原始字节数据（实现 IRawData）
/// 可用于图像、深度或任何字节流数据
/// </summary>
[Serializable]
public struct RawData : IRawData
{
    [SerializeField] private byte[] data;
    [SerializeField] private double timestampMs;

    public byte[] Data => data;
    public double TimestampMs => timestampMs;

    public RawData(byte[] data, double timestampMs)
    {
        this.data = data;
        this.timestampMs = timestampMs;
    }
}

/// <summary>
/// 位姿数据（实现 IPoseData）
/// </summary>
[Serializable]
public struct PoseData : IPoseData
{
    [SerializeField] private Matrix4x4 poseMatrix;
    [SerializeField] private bool hasPose;

    public Matrix4x4? PoseMatrix => hasPose ? poseMatrix : null;
    public bool HasPose => hasPose;

    public PoseData(Matrix4x4? matrix)
    {
        if (matrix.HasValue)
        {
            poseMatrix = matrix.Value;
            hasPose = true;
        }
        else
        {
            poseMatrix = Matrix4x4.identity;
            hasPose = false;
        }
    }
}

// ==================== UnityEvent 定义 ====================

/// <summary>
/// 原始字节数据事件（图像、深度通用）
/// </summary>
[Serializable]
public class RawDataEvent : UnityEvent<RawData> { }

/// <summary>
/// 位姿数据事件
/// </summary>
[Serializable]
public class PoseDataEvent : UnityEvent<PoseData> { }
