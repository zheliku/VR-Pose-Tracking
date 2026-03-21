using RuntimeInspectorNamespace;
using UnityEngine;

[DisallowMultipleComponent]
public class RuntimeReceiverConfigPanel : MonoBehaviour
{
    private const string ReceiverIPPrefKey = "PayloadReceiver.ServerIP";
    private const string ReceiverPortPrefKey = "PayloadReceiver.ServerPort";
    private const string ReceiverUseTopicPrefKey = "PayloadReceiver.UseTopic";
    private const string ReceiverTopicPrefKey = "PayloadReceiver.Topic";
    private const string ReceiveHighWatermarkPrefKey = "PayloadReceiver.ReceiveHighWatermark";
    private const string SocketLingerMsPrefKey = "PayloadReceiver.SocketLingerMs";
    private const string ReceivePollTimeoutMsPrefKey = "PayloadReceiver.ReceivePollTimeoutMs";

    [Header("Target")]
    [SerializeField] private PayloadReceiver payloadReceiver;

    [Header("Config")]
    [SerializeField] private string serverIP = "127.0.0.1";
    [SerializeField] private int serverPort = 5556;
    [SerializeField] private bool useTopic = true;
    [SerializeField] private string topic = "tracking";
    [SerializeField][Range(1, 10)] private int receiveHighWatermark = 1;
    [SerializeField] private int socketLingerMs = 0;
    [SerializeField] private int receivePollTimeoutMs = 100;

    private void Awake()
    {
        LoadFromPrefs();
        ApplyLoadedConfigToTarget();
    }

    [RuntimeInspectorButton("Apply Receiver Config", false, ButtonVisibility.InitializedObjects)]
    public void SyncFromTarget()
    {
        if (payloadReceiver == null)
        {
            Debug.LogWarning("[RuntimeReceiverConfigPanel] PayloadReceiver is not assigned.");
            return;
        }

        bool endpointApplied = payloadReceiver.TryApplyConnectionConfig(serverIP, serverPort, useTopic, topic);
        bool settingsApplied = payloadReceiver.TryApplyReceiveSettings(receiveHighWatermark, socketLingerMs, receivePollTimeoutMs);

        serverIP = payloadReceiver.CurrentServerIP;
        serverPort = payloadReceiver.CurrentServerPort;
        useTopic = payloadReceiver.UseTopic;
        topic = payloadReceiver.Topic;
        receiveHighWatermark = payloadReceiver.CurrentReceiveHighWatermark;
        socketLingerMs = payloadReceiver.CurrentSocketLingerMs;
        receivePollTimeoutMs = payloadReceiver.CurrentReceivePollTimeoutMs;

        if (endpointApplied && settingsApplied)
        {
            PlayerPrefs.SetString(ReceiverIPPrefKey, serverIP);
            PlayerPrefs.SetInt(ReceiverPortPrefKey, serverPort);
            PlayerPrefs.SetInt(ReceiverUseTopicPrefKey, useTopic ? 1 : 0);
            PlayerPrefs.SetString(ReceiverTopicPrefKey, topic);
            PlayerPrefs.SetInt(ReceiveHighWatermarkPrefKey, receiveHighWatermark);
            PlayerPrefs.SetInt(SocketLingerMsPrefKey, socketLingerMs);
            PlayerPrefs.SetInt(ReceivePollTimeoutMsPrefKey, receivePollTimeoutMs);
            PlayerPrefs.Save();
        }
    }

    // [RuntimeInspectorButton("Reload From Prefs", false, ButtonVisibility.InitializedObjects)]
    public void LoadFromPrefs()
    {
        if (payloadReceiver != null)
        {
            serverIP = payloadReceiver.CurrentServerIP;
            serverPort = payloadReceiver.CurrentServerPort;
            useTopic = payloadReceiver.UseTopic;
            topic = payloadReceiver.Topic;
            receiveHighWatermark = payloadReceiver.CurrentReceiveHighWatermark;
            socketLingerMs = payloadReceiver.CurrentSocketLingerMs;
            receivePollTimeoutMs = payloadReceiver.CurrentReceivePollTimeoutMs;
        }

        if (PlayerPrefs.HasKey(ReceiverIPPrefKey))
        {
            string savedIP = Normalize(PlayerPrefs.GetString(ReceiverIPPrefKey, serverIP));
            serverIP = savedIP;
        }

        if (PlayerPrefs.HasKey(ReceiverPortPrefKey))
        {
            int savedPort = PlayerPrefs.GetInt(ReceiverPortPrefKey, serverPort);
            if (savedPort is >= 1 and <= 65535)
            {
                serverPort = savedPort;
            }
        }

        if (PlayerPrefs.HasKey(ReceiverUseTopicPrefKey))
        {
            useTopic = PlayerPrefs.GetInt(ReceiverUseTopicPrefKey, useTopic ? 1 : 0) != 0;
        }

        if (PlayerPrefs.HasKey(ReceiverTopicPrefKey))
        {
            string savedTopic = Normalize(PlayerPrefs.GetString(ReceiverTopicPrefKey, topic));
            if (!string.IsNullOrEmpty(savedTopic))
            {
                topic = savedTopic;
            }
        }

        if (PlayerPrefs.HasKey(ReceiveHighWatermarkPrefKey))
        {
            int savedReceiveHighWatermark = PlayerPrefs.GetInt(ReceiveHighWatermarkPrefKey, receiveHighWatermark);
            if (savedReceiveHighWatermark >= 1)
            {
                receiveHighWatermark = savedReceiveHighWatermark;
            }
        }

        if (PlayerPrefs.HasKey(SocketLingerMsPrefKey))
        {
            int savedSocketLingerMs = PlayerPrefs.GetInt(SocketLingerMsPrefKey, socketLingerMs);
            if (savedSocketLingerMs >= 0)
            {
                socketLingerMs = savedSocketLingerMs;
            }
        }

        if (PlayerPrefs.HasKey(ReceivePollTimeoutMsPrefKey))
        {
            int savedReceivePollTimeoutMs = PlayerPrefs.GetInt(ReceivePollTimeoutMsPrefKey, receivePollTimeoutMs);
            if (savedReceivePollTimeoutMs >= 1)
            {
                receivePollTimeoutMs = savedReceivePollTimeoutMs;
            }
        }
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private void ApplyLoadedConfigToTarget()
    {
        if (payloadReceiver == null)
        {
            return;
        }

        payloadReceiver.TryApplyConnectionConfig(serverIP, serverPort, useTopic, topic);
        payloadReceiver.TryApplyReceiveSettings(receiveHighWatermark, socketLingerMs, receivePollTimeoutMs);
        serverIP = payloadReceiver.CurrentServerIP;
        serverPort = payloadReceiver.CurrentServerPort;
        useTopic = payloadReceiver.UseTopic;
        topic = payloadReceiver.Topic;
        receiveHighWatermark = payloadReceiver.CurrentReceiveHighWatermark;
        socketLingerMs = payloadReceiver.CurrentSocketLingerMs;
        receivePollTimeoutMs = payloadReceiver.CurrentReceivePollTimeoutMs;
    }
}
