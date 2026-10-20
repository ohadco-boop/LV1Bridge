using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static class Program
{
    sealed class KeyInfo { public string Kind = "aux"; public int Index = 1; }
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static readonly Dictionary<string, KeyInfo> Keys = new Dictionary<string, KeyInfo>();
    static readonly object Gate = new object(), RenderGate = new object(), SendGate = new object();
    static readonly Dictionary<string,string> Rendered = new Dictionary<string,string>();
    static int polling;
    static void Trace(string value) { try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(fontSettings),"deck-events.log"),DateTime.Now.ToString("HH:mm:ss.fff")+" "+value+Environment.NewLine); } catch {} }
    static string[] meterNames = new string[16];
    static int[] meterLevels = new int[32];
    static bool metersFresh;
    static bool[] meterMono=new bool[16];
    static int fontSize = 21;
    static readonly string fontSettings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LV1 Bridge", "deck-font-size.txt");
    static void LoadFontSize() { try { int value; if (Int32.TryParse(File.ReadAllText(fontSettings), out value)) fontSize = Math.Max(14, Math.Min(40, value)); } catch { } }
    static void ChangeFontSize(int step)
    {
        fontSize = Math.Max(14, Math.Min(40, fontSize + step));
        try { Directory.CreateDirectory(Path.GetDirectoryName(fontSettings)); File.WriteAllText(fontSettings, fontSize.ToString()); } catch { }
        RefreshAll();
    }
    static ClientWebSocket ws; static string pluginUuid, registerEvent;
    static int activeAux = -1; static string page = "aux"; static readonly string[] auxNames = new string[24], userNames = new string[16];

    static void Main(string[] args)
    {
        string port = Arg(args, "-port"), uuid = Arg(args, "-pluginUUID"); pluginUuid = uuid; registerEvent = Arg(args, "-registerEvent");
        if (String.IsNullOrEmpty(port) || String.IsNullOrEmpty(uuid)) return;
        for (int i = 0; i < auxNames.Length; i++) auxNames[i] = i < 16 ? "MON " + (i + 1) : "FX " + (i - 15);
        for (int i = 0; i < userNames.Length; i++) userNames[i] = "USER " + (i + 1);
        LoadFontSize();
        Run(Int32.Parse(port)).GetAwaiter().GetResult();
    }

    static async Task Run(int port)
    {
        ws = new ClientWebSocket(); await ws.ConnectAsync(new Uri("ws://127.0.0.1:" + port), CancellationToken.None);
        await Send(new Dictionary<string, object> { { "event", registerEvent }, { "uuid", pluginUuid } }); Trace("Registered");
        Timer poll = new Timer(delegate { RefreshState(); }, null, 100, 150);
        byte[] buffer = new byte[65536];
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                MemoryStream ms = new MemoryStream(); WebSocketReceiveResult rr;
                do { rr = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None); if (rr.MessageType == WebSocketMessageType.Close) return; ms.Write(buffer, 0, rr.Count); } while (!rr.EndOfMessage);
                Handle(Encoding.UTF8.GetString(ms.ToArray()));
            }
        }
        finally { poll.Dispose(); }
    }

    static void Handle(string json)
    {
        try
        {
            Dictionary<string, object> e = Json.Deserialize<Dictionary<string, object>>(json); string ev = Get(e, "event"); string context = Get(e, "context");
            if(ev=="keyDown" || ev=="willAppear") Trace(ev+" "+context);
            if (ev == "willDisappear") { lock (Gate) Keys.Remove(context); lock (RenderGate) Rendered.Remove(context); return; }
            if (ev != "willAppear" && ev != "keyDown" && ev != "didReceiveSettings") return;
            KeyInfo k = ParseSettings(e);
            lock (Gate) { if (k == null) Keys.TryGetValue(context, out k); if (k == null) return; Keys[context] = k; }
            if (ev == "willAppear") { lock (RenderGate) Rendered.Remove(context); }
            if (ev == "keyDown")
            {
                string kind; int index; Resolve(k, out kind, out index);
                Trace("Resolved "+kind+" "+index);
                if (kind == "metersPage") { page = page == "meters" ? "aux" : "meters"; RefreshState(); RefreshAll(); return; }
                if (kind == "page") { page = page == "aux" ? "user" : "aux"; RefreshAll(); return; }
                if (kind == "fontDown" || kind == "fontUp") { ChangeFontSize(kind == "fontUp" ? 2 : -2); return; }
                if (kind == "empty" || kind == "meter") return;
                string cmd = kind == "mix" ? "MIX" : kind == "user" ? "USER " + index : "AUX " + index;
                bool ok = Bridge(cmd).StartsWith("OK"); if (!ok) ShowAlert(context);
                // A short non-overlapping poll updates feedback; do not block the key event loop.
            }
            else UpdateKey(context, k);
        }
        catch (Exception ex) { Trace(ex.ToString()); }
    }

    static KeyInfo ParseSettings(Dictionary<string, object> e)
    {
        object po, so; if (!e.TryGetValue("payload", out po)) return null; Dictionary<string, object> p = po as Dictionary<string, object>;
        if (p == null || !p.TryGetValue("settings", out so)) return null; Dictionary<string, object> s = so as Dictionary<string, object>; if (s == null || !s.ContainsKey("kind") || !s.ContainsKey("index")) return null;
        KeyInfo k = new KeyInfo(); object o; if (s.TryGetValue("kind", out o)) k.Kind = Convert.ToString(o).ToLowerInvariant(); if (s.TryGetValue("index", out o)) Int32.TryParse(Convert.ToString(o), out k.Index); return k;
    }

    static void RefreshState()
    {
        if (Interlocked.Exchange(ref polling, 1) != 0) return;
        try
        {
            if (page == "meters")
            {
                string[] m = Bridge("METERS").Split('|');
                metersFresh = m.Length >= 50 && m[0] == "METERS" && m[1] == "1";
                if (m.Length >= 50 && m[0] == "METERS") for (int i=0;i<16;i++) { meterNames[i]=Unpack(m[2+i*3]); int.TryParse(m[3+i*3],out meterLevels[i*2]); int.TryParse(m[4+i*3],out meterLevels[i*2+1]); }
                for(int i=0;i<16;i++)meterMono[i]=m.Length>=66 && m[50+i]=="1";
                RefreshAll(); return;
            }
            string s = Bridge("STATE"); string[] p = s.Split('|'); if (p.Length < 42 || p[0] != "STATE") return;
            Int32.TryParse(p[1], out activeAux); for (int i = 0; i < 24; i++) auxNames[i] = Unpack(p[2 + i]); for (int i = 0; i < 16; i++) userNames[i] = Unpack(p[26 + i]);
            RefreshAll();
        }
        catch { }
        finally { Interlocked.Exchange(ref polling, 0); }
    }

    static void UpdateKey(string context, KeyInfo k)
    {
        string kind; int index; Resolve(k, out kind, out index); string title; int state = 0;
        if (kind == "meter") { UpdateMeter(context, index); return; }
        if (kind == "metersPage") title = page == "meters" ? "AUX\nSENDS" : "VU\nMETERS";
        else if (kind == "mix") { title = "MAIN\nMIX"; state = activeAux < 0 ? 1 : 0; }
        else if (kind == "page") { title = page == "aux" ? "USER\nKEYS" : "AUX\nSENDS"; }
        else if (kind == "fontDown") title = "A−\n" + fontSize;
        else if (kind == "fontUp") title = "A+\n" + fontSize;
        else if (kind == "empty") title = "";
        else if (kind == "user") title = index >= 1 && index <= 16 ? userNames[index - 1] : "USER " + index;
        else { title = index >= 1 && index <= 24 ? auxNames[index - 1] : "AUX " + index; state = activeAux == index - 1 ? 1 : 0; }
        string fingerprint = kind + "|" + title + "|" + state + "|" + fontSize;
        lock (RenderGate)
        {
            string previous;
            if (Rendered.TryGetValue(context, out previous) && previous == fingerprint) return;
            Send(new Dictionary<string, object> { { "event", "setTitle" }, { "context", context }, { "payload", new Dictionary<string, object> { { "title", "" }, { "target", 0 } } } });
            Send(new Dictionary<string, object> { { "event", "setImage" }, { "context", context }, { "payload", new Dictionary<string, object> { { "image", RenderImage(title, kind, state == 1) }, { "target", 0 } } } });
            Send(new Dictionary<string, object> { { "event", "setState" }, { "context", context }, { "payload", new Dictionary<string, object> { { "state", state } } } });
            Rendered[context] = fingerprint;
        }
    }

    static void UpdateMeter(string context, int index)
    {
        string name = meterNames[index] ?? ""; bool mono=meterMono[index];
        int left = metersFresh ? Math.Max(0,Math.Min(20,(meterLevels[index*2]*20+126)/127)) : 0;
        int right = metersFresh ? Math.Max(0,Math.Min(20,(meterLevels[index*2+1]*20+126)/127)) : 0;
        string fingerprint = "meter|"+name+"|"+left+"|"+right+"|"+metersFresh+"|"+fontSize+"|"+mono;
        lock(RenderGate)
        {
            string previous; Rendered.TryGetValue(context,out previous); if(previous==fingerprint)return;
            if(previous==null || !previous.StartsWith("meter|")) {
                Send(new { @event="setTitle",context=context,payload=new {title="",target=0} });
                Send(new { @event="setState",context=context,payload=new {state=0} });
            }
            var svg=new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' width='144' height='144'><rect width='144' height='144' rx='12' fill='#080f18'/>");
            for(int lane=0;lane<(mono?1:2);lane++) for(int n=0;n<20;n++) {
                string color=n<(lane==0?left:right) ? (n>=18?"#ef4444":n>=14?"#facc15":"#22c55e") : "#1b2935";
                svg.Append("<rect x='").Append(40+lane*34).Append("' y='").Append(108-n*4).Append("' width='").Append(mono?61:27).Append("' height='3' fill='").Append(color).Append("'/>");
            }
            int size=Math.Min(fontSize,30), width=Math.Max(5,120/(size/2));
            string label=name.Length>width ? name.Substring(0,width-1)+"…" : name;
            svg.Append("<text x='72' y='24' text-anchor='middle' fill='white' font-family='Arial' font-size='").Append(size).Append("'>").Append(Xml(label)).Append("</text>");
            svg.Append("<text x='72' y='132' text-anchor='middle' fill='#94a3b8' font-family='Arial' font-size='13'>").Append(metersFresh?(mono?"MONO · "+(index+1):"L · "+(index+1)+" · R"):"NO DATA").Append("</text></svg>");
            Send(new { @event="setImage",context=context,payload=new {image="data:image/svg+xml;base64,"+Convert.ToBase64String(Encoding.UTF8.GetBytes(svg.ToString())),target=0} });
            Rendered[context]=fingerprint;
        }
    }

    static string RenderImage(string title, string kind, bool active)
    {
        string bg = active ? "#14532d" : kind == "user" ? "#3b1d5c" : kind == "mix" ? "#713f12" : kind == "page" ? "#164e63" : kind == "empty" ? "#080b10" : "#172554";
        int size = kind == "fontDown" || kind == "fontUp" ? 27 : fontSize;
        int maxChars = Math.Max(4, (int)(120.0 / (size * 0.57)));
        var lines = new List<string>();
        foreach (string paragraph in (title ?? "").Split('\n'))
        {
            string rest = paragraph.Trim();
            while (rest.Length > maxChars)
            {
                int split = rest.LastIndexOf(' ', Math.Min(maxChars, rest.Length - 1));
                if (split < 1) split = maxChars;
                lines.Add(rest.Substring(0, split).Trim()); rest = rest.Substring(split).Trim();
            }
            lines.Add(rest);
        }
        if (lines.Count > 3) { lines.RemoveRange(3, lines.Count - 3); lines[2] = lines[2].TrimEnd() + "…"; }
        double lineHeight = size * 1.05;
        double firstY = 72.0 - (lines.Count - 1) * lineHeight / 2.0 + size * 0.34;
        var svg = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' width='144' height='144'><rect width='144' height='144' rx='18' fill='").Append(bg)
            .Append("'/><rect x='7' y='7' width='130' height='130' rx='14' fill='none' stroke='").Append(active ? "#4ade80" : "#38bdf8").Append("' stroke-width='4'/>");
        for (int i = 0; i < lines.Count; i++)
            svg.Append("<text x='72' y='").Append((firstY + i * lineHeight).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture))
               .Append("' text-anchor='middle' fill='white' font-family='Arial' font-size='").Append(size).Append("' font-weight='bold'>").Append(Xml(lines[i])).Append("</text>");
        svg.Append("</svg>");
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg.ToString()));
    }
    static string Xml(string s) { return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("'", "&apos;").Replace("\"", "&quot;"); }

    static void Resolve(KeyInfo k, out string kind, out int index)
    {
        kind = k.Kind; index = k.Index; if (kind != "slot") return; int slot = Math.Max(0, index);
        if (slot == 28) { kind = "metersPage"; index = 0; return; }
        if (page == "meters" && slot < 16) { kind = "meter"; index = slot; return; }
        if (slot == 24) { kind = "mix"; index = 0; return; }
        if (slot == 26) { kind = "fontDown"; index = 0; return; }
        if (slot == 27) { kind = "fontUp"; index = 0; return; }
        if (slot == 25) { kind = "page"; index = 0; return; }
        if (page == "aux" && slot < 24) { kind = "aux"; index = slot + 1; return; }
        if (page == "user" && slot < 16) { kind = "user"; index = slot + 1; return; }
        kind = "empty"; index = 0;
    }

    static void RefreshAll()
    {
        KeyValuePair<string, KeyInfo>[] all; lock (Gate) { all = new List<KeyValuePair<string, KeyInfo>>(Keys).ToArray(); }
        foreach (KeyValuePair<string, KeyInfo> x in all) UpdateKey(x.Key, x.Value);
    }

    static string Bridge(string cmd)
    {
        try { using (TcpClient c = new TcpClient()) { IAsyncResult ar = c.BeginConnect("127.0.0.1", 49631, null, null); if (!ar.AsyncWaitHandle.WaitOne(500)) return "ERR"; c.EndConnect(ar); c.ReceiveTimeout = c.SendTimeout = 800; using (NetworkStream s = c.GetStream()) using (StreamWriter w = new StreamWriter(s, new UTF8Encoding(false), 1024, true)) using (StreamReader r = new StreamReader(s, Encoding.UTF8, false, 1024, true)) { w.AutoFlush = true; w.WriteLine(cmd); return r.ReadLine() ?? "ERR"; } } } catch { return "ERR"; }
    }

    static Task Send(object value)
    {
        if (ws == null || ws.State != WebSocketState.Open) return Task.FromResult(0); byte[] b = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(value));
        lock (SendGate) ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, CancellationToken.None).GetAwaiter().GetResult(); return Task.FromResult(0);
    }
    static void ShowAlert(string context) { Send(new Dictionary<string, object> { { "event", "showAlert" }, { "context", context } }); }
    static string Unpack(string s) { try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); } catch { return ""; } }
    static string Fit(string s) { s = String.IsNullOrWhiteSpace(s) ? "-" : s.Trim(); return s.Length <= 11 ? s : s.Substring(0, 11); }
    static string Get(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) ? Convert.ToString(o) : ""; }
    static string Arg(string[] a, string name) { for (int i = 0; i + 1 < a.Length; i++) if (a[i] == name) return a[i + 1]; return ""; }
}
