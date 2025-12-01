using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace RRemote.Roku
{

    // https://developer.roku.com/docs/developer-program/dev-tools/external-control-api.md

    public class RokuClient
    {
        public const string DISCOVER_MESSAGE = "M-SEARCH * HTTP/1.1\r\n" +
                                               "HOST: {0}:{1}\r\n" +
                                               "ST:roku:ecp\r\n" +
                                               "MAN:\"ssdp:discover\"\r\n";

        public const string LOCATION_PATTERN = @"location:\s?(?<address>http://[^/]+/)";


        // ECP Key Commands - Complete list from Roku ECP documentation
        public static class Keys
        {
            // Navigation Keys
            public const string Home = "Home";
            public const string Back = "Back";
            public const string Up = "Up";
            public const string Down = "Down";
            public const string Left = "Left";
            public const string Right = "Right";
            public const string Select = "Select";

            // Playback Keys
            public const string Play = "Play";
            public const string Pause = "Pause";
            public const string PlayPause = "PlayPause";
            public const string Stop = "Stop";
            public const string Replay = "Replay";
            public const string InstantReplay = "InstantReplay";
            public const string Rev = "Rev";
            public const string Fwd = "Fwd";

            // Audio Keys
            public const string VolumeUp = "VolumeUp";
            public const string VolumeDown = "VolumeDown";
            public const string VolumeMute = "VolumeMute";

            // Power Key
            public const string Power = "Power";
            public const string PowerOff = "PowerOff";
            public const string PowerOn = "PowerOn";

            // Special Keys
            public const string Info = "Info";
            public const string Search = "Search";
            public const string Enter = "Enter";
            public const string FindRemote = "FindRemote";

            // Channel Keys
            public const string ChannelUp = "ChannelUp";
            public const string ChannelDown = "ChannelDown";

            // Input Keys
            public const string InputTuner = "InputTuner";
            public const string InputHDMI1 = "InputHDMI1";
            public const string InputHDMI2 = "InputHDMI2";
            public const string InputHDMI3 = "InputHDMI3";
            public const string InputHDMI4 = "InputHDMI4";
            public const string InputAV1 = "InputAV1";

            // Backlight Key (for remotes with backlight)
            public const string Backlight = "Backlight";

            // Gaming Keys (for gaming remotes)
            public const string A = "A";
            public const string B = "B";
        }

        // Simple logging method that can be easily replaced with a proper logging framework
        private static void LogError(string methodName, Exception ex)
        {
            Debug.WriteLine($"[RokuClient.{methodName}] Error: {ex.Message}");
            Debug.WriteLine($"[RokuClient.{methodName}] Stack Trace: {ex.StackTrace}");
        }

        private static void LogWarning(string methodName, string message)
        {
            Debug.WriteLine($"[RokuClient.{methodName}] Warning: {message}");
        }

        private static void LogInfo(string methodName, string message)
        {
            Debug.WriteLine($"[RokuClient.{methodName}] Info: {message}");
        }

        //Get a list of Available Roku Devices
        public static async Task Discover(Action<RokuDevice> deviceDiscovered,
            string ip = "239.255.255.250",
            int port = 1900, int waitSeconds = 15)
        {
            try
            {
                List<Task> tasks = new List<Task>();
                NetworkInterface[] nics = GetUsableNics();
                foreach (var nic in nics)
                {
                    foreach (var addr in nic.GetIPProperties().UnicastAddresses
                        .Where(x => x.Address.AddressFamily == AddressFamily.InterNetwork))
                    {
                        IPEndPoint ep = new IPEndPoint(addr.Address, 0);
                        tasks.Add(Task.Run(() =>
                        {
                            DiscoverHelp(deviceDiscovered, ep, ip, port, waitSeconds);
                        }));
                    }
                }
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                LogError(nameof(Discover), ex);
            }
        }

        private static async Task DiscoverHelp(Action<RokuDevice> deviceDiscovered,
            IPEndPoint ep,
            string ip = "239.255.255.250",
            int port = 1900, int waitSeconds = 15)
        {
            try
            {
                var token = new CancellationTokenSource();

                var mcEndpoint = new IPEndPoint(IPAddress.Parse(ip), port);
                var data = string.Format(DISCOVER_MESSAGE, ip, port);

                var discoverBytes = Encoding.UTF8.GetBytes(data);

                var startTime = DateTime.Now;
                token.CancelAfter(TimeSpan.FromSeconds(waitSeconds));

                using (var udp = new UdpClient(ep))
                {
                    await udp.SendAsync(discoverBytes, discoverBytes.Length, mcEndpoint);

                    while (!token.IsCancellationRequested)
                    {
                        var operation = udp.ReceiveAsync();

                        try
                        {
                            operation.Wait(token.Token); //Wait for result until cancelled
                        }
                        catch (OperationCanceledException)
                        {
                            // This is expected when the timeout expires - no devices found or discovery complete
                            LogWarning(nameof(DiscoverHelp), "Discovery timeout reached or cancelled");
                            break;
                        }

                        if (operation.IsCompleted && !operation.IsFaulted)
                        {
                            var result = operation.Result;
                            var deviceResponse = Encoding.ASCII.GetString(result.Buffer);

                            var match = Regex.Match(deviceResponse, LOCATION_PATTERN, RegexOptions.IgnoreCase);
                            if (match.Success && deviceResponse.ToLower().Contains("roku"))
                            {
                                var address = match.Groups["address"].Value;
                                // Don't allow device info request to hold up discovering additional devices
                                // Fire and forget the device reading operation
                                _ = Task.Run(() => ReadDevice(deviceDiscovered, address));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError(nameof(DiscoverHelp), ex);
            }
        }


        //Get additional device info
        public static async Task ReadDevice(Action<RokuDevice> deviceDiscovered, string endpoint, bool isStatic = false)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10); // Add timeout to prevent hanging

                var requestUrl = endpoint + "query/device-info";
                using var result = await client.GetStreamAsync(requestUrl);

                var serializer = new XmlSerializer(typeof(RokuDevice));
                var device = (RokuDevice?)serializer.Deserialize(result);

                if (device != null)
                {
                    device.Endpoint = endpoint;
                    device.IsStatic = isStatic;
                    deviceDiscovered?.Invoke(device);
                }
                else
                {
                    LogWarning(nameof(ReadDevice), $"Failed to deserialize device info from {endpoint}");
                }
            }
            catch (Exception ex)
            {
                LogError(nameof(ReadDevice), ex);
            }
        }

        //Get a list of apps installed on a device
        public static async Task<IEnumerable<RokuApp>> ListDeviceApps(string endpoint)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                var requestUrl = endpoint + "query/apps";
                using var result = await client.GetStreamAsync(requestUrl);

                var serializer = new XmlSerializer(typeof(DeviceAppWrapper));
                var wrapper = (DeviceAppWrapper?)serializer.Deserialize(result);

                return wrapper?.Apps ?? new List<RokuApp>();
            }
            catch (Exception ex)
            {
                LogError(nameof(ListDeviceApps), ex);
                return new List<RokuApp>();
            }
        }

        //Get the current running app
        public static async Task<RokuActiveApp?> GetCurrentApp(string endpoint)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                var requestUrl = endpoint + "query/active-app";
                using var result = await client.GetStreamAsync(requestUrl);

                var serializer = new XmlSerializer(typeof(RokuActiveApp));
                return (RokuActiveApp?)serializer.Deserialize(result);
            }
            catch (Exception ex)
            {
                LogError(nameof(GetCurrentApp), ex);
                return null;
            }
        }

        //Get media player info (if playing media)
        public static async Task<string?> GetMediaPlayer(string endpoint)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                var requestUrl = endpoint + "query/media-player";
                var response = await client.GetAsync(requestUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                return null;
            }
            catch (Exception ex)
            {
                LogError(nameof(GetMediaPlayer), ex);
                return null;
            }
        }

        //Get TV channels (for Roku TV)
        public static async Task<string?> GetTvChannels(string endpoint)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                var requestUrl = endpoint + "query/tv-channels";
                var response = await client.GetAsync(requestUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                return null;
            }
            catch (Exception ex)
            {
                LogError(nameof(GetTvChannels), ex);
                return null;
            }
        }

        //Get TV active channel (for Roku TV)
        public static async Task<string?> GetTvActiveChannel(string endpoint)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                var requestUrl = endpoint + "query/tv-active-channel";
                var response = await client.GetAsync(requestUrl);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                return null;
            }
            catch (Exception ex)
            {
                LogError(nameof(GetTvActiveChannel), ex);
                return null;
            }
        }

        //Launch an app with the app id returned from the app listing
        public static async Task<bool> LaunchApp(string endpoint, string id, Dictionary<string, string>? contentMetadata = null)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                var requestUrl = $"{endpoint}launch/{id}";
                
                HttpContent? content = null;
                if (contentMetadata != null && contentMetadata.Any())
                {
                    // Add content metadata for deep linking
                    var formData = new List<KeyValuePair<string, string>>();
                    foreach (var kvp in contentMetadata)
                    {
                        formData.Add(new KeyValuePair<string, string>(kvp.Key, kvp.Value));
                    }
                    content = new FormUrlEncodedContent(formData);
                }

                var response = await client.PostAsync(requestUrl, content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                LogError(nameof(LaunchApp), ex);
                return false;
            }
        }

        //Install an app from the Roku Channel Store
        public static async Task<bool> InstallApp(string endpoint, string channelId)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30); // Installation may take longer

                var requestUrl = $"{endpoint}install/{channelId}";
                var response = await client.PostAsync(requestUrl, null);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                LogError(nameof(InstallApp), ex);
                return false;
            }
        }

        //Launch a TV channel (for Roku TV)
        public static async Task<bool> LaunchTvChannel(string endpoint, string channelNumber)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                var requestUrl = $"{endpoint}launch/tvinput.dtv";
                var formData = new List<KeyValuePair<string, string>>
                {
                    new("ch", channelNumber)
                };
                var content = new FormUrlEncodedContent(formData);

                var response = await client.PostAsync(requestUrl, content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                LogError(nameof(LaunchTvChannel), ex);
                return false;
            }
        }

        //Emulate the roku remote by sending a key command
        //Reference here: https://developer.roku.com/docs/developer-program/dev-tools/external-control-api.md
        public static async Task<bool> PressKey(string endpoint, string key)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(5);

                var requestUrl = $"{endpoint}keypress/{key}";
                var response = await client.PostAsync(requestUrl, null);

                LogInfo(nameof(PressKey), $"Sent key '{key}' to {endpoint} - Status: {response.StatusCode}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                LogError(nameof(PressKey), ex);
                return false;
            }
        }

        //Send multiple key presses in sequence with optional delays
        public static async Task<bool> PressKeySequence(string endpoint, IEnumerable<string> keys, int delayMs = 100)
        {
            try
            {
                bool allSuccessful = true;
                foreach (var key in keys)
                {
                    var result = await PressKey(endpoint, key);
                    if (!result) allSuccessful = false;
                    
                    if (delayMs > 0)
                        await Task.Delay(delayMs);
                }
                return allSuccessful;
            }
            catch (Exception ex)
            {
                LogError(nameof(PressKeySequence), ex);
                return false;
            }
        }

        //Send text input (useful for search fields)
        public static async Task<bool> SendText(string endpoint, string text)
        {
            try
            {
                bool allSuccessful = true;
                foreach (char c in text)
                {
                    string key = c switch
                    {
                        ' ' => "Lit_%20", // Space
                        >= 'a' and <= 'z' => $"Lit_{Uri.EscapeDataString(c.ToString())}",
                        >= 'A' and <= 'Z' => $"Lit_{Uri.EscapeDataString(c.ToString())}",
                        >= '0' and <= '9' => $"Lit_{c}",
                        _ => $"Lit_{Uri.EscapeDataString(c.ToString())}"
                    };
                    
                    var result = await PressKey(endpoint, key);
                    if (!result) allSuccessful = false;
                    
                    await Task.Delay(50); // Small delay between characters
                }
                return allSuccessful;
            }
            catch (Exception ex)
            {
                LogError(nameof(SendText), ex);
                return false;
            }
        }

        //Search for content using the global search
        public static async Task<bool> Search(string endpoint, string query)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                var requestUrl = $"{endpoint}search/browse";
                var formData = new List<KeyValuePair<string, string>>
                {
                    new("keyword", query)
                };
                var content = new FormUrlEncodedContent(formData);

                var response = await client.PostAsync(requestUrl, content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                LogError(nameof(Search), ex);
                return false;
            }
        }

        public static NetworkInterface[] GetUsableNics()
        {
            try
            {
                List<NetworkInterface> ret = new List<NetworkInterface>();
                NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();

                foreach (NetworkInterface adapter in nics)
                {
                    switch (adapter.NetworkInterfaceType)
                    {
                        case NetworkInterfaceType.Ethernet:
                        case NetworkInterfaceType.Ethernet3Megabit:
                        case NetworkInterfaceType.FastEthernetFx:
                        case NetworkInterfaceType.FastEthernetT:
                        case NetworkInterfaceType.GigabitEthernet:
                        case NetworkInterfaceType.Wireless80211:
                            break;
                        default:
                            continue;
                    }

                    IPInterfaceProperties ip_properties = adapter.GetIPProperties();
                    if (!adapter.GetIPProperties().MulticastAddresses.Any())
                        continue; // most of VPN adapters will be skipped
                    if (!adapter.SupportsMulticast)
                        continue; // multicast is meaningless for this type of connection
                    if (OperationalStatus.Up != adapter.OperationalStatus)
                        continue; // this adapter is off or not connected

                    IPv4InterfaceProperties? p = adapter.GetIPProperties().GetIPv4Properties();
                    if (null == p)
                        continue; // IPv4 is not configured on this adapter

                    ret.Add(adapter);
                }
                return ret.ToArray();
            }
            catch (Exception ex)
            {
                LogError(nameof(GetUsableNics), ex);
                return Array.Empty<NetworkInterface>();
            }
        }
    }
}