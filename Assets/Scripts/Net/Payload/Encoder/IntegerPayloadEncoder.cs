using System.Text;
using UnityEngine;

public class IntegerPayloadEncoder : PayloadEncoderBase
{
    [SerializeField] private int value = 0;

    public void SetValue(int newValue)
    {
        value = newValue;
    }

    public override bool TryEncodePayload(out byte[][] payloadParts)
    {
        payloadParts = new[] { Encoding.UTF8.GetBytes(value.ToString()) };
        return true;
    }
}
