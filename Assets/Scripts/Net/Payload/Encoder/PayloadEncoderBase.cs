using UnityEngine;

public abstract class PayloadEncoderBase : MonoBehaviour
{
    public abstract bool TryEncodePayload(out byte[][] payloadParts);
}
