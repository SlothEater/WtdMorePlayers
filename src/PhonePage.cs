using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace WtdMorePlayers
{
    // Serves a patched copy of the playwtd.com phone page from the game itself.
    // The page is downloaded from playwtd.com on the player's own PC at startup (it is not shipped with the mod),
    // then patched so the vote screen shows every dub instead of a fixed 6 buttons.
    internal static class PhonePage
    {
        // Set by the patched page so the host knows this phone can show every dub.
        internal const string FullVotesProperty = "WTD_FULL_VOTES";

        private const string SourceSite = "https://playwtd.com/";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Port;

        // "192.168.1.20:8080" once the server is up, otherwise null.
        internal static string Address { get; private set; }

        private static Dictionary<string, byte[]> _files;
        private static TcpListener _listener;
        private static float _nextLabelCheck;

        private static string CacheDir => Path.Combine(Paths.CachePath, "WtdMorePlayers-phone-page");

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("PhonePage", "Enabled", true,
                "Serve a patched copy of the phone page from this PC so the vote screen can show every dub. " +
                "Players join at the address shown on the TV instead of playwtd.com.");
            Port = config.Bind("PhonePage", "Port", 8080, "Port for the local phone page.");
        }

        internal static IEnumerator Run()
        {
            var files = new Dictionary<string, byte[]>();
            yield return Fetch("index.html", files);
            if (!files.TryGetValue("index.html", out var indexBytes))
            {
                Plugin.Log.LogError("Phone page: could not get index.html (offline and no cache). Players should use playwtd.com.");
                yield break;
            }

            string index = Encoding.UTF8.GetString(indexBytes);
            foreach (var path in LocalReferences(index))
                yield return Fetch(path, files);
            foreach (var css in files.Keys.Where(k => k.EndsWith(".css")).ToList())
            {
                foreach (var path in CssReferences(css, Encoding.UTF8.GetString(files[css])))
                    yield return Fetch(path, files);
            }

            if (!Patch(files))
                yield break;

            try
            {
                StartServer(files);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Phone page: could not start server on port {Port.Value}: {e.Message}");
            }
        }

        // Relative src/href paths in the page, e.g. "js/script.js?version=2" -> "js/script.js".
        private static IEnumerable<string> LocalReferences(string html)
        {
            foreach (Match m in Regex.Matches(html, "(?:src|href)=\"([^\"]+)\""))
            {
                string p = CleanPath(m.Groups[1].Value);
                if (p != null && !IsCommentedOut(html, m.Index))
                    yield return p;
            }
        }

        private static bool IsCommentedOut(string html, int index)
        {
            int open = html.LastIndexOf("<!--", index, StringComparison.Ordinal);
            int close = html.LastIndexOf("-->", index, StringComparison.Ordinal);
            return open > close;
        }

        private static IEnumerable<string> CssReferences(string cssPath, string css)
        {
            string dir = Path.GetDirectoryName(cssPath)?.Replace('\\', '/') ?? "";
            foreach (Match m in Regex.Matches(css, @"url\(\s*['""]?([^'"")]+)['""]?\s*\)"))
            {
                string p = CleanPath(m.Groups[1].Value);
                if (p == null)
                    continue;
                // Resolve "../assets/x.png" relative to the stylesheet.
                var parts = new List<string>(dir.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries));
                foreach (var seg in p.Split('/'))
                {
                    if (seg == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
                    else if (seg != ".") parts.Add(seg);
                }
                yield return string.Join("/", parts);
            }
        }

        private static string CleanPath(string raw)
        {
            if (raw.Contains(":") || raw.StartsWith("//") || raw.StartsWith("#") || raw.StartsWith("data"))
                return null;
            string p = raw.Split('?', '#')[0].TrimStart('/');
            return p.Length == 0 ? null : p;
        }

        // Downloads a file from playwtd.com, falling back to the last cached copy when offline.
        private static IEnumerator Fetch(string path, Dictionary<string, byte[]> files)
        {
            if (files.ContainsKey(path))
                yield break;
            string cacheFile = Path.Combine(CacheDir, path.Replace('/', Path.DirectorySeparatorChar));
            using (var req = UnityWebRequest.Get(SourceSite + path))
            {
                yield return req.SendWebRequest();
                if (!req.isNetworkError && !req.isHttpError && req.downloadHandler.data != null)
                {
                    files[path] = req.downloadHandler.data;
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));
                        File.WriteAllBytes(cacheFile, files[path]);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"Phone page: could not cache {path}: {e.Message}");
                    }
                    yield break;
                }
                if (File.Exists(cacheFile))
                {
                    files[path] = File.ReadAllBytes(cacheFile);
                    Plugin.Log.LogWarning($"Phone page: using cached {path} ({req.error})");
                }
                else
                {
                    Plugin.Log.LogWarning($"Phone page: could not get {path} ({req.error})");
                }
            }
        }

        private static bool Patch(Dictionary<string, byte[]> files)
        {
            const string scriptPath = "js/script.js";
            if (!files.TryGetValue(scriptPath, out var scriptBytes))
            {
                Plugin.Log.LogError("Phone page: js/script.js missing, not serving the local page.");
                return false;
            }
            string script = Encoding.UTF8.GetString(scriptBytes);

            var replacements = new[]
            {
                ("votingButtons[index].innerText", "getVotingButton(index).innerText"),
                ("votingButtons[index].style.display = 'block'", "getVotingButton(index).style.display = 'block'"),
                ("client.myActor().setCustomProperty(PLAYER_ID_KEY, getMyPlayerId());",
                    "client.myActor().setCustomProperty(PLAYER_ID_KEY, getMyPlayerId());\n\tclient.myActor().setCustomProperty('" + FullVotesProperty + "', true);"),
                ("function setupVoting(toVoteOn){", GetVotingButtonJs + "function setupVoting(toVoteOn){"),
            };
            foreach (var (from, to) in replacements)
            {
                if (!script.Contains(from))
                {
                    Plugin.Log.LogError($"Phone page: playwtd.com page has changed ('{from}' not found). Not serving the local page; players should use playwtd.com.");
                    return false;
                }
                script = script.Replace(from, to);
            }
            files[scriptPath] = Encoding.UTF8.GetBytes(script);

            // Drop the analytics tag from the local copy.
            string index = Encoding.UTF8.GetString(files["index.html"]);
            index = Regex.Replace(index, @"(?s)\s*<!-- Global site tag \(gtag\.js\).*?</script>\s*<script>.*?</script>", "");
            files["index.html"] = Encoding.UTF8.GetBytes(index);
            return true;
        }

        private const string GetVotingButtonJs =
            "// Added by WTD More Players: the original page has 6 vote buttons, clone more on demand.\n" +
            "function getVotingButton(i){\n" +
            "\twhile(votingButtons.length <= i){\n" +
            "\t\tlet n = votingButtons.length;\n" +
            "\t\tlet b = votingButtons[0].cloneNode(true);\n" +
            "\t\tb.id = 'voteButton' + n;\n" +
            "\t\tb.setAttribute('onclick', 'voteSelected(' + n + ')');\n" +
            "\t\tvotingButtons[n - 1].after(b);\n" +
            "\t\tvotingButtons.push(b);\n" +
            "\t}\n" +
            "\treturn votingButtons[i];\n" +
            "}\n\n";

        private static void StartServer(Dictionary<string, byte[]> files)
        {
            _files = files;
            _listener = new TcpListener(IPAddress.Any, Port.Value);
            _listener.Start();
            var thread = new Thread(AcceptLoop) { IsBackground = true, Name = "WtdPhonePage" };
            thread.Start();
            Address = LanAddress() + ":" + Port.Value;
            Plugin.Log.LogInfo($"Phone page ready: players join at http://{Address}");
        }

        private static void AcceptLoop()
        {
            while (true)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    return;
                }
                ThreadPool.QueueUserWorkItem(_ => Serve(client));
            }
        }

        private static void Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    stream.ReadTimeout = 5000;
                    var buffer = new byte[8192];
                    int read = 0;
                    while (read < buffer.Length)
                    {
                        int n = stream.Read(buffer, read, buffer.Length - read);
                        if (n <= 0)
                            break;
                        read += n;
                        if (Encoding.ASCII.GetString(buffer, 0, read).Contains("\r\n\r\n"))
                            break;
                    }
                    string requestLine = Encoding.ASCII.GetString(buffer, 0, read).Split('\n')[0];
                    var parts = requestLine.Split(' ');
                    string path = parts.Length > 1 ? Uri.UnescapeDataString(parts[1].Split('?')[0]).TrimStart('/') : "";
                    if (path.Length == 0 || path.EndsWith("/"))
                        path += "index.html";

                    if (_files.TryGetValue(path, out var body))
                        Respond(stream, "200 OK", ContentType(path), body);
                    else
                        Respond(stream, "404 Not Found", "text/plain", Encoding.ASCII.GetBytes("Not found"));
                }
                catch (Exception)
                {
                    // Phones drop connections all the time; nothing to do.
                }
            }
        }

        private static void Respond(Stream stream, string status, string contentType, byte[] body)
        {
            string headers = $"HTTP/1.1 {status}\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n" +
                             "Cache-Control: no-cache\r\nConnection: close\r\n\r\n";
            var head = Encoding.ASCII.GetBytes(headers);
            stream.Write(head, 0, head.Length);
            stream.Write(body, 0, body.Length);
        }

        private static string ContentType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": return "text/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".png": return "image/png";
                case ".jpg":
                case ".jpeg": return "image/jpeg";
                case ".svg": return "image/svg+xml";
                case ".gif": return "image/gif";
                default: return "application/octet-stream";
            }
        }

        // The address other devices on the network can reach (the interface used for the default route).
        private static string LanAddress()
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect("8.8.8.8", 65530); // UDP connect sends nothing, it only picks the route.
                    return ((IPEndPoint)s.LocalEndPoint).Address.ToString();
                }
            }
            catch (Exception)
            {
                return Dns.GetHostEntry(Dns.GetHostName()).AddressList
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? "127.0.0.1";
            }
        }

        // Swap "playwtd.com" on the TV for the local address, so players go to the patched page.
        internal static void UpdateLabels()
        {
            if (Address == null || Time.unscaledTime < _nextLabelCheck)
                return;
            _nextLabelCheck = Time.unscaledTime + 1f;
            foreach (var text in UnityEngine.Object.FindObjectsOfType<TMP_Text>())
            {
                string t = text.text;
                if (string.IsNullOrEmpty(t) || t.IndexOf("playwtd.com", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                // Leave real playwtd.com links (e.g. playwtd.com/mod) alone; only the bare join address changes.
                text.text = Regex.Replace(t, @"playwtd\.com(?!/)", Address, RegexOptions.IgnoreCase);
            }
        }

        internal static void Stop()
        {
            try { _listener?.Stop(); } catch (Exception) { }
        }
    }
}
