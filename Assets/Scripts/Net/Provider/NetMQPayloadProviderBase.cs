using UnityEngine;

public abstract class NetMQPayloadProviderBase : MonoBehaviour
{
    public abstract bool TryGetPayload(out byte[][] payloadParts);
}
