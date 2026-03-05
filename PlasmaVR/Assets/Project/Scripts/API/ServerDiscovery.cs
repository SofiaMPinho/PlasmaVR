using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

[Serializable]
public class DiscoveredServer
{
    public string name;
    public string url;
    public string token;
}

/// <summary>
/// Discovers PlasmaVR servers on the local network via UDP broadcast.
/// Populates a dropdown with found servers and allows one-tap connection.
/// Also supports manual URL entry and persists the last-used URL via PlayerPrefs.
/// </summary>
public class ServerDiscovery : MonoBehaviour
{
    [Header("UI")]
    public TMP_Dropdown serversDropdown;
    public TMP_InputField serverUrlInput;
    public TextMeshProUGUI statusText;
    

    [Header("Discovery")]
    public int discoveryPort = 50000;
    public float listenDuration = 2.0f;

    private List<DiscoveredServer> discovered = new List<DiscoveredServer>();
    

    private const string PREF_KEY = "PlasmaVR_ServerURL";
    private const string PREF_KEY_NAME = "PlasmaVR_ServerName";

    void Start()
    {
        // Initialize dropdown with cached IP or default placeholder
        if (serversDropdown != null)
        {
            serversDropdown.ClearOptions();
            string cachedUrl = PlayerPrefs.GetString(PREF_KEY, "");
            if (!string.IsNullOrEmpty(cachedUrl))
            {
                string cachedName = PlayerPrefs.GetString(PREF_KEY_NAME, "");
                string displayName = !string.IsNullOrEmpty(cachedName) ? cachedName : cachedUrl;
                serversDropdown.AddOptions(new List<string> { displayName });
                serversDropdown.value = 0;
                if (serverUrlInput != null)
                    serverUrlInput.text = cachedUrl;
                UpdateStatus("Using cached server.");
            }
            else
            {
                serversDropdown.AddOptions(new List<string> { "Server" });
                UpdateStatus("Idle");
            }
        }

        // Wire up value-changed listener
        if (serversDropdown != null)
        {
            serversDropdown.onValueChanged.RemoveAllListeners();
            serversDropdown.onValueChanged.AddListener(OnDropdownValueChanged);
        }

        // Start discovery automatically on startup
        StartDiscovery();
    }

    public void StartDiscovery()
    {
        UpdateStatus("Discovering servers on LAN...");
        discovered.Clear();
        // Don't clear the dropdown yet — keep the cached entry visible while searching
        StartCoroutine(DiscoveryCoroutine());
    }

    private IEnumerator DiscoveryCoroutine()
    {
        UdpClient udp = null;
        List<string> seenUrls = new List<string>();

        // Initialize UDP socket — if this fails, exit coroutine
        try
        {
            udp = new UdpClient(0);
            udp.EnableBroadcast = true;
            udp.Client.ReceiveTimeout = 200; // ms

            byte[] msg = Encoding.UTF8.GetBytes("PLASMAVR_DISCOVER");
            IPEndPoint broadcastEP = new IPEndPoint(IPAddress.Broadcast, discoveryPort);
            udp.Send(msg, msg.Length, broadcastEP);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ServerDiscovery] Failed to start discovery socket: {ex}");
            UpdateStatus("Discovery error");
            if (udp != null)
            {
                try { udp.Close(); } catch { }
            }
            yield break;
        }

        float endTime = Time.time + listenDuration;

        while (Time.time < endTime)
        {
            // Try to receive responses
            try
            {
                IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = udp.Receive(ref remoteEP);
                string txt = Encoding.UTF8.GetString(data);
                // Parse JSON
                try
                {
                    DiscoveredServer ds = JsonUtility.FromJson<DiscoveredServer>(txt);
                    if (ds != null && !string.IsNullOrEmpty(ds.url) && !seenUrls.Contains(ds.url))
                    {
                        discovered.Add(ds);
                        seenUrls.Add(ds.url);
                        UpdateDropdown();
                        UpdateStatus($"Found {discovered.Count} server(s)");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ServerDiscovery] Failed to parse discovery response: {e}");
                }
            }
            catch (SocketException)
            {
                // timeout - just continue waiting
            }

            yield return null;
        }

        if (discovered.Count == 0)
            UpdateStatus("No servers found on LAN.");
        else
            UpdateStatus($"Discovery complete: {discovered.Count} server(s) found.");

        try
        {
            udp.Close();
        }
        catch { }
    }

    private void UpdateDropdown()
    {
        if (serversDropdown == null) return;
        var options = new List<string>();
        // Show only the server name/token in the dropdown
        foreach (var s in discovered)
            options.Add(s.name);
        serversDropdown.ClearOptions();
        serversDropdown.AddOptions(options);
        // Ensure the input field is populated for the current selection
        if (options.Count > 0)
        {
            // clamp value and invoke handler to populate the URL field
            serversDropdown.value = Mathf.Clamp(serversDropdown.value, 0, options.Count - 1);
            try
            {
                OnDropdownValueChanged(serversDropdown.value);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ServerDiscovery] Failed to populate URL on dropdown update: {ex}");
            }
        }
        else
        {
            if (serverUrlInput != null)
                serverUrlInput.text = string.Empty;
        }
    }

    // Connection is handled by DatasetSelectionUI and its buttons; discovery only sets the URL input.

    private void OnDropdownValueChanged(int idx)
    {
        if (idx < 0 || idx >= discovered.Count) return;
        var s = discovered[idx];
        if (serverUrlInput != null)
            serverUrlInput.text = s.url;
        PlayerPrefs.SetString(PREF_KEY, s.url);
        PlayerPrefs.SetString(PREF_KEY_NAME, s.name);
        PlayerPrefs.Save();
        UpdateStatus($"Selected {s.name}");
    }

    private void ApplyServerUrl(string url)
    {
        if (serverUrlInput != null)
            serverUrlInput.text = url;
        PlayerPrefs.SetString(PREF_KEY, url);
        PlayerPrefs.Save();
    }

    private void UpdateStatus(string s)
    {
        if (statusText != null)
            statusText.text = s;
        Debug.Log($"[ServerDiscovery] {s}");
    }
}
