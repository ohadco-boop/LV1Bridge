using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Windows.Forms;
using System.IO.Compression;

internal static class Program
{
    const string PluginFile = "LV1Bridge_Control.dll";
    const string BridgeExe = "FaderBankXTouchBridge.exe";
    const string CPVMidi = "CPVMidi.dll";
    const string CPoEth = "CPoEthAPI.dll";
    const string StartupLink = "Fader Bank Bridge.lnk";
    const string StartupResource = "FaderBankBridgeStartup.lnk";
    const string OriginalRel = @"Modules\FaderBank_Control.dll";
    const string BridgeRel = @"Modules\LV1Bridge_Control.dll";
    static bool noLaunch;
    static void Stage(string text) { File.AppendAllText(Path.Combine(Path.GetTempPath(),"LV1BridgeSurface-progress.log"),DateTime.Now.ToString("s")+" "+text+Environment.NewLine); }

    [STAThread]
    static int Main(string[] args)
    {
        bool silent = Array.Exists(args, a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));
        try
        {
            noLaunch=Array.Exists(args,a=>a=="--no-launch");
            Stage("Starting repair");
            bool elevatedChild = Array.Exists(args, a => a.Equals("--elevated", StringComparison.OrdinalIgnoreCase));
            if (!elevatedChild)
            {
                string forwarded = "--elevated " + string.Join(" ", args);
                var psi = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, forwarded)
                { UseShellExecute = true, Verb = "runas" };
                Process.Start(psi);
                return 0;
            }
            Stage("Checking LV1 and stopping Bridge"); StopLV1();
            StopBridge();
            string modules = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Waves", "eMotion LV1", "Modules");
            string pref = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Waves Audio", "Preferences", "LV1.Mixer SoundGrid.preferences");
            if (!Directory.Exists(modules)) throw new DirectoryNotFoundException(modules);
            if (!File.Exists(pref)) throw new FileNotFoundException("LV1 preferences not found", pref);
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LV1 Bridge Surface");
            string backup = Path.Combine(root, "Backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(backup);
            File.Copy(pref, Path.Combine(backup, Path.GetFileName(pref)), true);
            string installed = Path.Combine(modules, PluginFile);
            if (File.Exists(installed)) File.Copy(installed, Path.Combine(backup, PluginFile), true);
            Stage("Installing module and preferences"); ExtractPlugin(installed);
            ExtractResource("LV1BridgeBackend.dll", Path.Combine(modules, "LV1BridgeSupport", "LV1BridgeBackend.dll"));
            UpdatePreferences(pref);
            string bridgeDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LV1 FaderBank X-Touch Bridge");
            Directory.CreateDirectory(bridgeDirectory);
            Stage("Installing Bridge files"); ExtractResource(BridgeExe, Path.Combine(bridgeDirectory, BridgeExe));
            ExtractResource(CPVMidi, Path.Combine(bridgeDirectory, CPVMidi));
            ExtractResource(CPoEth, Path.Combine(bridgeDirectory, CPoEth));
            string startup = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupLink);
            ExtractResource(StartupResource, startup);
            Stage("Installing Stream Deck plugin and profile"); InstallDeck(root, backup);
            File.WriteAllText(Path.Combine(root, "install.log"), DateTime.Now.ToString("s") + " Installed " + installed + Environment.NewLine + "Backup " + backup);
            if (!noLaunch) { Stage("Starting applications"); StartBridge(bridgeDirectory); System.Threading.Thread.Sleep(3000); StartLV1(); }
            Stage("Completed");
            if (!silent) MessageBox.Show("Bridge module installed.\n\nAdd LV1 Bridge to Controls in any session. The module binds MIDI automatically.\nConnection still needs to be verified in LV1.", "LV1 Bridge Surface", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "LV1BridgeSurface-error.log"), ex.ToString()); } catch { }
            if (!silent) MessageBox.Show(ex.Message, "LV1 Bridge Surface - Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    static bool IsAdministrator()
    {
        using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    static void InstallDeck(string root, string backup)
    {
        string deckApp=null;
        foreach(var p in Process.GetProcessesByName("StreamDeck")) { try {deckApp=p.MainModule.FileName;p.Kill();p.WaitForExit(5000);} finally {p.Dispose();} }
        foreach(var p in Process.GetProcessesByName("LV1StreamDeckPlugin")) {try {p.Kill();p.WaitForExit(5000);} catch {} finally {p.Dispose();}}
        string destination=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Elgato","StreamDeck");
        try {
            using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("DeckPayload.zip"))
            using(var zip=new ZipArchive(input,ZipArchiveMode.Read))
            foreach(var entry in zip.Entries) {
                string path=Path.GetFullPath(Path.Combine(destination,entry.FullName.Replace('/',Path.DirectorySeparatorChar)));
                if(!path.StartsWith(Path.GetFullPath(destination)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Invalid payload path");
                if(entry.FullName.EndsWith("/"))continue;
                if(entry.FullName.StartsWith("ProfilesV3/") && File.Exists(path))continue;
                if(File.Exists(path)) {string saved=Path.Combine(backup,"Deck",entry.FullName.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(saved));File.Copy(path,saved,true);}
                Directory.CreateDirectory(Path.GetDirectoryName(path));using(var source=entry.Open())using(var target=File.Create(path))source.CopyTo(target);
            }
            ExtractResource("DeckPluginPackage",Path.Combine(root,"LV1-Fader-Bank-Bridge.streamDeckPlugin"));
            ExtractResource("DeckProfilePackage",Path.Combine(root,"LV1-Fader-Bank-XL.streamDeckProfile"));
        }
        finally {if(!noLaunch && !String.IsNullOrEmpty(deckApp))Process.Start(new ProcessStartInfo(deckApp){UseShellExecute=true});}
    }

    static void StopLV1()
    {
        var processes = Process.GetProcessesByName("eMotion LV1");
        bool running = processes.Length != 0;
        foreach (var process in processes) process.Dispose();
        if (running) throw new InvalidOperationException("Close LV1 before installing the Bridge module. No files were changed.");
    }

    static void StartLV1()
    {
        string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Waves", "eMotion LV1", "eMotion LV1.exe");
        if (File.Exists(exe)) Process.Start(exe);
    }

    static void StopBridge()
    {
        foreach (var p in Process.GetProcessesByName("FaderBankXTouchBridge"))
        {
            try { p.Kill(); p.WaitForExit(5000); } catch { }
        }
    }

    static void StartBridge(string directory)
    {
        string exe = Path.Combine(directory, BridgeExe);
        if (!File.Exists(exe)) throw new FileNotFoundException("LV1 Bridge executable was not installed", exe);
        Process.Start(new ProcessStartInfo(exe, "--background") { WorkingDirectory = directory, UseShellExecute = true });
    }

    static void ExtractPlugin(string destination)
    {
        ExtractResource(PluginFile, destination);
    }

    static void ExtractResource(string resourceName, string destination)
    {
        using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
        {
            if (input == null) throw new InvalidOperationException("Embedded bridge resource is missing: " + resourceName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temp = destination + ".new";
            using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) input.CopyTo(output);
            if (File.Exists(destination)) File.Delete(destination);
            File.Move(temp, destination);
        }
    }

    static XmlElement Child(XmlNode node, string name)
    {
        return node.SelectSingleNode(name) as XmlElement;
    }

    static void SetChild(XmlDocument doc, XmlNode node, string name, string value)
    {
        XmlElement e = Child(node, name);
        if (e == null) { e = doc.CreateElement(name); node.AppendChild(e); }
        e.InnerText = value;
    }

    static string Rel(XmlNode node)
    {
        var e = Child(node, "RelativePathFromApp"); return e == null ? "" : e.InnerText;
    }

    static void UpdatePreferences(string path)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.Load(path);
        XmlNode original = null;
        foreach (XmlNode n in doc.SelectNodes("//SurfaceDriver"))
            if (Rel(n).Equals(OriginalRel, StringComparison.OrdinalIgnoreCase)) { original = n; break; }
        // A Modular installation may contain only our Bridge registration.
        // Preserve that registration and its assignments when repairing files.
        foreach (XmlNode n in doc.SelectNodes("//SurfaceDriver"))
            if (Rel(n).Equals(BridgeRel, StringComparison.OrdinalIgnoreCase)) return;
        foreach (XmlNode n in doc.SelectNodes("//SurfaceDriver"))
            if (Rel(n).Equals(BridgeRel, StringComparison.OrdinalIgnoreCase)) n.ParentNode.RemoveChild(n);
        XmlNode bridge;
        if(original != null) bridge=original.CloneNode(true);
        else {
            var template=new XmlDocument();
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("BridgeSurfaceTemplate.xml"))template.Load(stream);
            bridge=doc.ImportNode(template.DocumentElement,true);
        }
        SetChild(doc, bridge, "RelativePathFromApp", BridgeRel);
        SetChild(doc, bridge, "AssignIndex", "4294967295");
        SetChild(doc, bridge, "IsActive", "false");
        SetChild(doc, bridge, "ControllerType", "LV1 Bridge");
        // Preset binding is handled by the module, not by editing sessions or preferences.
        if(original != null) original.ParentNode.InsertAfter(bridge, original);
        else {
            XmlNode container=doc.SelectSingleNode("//SurfaceDrivers");
            if(container==null) {container=doc.CreateElement("SurfaceDrivers");doc.DocumentElement.AppendChild(container);}
            container.AppendChild(bridge);
        }
        string temp = path + ".new";
        doc.Save(temp);
        File.Copy(temp, path, true);
        File.Delete(temp);
    }

    static void ForceBridgePreset(XmlDocument preferences, XmlNode bridge)
    {
        XmlElement preset = bridge.SelectSingleNode(".//Preset") as XmlElement;
        XmlElement presetSize = bridge.SelectSingleNode(".//PresetSize") as XmlElement;
        if (preset == null || String.IsNullOrWhiteSpace(preset.InnerText))
            throw new InvalidOperationException("Fader Bank default preset is missing");

        string hex = Regex.Replace(preset.InnerText, "[^0-9a-fA-F]", "");
        byte[] oldBytes = new byte[hex.Length / 2];
        for (int i = 0; i < oldBytes.Length; i++) oldBytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        int contentLength = oldBytes.Length;
        while (contentLength > 0 && oldBytes[contentLength - 1] == 0) contentLength--;
        string xml = Encoding.UTF8.GetString(oldBytes, 0, contentLength);

        var fragments = new XmlDocument { PreserveWhitespace = false };
        fragments.LoadXml("<ROOT>" + xml + "</ROOT>");
        foreach (XmlNode section in fragments.DocumentElement.ChildNodes)
        {
            if (section.Name != "MIDI" && section.Name != "MIDI_D2") continue;
            bool enabled = section.Name == "MIDI";
            SetChild(fragments, section, "NumDevicesConnected", enabled ? "1" : "0");
            SetChild(fragments, section, "EnableUserKeys", "true");
            XmlElement surfaces = Child(section, "Surfaces");
            if (surfaces == null) { surfaces = fragments.CreateElement("Surfaces"); section.AppendChild(surfaces); }
            surfaces.RemoveAll();
            XmlElement surface = fragments.CreateElement("Surface");
            surfaces.AppendChild(surface);
            if (enabled)
            {
                AddPort(fragments, surface, true, "FaderBank In");
                AddPort(fragments, surface, false, "FaderBank Out");
            }
        }

        var text = new StringBuilder();
        foreach (XmlNode section in fragments.DocumentElement.ChildNodes) text.Append(section.OuterXml).Append('\n');
        byte[] content = Encoding.UTF8.GetBytes(text.ToString());
        if (content.Length > oldBytes.Length) throw new InvalidOperationException("Bridge preset is larger than the Waves preset buffer");
        byte[] payload = new byte[oldBytes.Length];
        Buffer.BlockCopy(content, 0, payload, 0, content.Length);
        var formatted = new StringBuilder(payload.Length * 3 + payload.Length / 16);
        formatted.AppendLine();
        for (int i = 0; i < payload.Length; i++)
        {
            if (i > 0 && i % 16 == 0) formatted.AppendLine();
            else if (i % 16 != 0) formatted.Append(' ');
            formatted.Append(payload[i].ToString("x2"));
        }
        formatted.AppendLine();
        preset.InnerText = formatted.ToString();
        if (presetSize != null) presetSize.InnerText = (payload.Length * 2).ToString();
    }

    static void AddPort(XmlDocument doc, XmlElement surface, bool input, string name)
    {
        XmlElement port = doc.CreateElement("Port");
        port.SetAttribute("IN", input ? "true" : "false");
        port.SetAttribute("Type", "0");
        port.SetAttribute("Index", "0");
        surface.AppendChild(port);
        SetChild(doc, port, "Name", name);
        SetChild(doc, port, "MixerRemoteEnabled", "false");
    }
}
