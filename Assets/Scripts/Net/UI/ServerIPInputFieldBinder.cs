using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class ServerIPInputFieldBinder : MonoBehaviour
{
    private const string SenderIPPrefKey = "PayloadSender.ServerIP";
    private const string ReceiverIPPrefKey = "PayloadReceiver.ServerIP";

    [Header("UI")]
    [SerializeField] private TMP_InputField inputField;

    private void Awake()
    {
        if (inputField == null)
        {
            inputField = GetComponent<TMP_InputField>();
        }

        RefreshInputFromPrefs();
    }

    public void RefreshInputFromPrefs()
    {
        if (inputField == null)
        {
            return;
        }

        string savedIP = GetSavedOrDefaultServerIP();
        if (!string.IsNullOrEmpty(savedIP))
        {
            inputField.SetTextWithoutNotify(savedIP);
        }
    }

    private string GetSavedOrDefaultServerIP()
    {
        string senderIP = Normalize(PlayerPrefs.GetString(SenderIPPrefKey, string.Empty));
        if (!string.IsNullOrEmpty(senderIP))
        {
            return senderIP;
        }

        string receiverIP = Normalize(PlayerPrefs.GetString(ReceiverIPPrefKey, string.Empty));
        if (!string.IsNullOrEmpty(receiverIP))
        {
            return receiverIP;
        }

        PayloadSender sender = FindFirstObjectByType<PayloadSender>();
        if (sender != null)
        {
            string senderDefaultIP = Normalize(sender.CurrentServerIP);
            if (!string.IsNullOrEmpty(senderDefaultIP))
            {
                return senderDefaultIP;
            }
        }

        PayloadReceiver receiver = FindFirstObjectByType<PayloadReceiver>();
        if (receiver != null)
        {
            string receiverDefaultIP = Normalize(receiver.CurrentServerIP);
            if (!string.IsNullOrEmpty(receiverDefaultIP))
            {
                return receiverDefaultIP;
            }
        }

        return string.Empty;
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }
}
