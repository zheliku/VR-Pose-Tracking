using RuntimeInspectorNamespace;
using UnityEngine;

[DisallowMultipleComponent]
public class RuntimeSenderConfigPanel : MonoBehaviour
{
    private const string SenderIPPrefKey = "PayloadSender.ServerIP";
    private const string SenderPortPrefKey = "PayloadSender.ServerPort";
    private const string TargetFpsPrefKey = "PayloadSender.TargetFps";
    private const string LogIntervalPrefKey = "PayloadSender.LogInterval";
    private const string SendHighWatermarkPrefKey = "PayloadSender.SendHighWatermark";
    private const string SocketLingerMsPrefKey = "PayloadSender.SocketLingerMs";

    [Header("Target")]
    [SerializeField] private PayloadSender payloadSender;

    [Header("Config")]
    [SerializeField] private string serverIP = "127.0.0.1";
    [SerializeField] private int serverPort = 5557;
    [Range(1, 90)]
    [SerializeField] private int targetFps = 60;
    [Range(1, 90)]
    [SerializeField] private int logInterval = 30;
    [Range(1, 100)]
    [SerializeField] private int sendHighWatermark = 1;
    [SerializeField] private int socketLingerMs = 0;

    private void Awake()
    {
        LoadFromPrefs();
        ApplyLoadedConfigToTarget();
    }

    [RuntimeInspectorButton("Apply Sender Config", false, ButtonVisibility.InitializedObjects)]
    public void SyncFromTarget()
    {
        if (payloadSender == null)
        {
            Debug.LogWarning("[RuntimeSenderConfigPanel] PayloadSender is not assigned.");
            return;
        }

        bool endpointApplied = payloadSender.TryApplyServerConfig(serverIP, serverPort);
        bool settingsApplied = payloadSender.TryApplySendSettings(targetFps, logInterval, sendHighWatermark, socketLingerMs);

        serverIP = payloadSender.CurrentServerIP;
        serverPort = payloadSender.CurrentServerPort;
        targetFps = payloadSender.CurrentTargetFps;
        logInterval = payloadSender.CurrentLogInterval;
        sendHighWatermark = payloadSender.CurrentSendHighWatermark;
        socketLingerMs = payloadSender.CurrentSocketLingerMs;

        if (endpointApplied && settingsApplied)
        {
            PlayerPrefs.SetString(SenderIPPrefKey, serverIP);
            PlayerPrefs.SetInt(SenderPortPrefKey, serverPort);
            PlayerPrefs.SetInt(TargetFpsPrefKey, targetFps);
            PlayerPrefs.SetInt(LogIntervalPrefKey, logInterval);
            PlayerPrefs.SetInt(SendHighWatermarkPrefKey, sendHighWatermark);
            PlayerPrefs.SetInt(SocketLingerMsPrefKey, socketLingerMs);
            PlayerPrefs.Save();
        }
    }

    // [RuntimeInspectorButton("Reload From Prefs", false, ButtonVisibility.InitializedObjects)]
    public void LoadFromPrefs()
    {
        if (payloadSender != null)
        {
            serverIP = payloadSender.CurrentServerIP;
            serverPort = payloadSender.CurrentServerPort;
            targetFps = payloadSender.CurrentTargetFps;
            logInterval = payloadSender.CurrentLogInterval;
            sendHighWatermark = payloadSender.CurrentSendHighWatermark;
            socketLingerMs = payloadSender.CurrentSocketLingerMs;
        }

        if (PlayerPrefs.HasKey(SenderIPPrefKey))
        {
            string savedIP = Normalize(PlayerPrefs.GetString(SenderIPPrefKey, serverIP));
            serverIP = savedIP;
        }

        if (PlayerPrefs.HasKey(SenderPortPrefKey))
        {
            int savedPort = PlayerPrefs.GetInt(SenderPortPrefKey, serverPort);
            if (savedPort is >= 1 and <= 65535)
            {
                serverPort = savedPort;
            }
        }

        if (PlayerPrefs.HasKey(TargetFpsPrefKey))
        {
            int savedTargetFps = PlayerPrefs.GetInt(TargetFpsPrefKey, targetFps);
            if (savedTargetFps is >= 1 and <= 90)
            {
                targetFps = savedTargetFps;
            }
        }

        if (PlayerPrefs.HasKey(LogIntervalPrefKey))
        {
            int savedLogInterval = PlayerPrefs.GetInt(LogIntervalPrefKey, logInterval);
            if (savedLogInterval >= 0)
            {
                logInterval = savedLogInterval;
            }
        }

        if (PlayerPrefs.HasKey(SendHighWatermarkPrefKey))
        {
            int savedSendHighWatermark = PlayerPrefs.GetInt(SendHighWatermarkPrefKey, sendHighWatermark);
            if (savedSendHighWatermark >= 1)
            {
                sendHighWatermark = savedSendHighWatermark;
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
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private void ApplyLoadedConfigToTarget()
    {
        if (payloadSender == null)
        {
            return;
        }

        payloadSender.TryApplyServerConfig(serverIP, serverPort);
        payloadSender.TryApplySendSettings(targetFps, logInterval, sendHighWatermark, socketLingerMs);
        serverIP = payloadSender.CurrentServerIP;
        serverPort = payloadSender.CurrentServerPort;
        targetFps = payloadSender.CurrentTargetFps;
        logInterval = payloadSender.CurrentLogInterval;
        sendHighWatermark = payloadSender.CurrentSendHighWatermark;
        socketLingerMs = payloadSender.CurrentSocketLingerMs;
    }
}
