using System.Text;
using UnityEngine;

/// <summary>
/// 整数测试编码器。
///
/// 用途：
/// - 最小协议联调（验证 Sender/Receiver/Decoder 链路是否通）。
/// - 将一个 int 文本化后作为单帧 payload 发送。
/// </summary>
public class IntegerEncoder : EncoderBase
{
    [SerializeField] private int value = 0;

    /// <summary>
    /// 运行时更新待发送数值。
    /// </summary>
    public void SetValue(int newValue)
    {
        value = newValue;
    }

    /// <summary>
    /// 将 int 转为 UTF-8 文本，输出单帧 payload。
    /// </summary>
    public override bool TryEncodePayload(out byte[][] payloadParts)
    {
        payloadParts = new[] { Encoding.UTF8.GetBytes(value.ToString()) };
        return true;
    }
}
