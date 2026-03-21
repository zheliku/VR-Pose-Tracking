using RuntimeInspectorNamespace;
using UnityEngine;

public class InspectorConnector : MonoBehaviour
{
    public RuntimeInspector inspector;

    public Object initialTarget;

    [Tooltip("锁定 Inspector，使其不随 Hierarchy 的选择而改变")]
    public bool lockToTarget = true;

    [Tooltip("即使 initialTarget 为空，是否也在启动时清空显示")]
    public bool clearOnStart = true;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (inspector == null)
        {
            inspector = GetComponentInChildren<RuntimeInspector>();
        }

        if (inspector != null)
        {
            if (initialTarget == null)
            {
                RuntimeSenderConfigPanel senderPanel = FindFirstObjectByType<RuntimeSenderConfigPanel>();
                if (senderPanel != null)
                {
                    initialTarget = senderPanel;
                }
                else
                {
                    RuntimeReceiverConfigPanel receiverPanel = FindFirstObjectByType<RuntimeReceiverConfigPanel>();
                    if (receiverPanel != null)
                    {
                        initialTarget = receiverPanel;
                    }
                }
            }

            if (initialTarget != null)
            {
                inspector.Inspect(initialTarget);
            }
            else if (clearOnStart)
            {
                inspector.Inspect(null);
            }

            inspector.IsLocked = lockToTarget;
        }
        else
        {
            Debug.LogWarning("[InspectorConnector] No RuntimeInspector assigned.");
        }
    }
}
