using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FaderBankXTouchBridge
{
    internal static class NativeMidi
    {
        public const int CALLBACK_FUNCTION = 0x00030000;
        public const int MIM_DATA = 0x3C3, MIM_LONGDATA = 0x3C4, MIM_LONGERROR = 0x3C6;
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct MIDIINCAPS { public ushort wMid, wPid; public uint vDriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname; public uint dwSupport; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct MIDIOUTCAPS { public ushort wMid, wPid; public uint vDriverVersion; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname; public ushort wTechnology, wVoices, wNotes, wChannelMask; public uint dwSupport; }
        [StructLayout(LayoutKind.Sequential)] public struct MIDIHDR { public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public uint dwFlags; public IntPtr lpNext, reserved; public uint dwOffset; public IntPtr r0, r1, r2, r3, r4, r5, r6, r7; }
        public delegate void MidiInProc(IntPtr h, uint msg, IntPtr instance, IntPtr p1, IntPtr p2);
        [DllImport("winmm.dll")] public static extern uint midiInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] public static extern uint midiInGetDevCaps(UIntPtr id, out MIDIINCAPS caps, uint size);
        [DllImport("winmm.dll")] public static extern uint midiInOpen(out IntPtr h, uint id, MidiInProc cb, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] public static extern uint midiInPrepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] public static extern uint midiInUnprepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] public static extern uint midiInAddBuffer(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] public static extern uint midiInStart(IntPtr h);
        [DllImport("winmm.dll")] public static extern uint midiInStop(IntPtr h);
        [DllImport("winmm.dll")] public static extern uint midiInReset(IntPtr h);
        [DllImport("winmm.dll")] public static extern uint midiInClose(IntPtr h);
        [DllImport("winmm.dll")] public static extern uint midiOutGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] public static extern uint midiOutGetDevCaps(UIntPtr id, out MIDIOUTCAPS caps, uint size);
        [DllImport("winmm.dll")] public static extern uint midiOutOpen(out IntPtr h, uint id, IntPtr cb, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] public static extern uint midiOutShortMsg(IntPtr h, uint msg);
        [DllImport("winmm.dll")] public static extern uint midiOutPrepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] public static extern uint midiOutUnprepareHeader(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] public static extern uint midiOutLongMsg(IntPtr h, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] public static extern uint midiOutReset(IntPtr h);
        [DllImport("winmm.dll")] public static extern uint midiOutClose(IntPtr h);
    }

    internal sealed class MidiInput : IDisposable
    {
        sealed class Buffer { public IntPtr Data, Header; }
        readonly List<Buffer> buffers = new List<Buffer>(); readonly NativeMidi.MidiInProc callback; IntPtr handle; bool stopping;
        public event Action<byte, byte, byte> ShortMessage; public event Action<byte[]> LongMessage;
        public MidiInput(uint id)
        {
            callback = OnMidi; Check(NativeMidi.midiInOpen(out handle, id, callback, IntPtr.Zero, NativeMidi.CALLBACK_FUNCTION), "open MIDI input");
            try { for (int i = 0; i < 8; i++) AddBuffer(2048); Check(NativeMidi.midiInStart(handle), "start MIDI input"); } catch { Dispose(); throw; }
        }
        void AddBuffer(int length)
        {
            Buffer b = new Buffer(); b.Data = Marshal.AllocHGlobal(length); NativeMidi.MIDIHDR mh = new NativeMidi.MIDIHDR(); mh.lpData = b.Data; mh.dwBufferLength = (uint)length;
            b.Header = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeMidi.MIDIHDR))); Marshal.StructureToPtr(mh, b.Header, false);
            Check(NativeMidi.midiInPrepareHeader(handle, b.Header, (uint)Marshal.SizeOf(typeof(NativeMidi.MIDIHDR))), "prepare MIDI buffer"); buffers.Add(b);
            Check(NativeMidi.midiInAddBuffer(handle, b.Header, (uint)Marshal.SizeOf(typeof(NativeMidi.MIDIHDR))), "queue MIDI buffer");
        }
        void OnMidi(IntPtr h, uint msg, IntPtr instance, IntPtr p1, IntPtr p2)
        {
            try
            {
                if (msg == NativeMidi.MIM_DATA)
                {
                    uint m = unchecked((uint)p1.ToInt64()); Action<byte, byte, byte> cb = ShortMessage;
                    if (cb != null) cb((byte)(m & 255), (byte)((m >> 8) & 255), (byte)((m >> 16) & 255));
                }
                else if (msg == NativeMidi.MIM_LONGDATA || msg == NativeMidi.MIM_LONGERROR)
                {
                    NativeMidi.MIDIHDR mh = (NativeMidi.MIDIHDR)Marshal.PtrToStructure(p1, typeof(NativeMidi.MIDIHDR));
                    if (mh.dwBytesRecorded > 0) { byte[] b = new byte[mh.dwBytesRecorded]; Marshal.Copy(mh.lpData, b, 0, b.Length); Action<byte[]> cb = LongMessage; if (cb != null) cb(b); }
                    if (!stopping) { mh.dwBytesRecorded = 0; Marshal.StructureToPtr(mh, p1, false); NativeMidi.midiInAddBuffer(handle, p1, (uint)Marshal.SizeOf(typeof(NativeMidi.MIDIHDR))); }
                }
            } catch { }
        }
        public void Dispose()
        {
            stopping = true; if (handle != IntPtr.Zero) { NativeMidi.midiInStop(handle); NativeMidi.midiInReset(handle); }
            foreach (Buffer b in buffers) { if (handle != IntPtr.Zero) NativeMidi.midiInUnprepareHeader(handle, b.Header, (uint)Marshal.SizeOf(typeof(NativeMidi.MIDIHDR))); if (b.Header != IntPtr.Zero) Marshal.FreeHGlobal(b.Header); if (b.Data != IntPtr.Zero) Marshal.FreeHGlobal(b.Data); }
            buffers.Clear(); if (handle != IntPtr.Zero) { NativeMidi.midiInClose(handle); handle = IntPtr.Zero; }
        }
        static void Check(uint r, string what) { if (r != 0) throw new InvalidOperationException("Could not " + what + " (WinMM " + r + ")."); }
    }

    internal sealed class MidiOutput : IDisposable
    {
        IntPtr handle; readonly object gate = new object();
        public MidiOutput(uint id) { uint r = NativeMidi.midiOutOpen(out handle, id, IntPtr.Zero, IntPtr.Zero, 0); if (r != 0) throw new InvalidOperationException("Could not open MIDI output (WinMM " + r + ")."); }
        public void Send(byte status, byte d1, byte d2) { lock (gate) if (handle != IntPtr.Zero) NativeMidi.midiOutShortMsg(handle, (uint)(status | (d1 << 8) | (d2 << 16))); }
        public void SendSysEx(byte[] data)
        {
            if (data == null || data.Length == 0) return; IntPtr localHandle; lock (gate) localHandle = handle; if (localHandle == IntPtr.Zero) return;
            byte[] copy = (byte[])data.Clone(); ThreadPool.QueueUserWorkItem(delegate
            {
                IntPtr mem = IntPtr.Zero, hdr = IntPtr.Zero; try
                {
                    mem = Marshal.AllocHGlobal(copy.Length); Marshal.Copy(copy, 0, mem, copy.Length); NativeMidi.MIDIHDR mh = new NativeMidi.MIDIHDR(); mh.lpData = mem; mh.dwBufferLength = (uint)copy.Length;
                    hdr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeMidi.MIDIHDR))); Marshal.StructureToPtr(mh, hdr, false);
                    lock (gate) { if (handle == IntPtr.Zero) return; if (NativeMidi.midiOutPrepareHeader(handle, hdr, (uint)Marshal.SizeOf(typeof(NativeMidi.MIDIHDR))) != 0) return; NativeMidi.midiOutLongMsg(handle, hdr, (uint)Marshal.SizeOf(typeof(NativeMidi.MIDIHDR))); }
                    for (int i = 0; i < 100; i++) { Thread.Sleep(5); mh = (NativeMidi.MIDIHDR)Marshal.PtrToStructure(hdr, typeof(NativeMidi.MIDIHDR)); if ((mh.dwFlags & 1) != 0) break; }
                    lock (gate) if (handle != IntPtr.Zero) NativeMidi.midiOutUnprepareHeader(handle, hdr, (uint)Marshal.SizeOf(typeof(NativeMidi.MIDIHDR)));
                } catch { } finally { if (hdr != IntPtr.Zero) Marshal.FreeHGlobal(hdr); if (mem != IntPtr.Zero) Marshal.FreeHGlobal(mem); }
            });
        }
        public void Dispose() { lock (gate) { if (handle != IntPtr.Zero) { NativeMidi.midiOutReset(handle); NativeMidi.midiOutClose(handle); handle = IntPtr.Zero; } } }
    }

    internal sealed class PortItem
    {
        public readonly uint Id; public readonly string Name; public PortItem(uint id, string name) { Id = id; Name = name; } public override string ToString() { return Name; }
    }

    // Hosts a real pair of WinMM endpoints through CopperLan's already-installed,
    // signed CPVirtualMidi driver.  LV1 Classic only accepts the two fixed names
    // below, so the user-facing application is called Fader Bank Bridge while the
    // driver endpoints retain the names expected by FaderBank_Control.dll.
    internal sealed class FaderBankVirtualMidi : IDisposable
    {
        // These names are hard-coded in Waves FaderBank_Control.dll.
        const string InName = "FaderBank In", OutName = "FaderBank Out";
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void DriverCallback(IntPtr unused, IntPtr context, IntPtr packet, IntPtr unused2);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate IntPtr OpenFn(DriverCallback callback, IntPtr context);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate void CloseFn(IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int GetCapsFn(IntPtr handle, IntPtr buffer, ref uint size);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int SetPortCountFn(IntPtr handle, uint count);
        [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)] delegate int SetPortNameFn(IntPtr handle, uint index, [MarshalAs(UnmanagedType.LPWStr)] string name);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int StartReadFn(IntPtr handle, uint token);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int StopReadFn(IntPtr handle, uint token);
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate int SendDataFn(IntPtr handle, uint token, byte[] data, uint length);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)] static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] static extern int FreeLibrary(IntPtr module);

        IntPtr module, handle; uint port; readonly uint[] readTokens = { 1, 3 }, sendTokens = { 0, 2 }; DriverCallback callback; string stoppedService;
        CloseFn close; StopReadFn stopRead; SendDataFn sendData;
        public event Action<byte, byte, byte> ShortMessage; public event Action<byte[]> LongMessage;
        public string Detail { get; private set; }

        public static FaderBankVirtualMidi TryCreate(out string reason)
        {
            FaderBankVirtualMidi v = new FaderBankVirtualMidi();
            try { v.Open(); reason = v.Detail; return v; }
            catch (Exception ex) { reason = ex.Message; v.Dispose(); return null; }
        }
        void Open()
        {
            // CPVNM owns the driver's configuration handle and can reject port
            // count/name changes even though CPVMidi_Open itself succeeds.
            // This bridge replaces CPVNM for this one private MIDI cable.
            StopCopperLanManager();
            string[] paths = {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CPVMidi.dll"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CopperLan", "CPVNM", "CPVMidi.dll")
            };
            string dll = null; foreach (string p in paths) if (File.Exists(p)) { dll = p; break; }
            if (dll == null) throw new InvalidOperationException("CopperLan CPVirtualMidi is not installed.");
            module = LoadLibrary(dll); if (module == IntPtr.Zero) throw new InvalidOperationException("Could not load CopperLan CPVMidi.dll (" + Marshal.GetLastWin32Error() + ").");
            OpenFn open = Proc<OpenFn>("CPVMidi_Open"); close = Proc<CloseFn>("CPVMidi_Close"); GetCapsFn getCaps = Proc<GetCapsFn>("CPVMidi_GetCaps");
            SetPortCountFn setCount = Proc<SetPortCountFn>("CPVMidi_SetPortCount"); SetPortNameFn setName = Proc<SetPortNameFn>("CPVMidi_SetPortName");
            StartReadFn startRead = Proc<StartReadFn>("CPVMidi_StartRead"); stopRead = Proc<StopReadFn>("CPVMidi_StopRead"); sendData = Proc<SendDataFn>("CPVMidi_SendData");
            callback = OnDriverData; handle = open(callback, IntPtr.Zero);
            if (handle == IntPtr.Zero && TryStopService("CPVNM")) { stoppedService = "CPVNM"; Thread.Sleep(700); handle = open(callback, IntPtr.Zero); }
            if (handle == IntPtr.Zero && TryStopService("CPVNM Service")) { stoppedService = "CPVNM Service"; Thread.Sleep(700); handle = open(callback, IntPtr.Zero); }
            if (handle == IntPtr.Zero) throw new InvalidOperationException("The CopperLan virtual MIDI driver could not be opened. Close CopperLan Manager or run this tool as administrator once.");

            // CPVirtualMidi assigns one shared name to both directions of a
            // virtual cable.  LV1's surface manifest deliberately asks for two
            // different WinMM names (FaderBank In / FaderBank Out), so use two
            // cables and only consume the required direction of each one.
            uint count = ReadPortCount(getCaps);
            if (count != 2)
            {
                if (setCount(handle, 2) == 0)
                    throw new InvalidOperationException("Could not reset the duplicated VMIDI ports. Close LV1 once and start the bridge again.");
                Thread.Sleep(1200);
            }
            port = 0;
            bool named = false;
            for (int attempt = 0; attempt < 20 && !named; attempt++)
            {
                named = setName(handle, 0, InName) != 0 && setName(handle, 1, OutName) != 0;
                if (!named) Thread.Sleep(250);
            }
            if (!named) throw new InvalidOperationException("The CPVirtualMidi configuration handle is still busy. Close any older Fader Bank Bridge window and try again.");
            Thread.Sleep(500);
            string actualIn, actualOut;
            ReadPairNames(getCaps, out actualIn, out actualOut);
            if (actualIn != InName || actualOut != OutName)
                throw new InvalidOperationException("CopperLan did not retain the requested MIDI names (" + actualIn + " / " + actualOut + ").");
            int readers = 0;
            foreach (uint token in readTokens) if (startRead(handle, token) != 0) readers++;
            if (readers == 0) throw new InvalidOperationException("Could not start the Fader Bank Bridge MIDI endpoints.");
            Detail = "Fader Bank Bridge is active as one LV1 device (endpoints: " + InName + " / " + OutName + ").";
        }
        uint ReadPortCount(GetCapsFn getCaps)
        {
            uint size = 0; getCaps(handle, IntPtr.Zero, ref size); if (size < 4) size = 0x4000;
            IntPtr mem = Marshal.AllocHGlobal((int)size); try { if (getCaps(handle, mem, ref size) == 0) return 0; return unchecked((uint)Marshal.ReadInt32(mem)); } finally { Marshal.FreeHGlobal(mem); }
        }
        void ReadPairNames(GetCapsFn getCaps, out string first, out string second)
        {
            first = second = ""; uint size = 0; getCaps(handle, IntPtr.Zero, ref size); if (size < 532) size = 532;
            IntPtr mem = Marshal.AllocHGlobal((int)size); try
            {
                if (getCaps(handle, mem, ref size) == 0 || Marshal.ReadInt32(mem) < 2) return;
                // Header DWORD followed by two 0x84-byte endpoint records per
                // cable.  The cable name begins eight bytes into each record.
                first = ReadName(mem, 4); second = ReadName(mem, 268);
            } finally { Marshal.FreeHGlobal(mem); }
        }
        // CPVMidi caps: DWORD cableCount, followed by 2*cableCount endpoint
        // entries. Each 0x84-byte entry has two DWORDs + WCHAR name[60].
        static string ReadName(IntPtr mem, int off) { return (Marshal.PtrToStringUni(IntPtr.Add(mem, off + 8), 60) ?? "").TrimEnd('\0'); }
        T Proc<T>(string name) where T : class { IntPtr p = GetProcAddress(module, name); if (p == IntPtr.Zero) throw new MissingMethodException(name); return Marshal.GetDelegateForFunctionPointer(p, typeof(T)) as T; }
        static bool TryStopService(string name)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo pi = new System.Diagnostics.ProcessStartInfo("sc.exe", "stop \"" + name + "\""); pi.UseShellExecute = false; pi.CreateNoWindow = true; pi.RedirectStandardOutput = true; pi.RedirectStandardError = true;
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(pi)) { p.WaitForExit(5000); return p.ExitCode == 0; }
            } catch { return false; }
        }
        static void StopCopperLanManager()
        {
            TryRunHidden("taskkill.exe", "/F /IM CPVNM.exe");
            TryRunHidden("sc.exe", "stop CPVNM");
            TryRunHidden("sc.exe", "stop \"CPVNM Service\"");
            Thread.Sleep(300);
        }
        static void TryRunHidden(string file, string arguments)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo pi = new System.Diagnostics.ProcessStartInfo(file, arguments);
                pi.UseShellExecute = false; pi.CreateNoWindow = true;
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(pi)) p.WaitForExit(3000);
            }
            catch { }
        }
        static void StartService(string name)
        {
            try { System.Diagnostics.ProcessStartInfo pi = new System.Diagnostics.ProcessStartInfo("sc.exe", "start \"" + name + "\""); pi.UseShellExecute = false; pi.CreateNoWindow = true; using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(pi)) p.WaitForExit(5000); } catch { }
        }
        void OnDriverData(IntPtr unused, IntPtr context, IntPtr packet, IntPtr unused2)
        {
            try
            {
                int length = (ushort)Marshal.ReadInt16(packet, 4); if (length <= 0 || length > 4096) return; byte[] data = new byte[length]; Marshal.Copy(IntPtr.Add(packet, 8), data, 0, length);
                if (length <= 3 && data[0] < 0xF0) { byte d1 = length > 1 ? data[1] : (byte)0, d2 = length > 2 ? data[2] : (byte)0; Action<byte, byte, byte> s = ShortMessage; if (s != null) s(data[0], d1, d2); }
                else { Action<byte[]> l = LongMessage; if (l != null) l(data); }
            } catch { }
        }
        public bool Send(byte status, byte d1, byte d2) { return Send(new[] { status, d1, d2 }); }
        public bool Send(byte[] data)
        {
            if (handle == IntPtr.Zero || data == null || data.Length == 0) return false;
            bool sent = false;
            foreach (uint token in sendTokens) if (sendData(handle, token, data, (uint)data.Length) != 0) sent = true;
            return sent;
        }
        public void Dispose()
        {
            if (handle != IntPtr.Zero) { try { if (stopRead != null) foreach (uint token in readTokens) stopRead(handle, token); } catch { } try { if (close != null) close(handle); } catch { } handle = IntPtr.Zero; }
            if (module != IntPtr.Zero) { FreeLibrary(module); module = IntPtr.Zero; }
            if (!String.IsNullOrEmpty(stoppedService)) { StartService(stoppedService); stoppedService = null; }
        }
    }

    public sealed class MainForm : Form
    {
        readonly ComboBox profile = new ComboBox(), lv1In = new ComboBox(), lv1Out = new ComboBox(), compactIn = new ComboBox(), compactOut = new ComboBox(), extenderIn = new ComboBox(), extenderOut = new ComboBox(), eqOut = new ComboBox();
        readonly NumericUpDown toggleNote = new NumericUpDown(), eqBase = new NumericUpDown(), eqChannel = new NumericUpDown();
        readonly Button refresh = new Button(), start = new Button(), panGain = new Button(), test = new Button(); readonly Label status = new Label(), counters = new Label(), modeLabel = new Label(), intro = new Label(), firstInLabel = new Label(), firstOutLabel = new Label(), secondInLabel = new Label(), secondOutLabel = new Label(), note = new Label();
        readonly CheckBox autoStart = new CheckBox(), enableEq = new CheckBox(), compactRingMeters = new CheckBox(); readonly TextBox monitor = new TextBox();
        MidiInput inLv1, inCompact, inExtender; MidiOutput outLv1, outCompact, outExtender, outEq; FaderBankVirtualMidi virtualLv1; bool running;
        long fromLv1, toLv1, meterPackets; bool gainMode, lv1FlipActive, userModeActive; int activeAux = -1, auxPage = -1; byte lv1FlipNote = 87; readonly int[] compactEncoderLast = new int[16]; readonly object stateGate = new object();
        readonly byte[] compactParameter = new byte[16], compactMeter = new byte[8], compactRingBehavior = new byte[8]; readonly DateTime[] compactParameterUntil = new DateTime[8];
        readonly string settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "LV1 FaderBank X-Touch Bridge", "settings.ini");
        DateTime lastEvent, lastIdentity, lastSessionProvision, lastReceived; volatile bool receivedFromLv1; int identityBursts, lastLv1ProcessId; readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer(); BridgeApiServer deckApi;
        readonly NotifyIcon tray = new NotifyIcon(); bool allowExit;
        bool IconProfile { get { return profile.SelectedIndex == 1; } }

        public MainForm()
        {
            Text = "LV1 Fader Bank Bridge"; Width = 940; Height = 800; MinimumSize = new Size(880, 740); StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 9F);
            BackColor = Color.FromArgb(28, 30, 35); ForeColor = Color.White; for (int i = 0; i < compactEncoderLast.Length; i++) compactEncoderLast[i] = -1; for (int i = 0; i < compactRingBehavior.Length; i++) compactRingBehavior[i] = 255;
            BuildUi(); RefreshPorts(); LoadSettings(); timer.Interval = 400; timer.Tick += delegate { UpdateStatus(); SendFaderBankIdentityIfDue(); ObserveLv1Process(); }; timer.Start();
            tray.Icon = SystemIcons.Application; tray.Text = "LV1 Fader Bank Bridge"; tray.Visible = true;
            ContextMenuStrip trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Open", null, delegate { ShowFromTray(); });
            trayMenu.Items.Add("Exit", null, delegate { allowExit = true; Close(); });
            tray.ContextMenuStrip = trayMenu; tray.DoubleClick += delegate { ShowFromTray(); };
            try { deckApi = new BridgeApiServer(49631, HandleDeckApi); } catch (Exception ex) { Log("Stream Deck API unavailable: " + ex.Message); }
            FormClosing += OnFormClosing;
            Shown += delegate { if (autoStart.Checked) StartBridge(); if (Program.StartHidden) BeginInvoke((MethodInvoker)HideToTray); };
        }

        void ShowFromTray() { ShowInTaskbar = true; Show(); WindowState = FormWindowState.Normal; Activate(); }
        void HideToTray() { Hide(); ShowInTaskbar = false; }
        void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!allowExit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideToTray(); return; }
            SaveSettings(); tray.Visible = false; tray.Dispose(); if (deckApi != null) deckApi.Dispose(); StopBridge();
        }

        void BuildUi()
        {
            Controls.Add(LabelAt("LV1 Fader Bank Bridge", 20, 14, 700, 34, 18, FontStyle.Bold));
            intro.Text = "Choose a controller profile. The LV1 virtual Fader Bank connection is shared by both modes."; intro.SetBounds(22, 49, 850, 25); intro.ForeColor = Color.Silver; Controls.Add(intro);
            Controls.Add(LabelAt("Controller profile", 22, 82, 195, 25, 9, FontStyle.Bold)); SetupCombo(profile, 218, 79, 448); profile.Items.Add("Behringer X-TOUCH Compact + Extender"); profile.Items.Add("iCON P1-M + P1-X (MCU, D4T/D4)"); profile.SelectedIndex = 0; profile.SelectedIndexChanged += delegate { ApplyProfileUi(true); }; Controls.Add(profile);
            int y = 118;
            Controls.Add(LabelAt("LV1 Fader Bank device", 22, y + 4, 195, 25, 9, FontStyle.Regular));
            SetupCombo(lv1In, 218, y, 448); lv1In.Items.Add("FaderBank — Automatic"); lv1In.SelectedIndex = 0; lv1In.Enabled = false; Controls.Add(lv1In);
            y += 38;
            AddPair("Compact input", compactIn, "Compact output", compactOut, y, firstInLabel, firstOutLabel); y += 38;
            AddPair("Extender input", extenderIn, "Extender output", extenderOut, y, secondInLabel, secondOutLabel); y += 45;
            refresh.Text = "Refresh MIDI ports"; refresh.SetBounds(22, y, 175, 32); refresh.Click += delegate { RefreshPorts(); }; Controls.Add(refresh);
            start.Text = "Start bridge"; start.SetBounds(207, y, 175, 32); start.BackColor = Color.FromArgb(0, 150, 180); start.FlatStyle = FlatStyle.Flat; start.Click += delegate { if (!running) StartBridge(); else StopBridge(); }; Controls.Add(start);
            test.Text = "Test LEDs / faders"; test.SetBounds(392, y, 175, 32); test.Click += delegate { TestOutputs(); }; Controls.Add(test);
            autoStart.Text = "Start automatically"; autoStart.Checked = true; autoStart.SetBounds(590, y + 5, 180, 25); Controls.Add(autoStart); y += 48;

            GroupBox mode = Group("Strip encoders 1–16", 22, y, 432, 126); Controls.Add(mode);
            mode.Controls.Add(LabelAt("All 16 strip encoders follow one LV1 mode:", 14, 24, 290, 22, 9, FontStyle.Regular));
            panGain.Text = "Switch PAN / GAIN"; panGain.SetBounds(14, 53, 180, 34); panGain.Click += delegate { SetGainMode(!gainMode, true); }; mode.Controls.Add(panGain);
            mode.Controls.Add(LabelAt("Compact toggle Note", 211, 57, 135, 24, 9, FontStyle.Regular)); SetupNumeric(toggleNote, 348, 55, 62, 0, 127, 49); mode.Controls.Add(toggleNote);
            modeLabel.Text = "PAN"; modeLabel.Font = new Font("Segoe UI", 11, FontStyle.Bold); modeLabel.ForeColor = Color.Cyan; modeLabel.SetBounds(14, 92, 90, 24); mode.Controls.Add(modeLabel);
            compactRingMeters.Text = "Show VU on Compact rings when idle"; compactRingMeters.Checked = true; compactRingMeters.SetBounds(112, 91, 290, 24); compactRingMeters.CheckedChanged += delegate { if (inLv1 != null) RefreshCompactRings(); }; mode.Controls.Add(compactRingMeters);

            GroupBox eq = Group("Compact encoders 9–16: fixed EQ MIDI", 470, y, 432, 126); Controls.Add(eq);
            enableEq.Text = "Enable separate EQ CC output"; enableEq.SetBounds(14, 22, 230, 24); eq.Controls.Add(enableEq);
            SetupCombo(eqOut, 14, 49, 395); eq.Controls.Add(eqOut);
            eq.Controls.Add(LabelAt("CC base", 14, 85, 60, 24, 9, FontStyle.Regular)); SetupNumeric(eqBase, 76, 83, 62, 0, 120, 102); eq.Controls.Add(eqBase);
            eq.Controls.Add(LabelAt("MIDI channel", 158, 85, 90, 24, 9, FontStyle.Regular)); SetupNumeric(eqChannel, 250, 83, 62, 1, 16, 16); eq.Controls.Add(eqChannel);
            y += 139;

            status.SetBounds(22, y, 500, 28); status.Text = "Stopped"; status.Font = new Font("Segoe UI", 10, FontStyle.Bold); Controls.Add(status);
            counters.SetBounds(520, y, 382, 28); counters.TextAlign = ContentAlignment.MiddleRight; counters.ForeColor = Color.Silver; Controls.Add(counters); y += 34;
            monitor.Multiline = true; monitor.ReadOnly = true; monitor.ScrollBars = ScrollBars.Vertical; monitor.BackColor = Color.FromArgb(18, 20, 24); monitor.ForeColor = Color.Gainsboro; monitor.Font = new Font("Consolas", 8.5F); monitor.SetBounds(22, y, 880, 220); monitor.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; Controls.Add(monitor);
            note.SetBounds(22, Height - 72, 870, 42); note.Font = new Font("Segoe UI", 8, FontStyle.Regular); note.ForeColor = Color.DarkGray; note.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom; Controls.Add(note); ApplyProfileUi(false);
        }

        void AddPair(string leftText, ComboBox left, string rightText, ComboBox right, int y, Label leftLabel, Label rightLabel)
        {
            Label l = leftLabel ?? LabelAt(leftText, 22, y + 3, 195, 25, 9, FontStyle.Bold); l.Text = leftText; l.SetBounds(22, y + 3, 195, 25); l.Font = new Font("Segoe UI", 9, FontStyle.Bold); Controls.Add(l); SetupCombo(left, 218, y, 235); Controls.Add(left);
            Label r = rightLabel ?? LabelAt(rightText, 470, y + 3, 195, 25, 9, FontStyle.Bold); r.Text = rightText; r.SetBounds(470, y + 3, 195, 25); r.Font = new Font("Segoe UI", 9, FontStyle.Bold); Controls.Add(r); SetupCombo(right, 666, y, 236); Controls.Add(right);
        }
        void ApplyProfileUi(bool refreshSelection)
        {
            bool icon = IconProfile;
            firstInLabel.Text = icon ? "P1-M input (channels 1–8)" : "Compact input"; firstOutLabel.Text = icon ? "P1-M output" : "Compact output";
            secondInLabel.Text = icon ? "P1-X input (channels 9–16)" : "Extender input"; secondOutLabel.Text = icon ? "P1-X output" : "Extender output";
            compactRingMeters.Text = icon ? "Send VU to P1-M channel meters" : "Show VU on Compact rings when idle";
            enableEq.Text = icon ? "Enable touchscreen/custom MIDI output" : "Enable separate EQ CC output";
            note.Text = icon ? "Required device modes: P1-M = MCU main unit; P1-X = MCU extender. D4T belongs on P1-M and D4 belongs on P1-X. Names, values and meters are sent through MCU." : "Required device modes: X-TOUCH COMPACT = Standard Layer A; X-TOUCH EXTENDER = MIDI CTRL REL. The optional EQ output is for an LV1 MIDI-control port mapped to eight EQ parameters.";
            if (refreshSelection) RefreshPorts();
        }
        static GroupBox Group(string t, int x, int y, int w, int h) { GroupBox g = new GroupBox(); g.Text = t; g.SetBounds(x, y, w, h); g.ForeColor = Color.White; return g; }
        static Label LabelAt(string t, int x, int y, int w, int h, float size, FontStyle style) { Label l = new Label(); l.Text = t; l.SetBounds(x, y, w, h); l.Font = new Font("Segoe UI", size, style); return l; }
        static void SetupCombo(ComboBox c, int x, int y, int w) { c.DropDownStyle = ComboBoxStyle.DropDownList; c.SetBounds(x, y, w, 28); }
        static void SetupNumeric(NumericUpDown n, int x, int y, int w, int min, int max, int value) { n.Minimum = min; n.Maximum = max; n.Value = value; n.SetBounds(x, y, w, 28); }

        void RefreshPorts()
        {
            ComboBox[] ins = { lv1In, compactIn, extenderIn }, outs = { lv1Out, compactOut, extenderOut, eqOut }; string[] oldIn = new string[ins.Length], oldOut = new string[outs.Length];
            for (int i = 0; i < ins.Length; i++) { oldIn[i] = ins[i].Text; ins[i].Items.Clear(); ins[i].Items.Add("(None)"); }
            for (int i = 0; i < outs.Length; i++) { oldOut[i] = outs[i].Text; outs[i].Items.Clear(); outs[i].Items.Add("(None)"); }
            for (uint id = 0; id < NativeMidi.midiInGetNumDevs(); id++) { NativeMidi.MIDIINCAPS c; if (NativeMidi.midiInGetDevCaps((UIntPtr)id, out c, (uint)Marshal.SizeOf(typeof(NativeMidi.MIDIINCAPS))) == 0) foreach (ComboBox box in ins) box.Items.Add(new PortItem(id, c.szPname)); }
            for (uint id = 0; id < NativeMidi.midiOutGetNumDevs(); id++) { NativeMidi.MIDIOUTCAPS c; if (NativeMidi.midiOutGetDevCaps((UIntPtr)id, out c, (uint)Marshal.SizeOf(typeof(NativeMidi.MIDIOUTCAPS))) == 0) foreach (ComboBox box in outs) box.Items.Add(new PortItem(id, c.szPname)); }
            string[] firstNames = IconProfile ? new[] { "P1-M", "iCON P1-M" } : new[] { "X-TOUCH COMPACT", "X-TOUCH" };
            string[] secondNames = IconProfile ? new[] { "P1-X", "iCON P1-X" } : new[] { "X-TOUCH EXTENDER", "EXTENDER" };
            for (int i = 0; i < ins.Length; i++) { SelectText(ins[i], oldIn[i]); if (ins[i].SelectedIndex < 0) AutoSelect(ins[i], i == 0 ? new[] { "FaderBank Out", "LV1Control Emu Out" } : i == 1 ? firstNames : secondNames); }
            for (int i = 0; i < outs.Length; i++) { SelectText(outs[i], oldOut[i]); if (outs[i].SelectedIndex < 0) AutoSelect(outs[i], i == 0 ? new[] { "FaderBank In", "LV1Control Emu In" } : i == 1 ? firstNames : i == 2 ? secondNames : new string[0]); }
        }
        static void AutoSelect(ComboBox box, string[] names) { foreach (string n in names) for (int i = 1; i < box.Items.Count; i++) if (box.Items[i].ToString().IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) { box.SelectedIndex = i; return; } box.SelectedIndex = 0; }

        void StartBridge()
        {
            try
            {
                StopBridge(); string virtualReason; virtualLv1 = FaderBankVirtualMidi.TryCreate(out virtualReason);
                if (virtualLv1 != null) { virtualLv1.ShortMessage += OnLv1Short; virtualLv1.LongMessage += OnLv1Long; Log(virtualReason); }
                else
                {
                    // Do not open the visible WinMM endpoints as a fallback.
                    // WinMM ports are exclusive on this system and doing so made
                    // them unavailable to LV1's Fader Bank and Mackie modules.
                    throw new InvalidOperationException(virtualReason);
                }
                if (compactOut.SelectedItem is PortItem) outCompact = new MidiOutput(((PortItem)compactOut.SelectedItem).Id);
                if (compactIn.SelectedItem is PortItem) { inCompact = new MidiInput(((PortItem)compactIn.SelectedItem).Id); inCompact.ShortMessage += OnCompactShort; }
                if (extenderOut.SelectedItem is PortItem) outExtender = new MidiOutput(((PortItem)extenderOut.SelectedItem).Id);
                if (extenderIn.SelectedItem is PortItem) { inExtender = new MidiInput(((PortItem)extenderIn.SelectedItem).Id); inExtender.ShortMessage += OnExtenderShort; }
                if (enableEq.Checked && eqOut.SelectedItem is PortItem)
                {
                    PortItem eqPort = (PortItem)eqOut.SelectedItem; PortItem lv1Port = lv1Out.SelectedItem as PortItem;
                    outEq = lv1Port != null && eqPort.Id == lv1Port.Id ? outLv1 : new MidiOutput(eqPort.Id);
                }
                fromLv1 = toLv1 = meterPackets = 0; receivedFromLv1 = false; lastReceived = DateTime.MinValue; identityBursts = 0; lastIdentity = DateTime.MinValue; running = true; SetEnabled(false); start.Text = "Stop bridge"; status.Text = "Virtual MIDI ready; waiting for LV1 feedback"; status.ForeColor = Color.LightGreen; Log(IconProfile ? "Bridge started. P1-M=MCU main; P1-X=MCU extender." : "Bridge started. Compact=Standard Layer A; Extender=MIDI CTRL REL.");
                SendFaderBankIdentity();
                if (!IconProfile) ForceStandardModes(); SetGainMode(gainMode, true); SaveSettings();
            } catch (Exception ex) { StopBridge(); MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        void StopBridge()
        {
            if (inLv1 != null) { inLv1.ShortMessage -= OnLv1Short; inLv1.LongMessage -= OnLv1Long; inLv1.Dispose(); inLv1 = null; }
            if (inCompact != null) { inCompact.ShortMessage -= OnCompactShort; inCompact.Dispose(); inCompact = null; }
            if (inExtender != null) { inExtender.ShortMessage -= OnExtenderShort; inExtender.Dispose(); inExtender = null; }
            if (virtualLv1 != null) { virtualLv1.ShortMessage -= OnLv1Short; virtualLv1.LongMessage -= OnLv1Long; virtualLv1.Dispose(); virtualLv1 = null; }
            DisposeOut(ref outLv1); DisposeOut(ref outCompact); DisposeOut(ref outExtender); DisposeOut(ref outEq); running = false; SetEnabled(true); start.Text = "Start bridge"; status.Text = "Stopped"; status.ForeColor = Color.White;
        }
        static void DisposeOut(ref MidiOutput o) { if (o != null) { o.Dispose(); o = null; } }
        void SetEnabled(bool on) { profile.Enabled = compactIn.Enabled = compactOut.Enabled = extenderIn.Enabled = extenderOut.Enabled = eqOut.Enabled = refresh.Enabled = enableEq.Enabled = eqBase.Enabled = eqChannel.Enabled = toggleNote.Enabled = on; lv1In.Enabled = false; }
        void ForceStandardModes() { if (outCompact != null) { outCompact.Send(0xB0, 127, 0); outCompact.Send(0xC0, 0, 0); } }

        void OnCompactShort(byte st, byte d1, byte d2)
        {
            if (IconProfile) { OnMcuShort(0, st, d1, d2); return; }
            int type = st & 0xF0;
            if (type == 0x90 && d2 > 0 && d1 == (byte)toggleNote.Value) { SetGainMode(!gainMode, true); return; }
            if (type == 0xB0)
            {
                if (d1 >= 1 && d1 <= 8) SendPitch((byte)(d1 - 1), d2);
                else if (d1 >= 101 && d1 <= 108) SendNote((byte)(96 + d1 - 101), d2 >= 64);
                else if (d1 >= 10 && d1 <= 17) { compactParameterUntil[d1 - 10] = DateTime.Now.AddMilliseconds(1200); ShowCompactParameter(d1 - 10); SendRelativeVpot((byte)(d1 - 10), d2, (int)(d1 - 10)); }
                else if (d1 >= 18 && d1 <= 25) SendEq((byte)(d1 - 18), d2, (int)(d1 - 10));
            }
            else if (type == 0x90 || type == 0x80)
            {
                bool down = type == 0x90 && d2 > 0;
                if (d1 <= 7) SendNote((byte)(16 + d1), down);
                else if (d1 >= 16 && d1 <= 23) SendNote((byte)(32 + d1 - 16), down);
                else if (d1 >= 24 && d1 <= 31) SendNote((byte)(48 + d1 - 24), down);
                else if (d1 >= 32 && d1 <= 39) SendNote((byte)(0 + d1 - 32), down);
            }
        }
        void OnExtenderShort(byte st, byte d1, byte d2)
        {
            if (IconProfile) { OnMcuShort(8, st, d1, d2); return; }
            int type = st & 0xF0;
            if (type == 0xB0 && d1 >= 70 && d1 <= 77) SendPitch((byte)(8 + d1 - 70), d2);
            else if (type == 0xB0 && d1 >= 80 && d1 <= 87) SendRelativeVpot((byte)(8 + d1 - 80), d2, -1);
            else if (type == 0x90 || type == 0x80)
            {
                bool down = type == 0x90 && d2 > 0;
                if (d1 <= 7) SendNote((byte)(24 + d1), down);
                else if (d1 >= 16 && d1 <= 23) SendNote((byte)(40 + d1 - 16), down);
                else if (d1 >= 24 && d1 <= 31) SendNote((byte)(56 + d1 - 24), down);
                else if (d1 >= 32 && d1 <= 39) SendNote((byte)(8 + d1 - 32), down);
                else if (d1 >= 110 && d1 <= 117) SendNote((byte)(104 + d1 - 110), down);
            }
        }
        void OnMcuShort(int offset, byte st, byte d1, byte d2)
        {
            int type = st & 0xF0, ch = st & 15;
            if (offset == 0 && ch == 15)
            {
                if ((type == 0x90 || type == 0x80) && d2 > 0 && (d1 == 80 || d1 == 81)) { SetGainMode(d1 == 81, true); return; }
                if (type == 0x90 && d2 > 0 && d1 >= 32 && d1 <= 55) { SelectAuxFromTouch(d1 - 32); return; }
                if (type == 0x90 && d2 > 0 && d1 == 56) { ReturnToMainMix(); return; }
                if (outEq != null) outEq.Send(st, d1, d2); return;
            }
            if (type == 0xE0 && ch < 8) { SendPitch14((byte)(offset + ch), d1 | (d2 << 7)); return; }
            if (type == 0xB0 && d1 >= 16 && d1 <= 23) { SendRelativeVpot((byte)(offset + d1 - 16), d2, -1); return; }
            if (type != 0x90 && type != 0x80) return;
            bool down = type == 0x90 && d2 > 0; int local = -1, group = -1;
            if (d1 <= 7) { local = d1; group = 16; }          // Record
            else if (d1 <= 15) { local = d1 - 8; group = 32; } // Solo
            else if (d1 <= 23) { local = d1 - 16; group = 48; } // Mute
            else if (d1 <= 31) { local = d1 - 24; group = 0; }  // Select
            else if (d1 >= 104 && d1 <= 111) { local = d1 - 104; group = 104; } // Touch
            if (local >= 0) SendNote((byte)(group + offset + local), down);
        }
        bool SendLv1(byte status, byte d1, byte d2) { if (virtualLv1 != null) return virtualLv1.Send(status, d1, d2); if (outLv1 != null) { outLv1.Send(status, d1, d2); return true; } return false; }
        void SendPitch(byte channel, byte value7) { int v = (value7 << 7) | value7; if (SendLv1((byte)(0xE0 | channel), (byte)(v & 127), (byte)((v >> 7) & 127))) CountToLv1(); }
        void SendPitch14(byte channel, int value14) { if (SendLv1((byte)(0xE0 | (channel & 15)), (byte)(value14 & 127), (byte)((value14 >> 7) & 127))) CountToLv1(); }
        void SendNote(byte note, bool down) { if (SendLv1(0x90, note, down ? (byte)127 : (byte)0)) CountToLv1(); }
        void PulseLv1(byte note) { SendNote(note, true); SendNote(note, false); }
        void SelectAuxFromTouch(int aux)
        {
            if (aux < 0 || aux > 23) return;
            if (activeAux == aux) { ReturnToMainMix(); return; }
            if (userModeActive) { PulseLv1(0x70); userModeActive = false; Thread.Sleep(120); }
            if (activeAux < 0) { PulseLv1(lv1FlipNote); lv1FlipActive = true; Thread.Sleep(220); }
            int wantedPage = aux / 8;
            // The Classic Fader Bank uses buttons 10, 11 and 12 for MON 1-8,
            // MON 9-16 and MON 17-24. Button 9 is the separate FX 1-8 page.
            // Stream Deck AUX keys intentionally address monitor mixes 1-24.
            PulseLv1((byte)(10 + wantedPage)); Thread.Sleep(120);
            auxPage = wantedPage;
            PulseLv1((byte)(aux % 8)); activeAux = aux; UpdateAuxUi(); Log("Requested AUX " + (aux + 1));
        }
        void ReturnToMainMix()
        {
            if (lv1FlipActive || activeAux >= 0) PulseLv1(lv1FlipNote);
            lv1FlipActive = false; activeAux = -1; auxPage = -1; UpdateAuxUi(); Log("Faders returned to MAIN MIX");
        }

        string HandleDeckApi(string request)
        {
            if (String.Equals(request, "PING", StringComparison.OrdinalIgnoreCase)) return "PONG";
            if (String.Equals(request, "STATE", StringComparison.OrdinalIgnoreCase))
            {
                string[] aux = new string[24], users = new string[16]; SessionNames.Load(aux, users);
                StringBuilder b = new StringBuilder("STATE|").Append(activeAux);
                foreach (string s in aux) b.Append('|').Append(SessionNames.Pack(s));
                foreach (string s in users) b.Append('|').Append(SessionNames.Pack(s));
                return b.ToString();
            }
            if (!running || !receivedFromLv1) return "ERR NO_LV1_FEEDBACK";
            string[] p = (request ?? "").Split(' '); int n;
            if (p.Length == 2 && p[0] == "AUX" && Int32.TryParse(p[1], out n) && n >= 1 && n <= 24) { BeginInvoke((MethodInvoker)delegate { SelectAuxFromTouch(n - 1); }); return "OK"; }
            if (String.Equals(request, "MIX", StringComparison.OrdinalIgnoreCase)) { BeginInvoke((MethodInvoker)delegate { ReturnToMainMix(); }); return "OK"; }
            if (p.Length == 2 && p[0] == "USER" && Int32.TryParse(p[1], out n) && n >= 1 && n <= 16)
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (activeAux >= 0) { ReturnToMainMix(); Thread.Sleep(150); }
                    if (!userModeActive) { PulseLv1(0x70); userModeActive = true; Thread.Sleep(150); }
                    PulseLv1((byte)(n - 1));
                    Log("Requested user key " + n + " from Stream Deck");
                }); return "OK";
            }
            return "ERR";
        }
        void UpdateAuxUi()
        {
            try { BeginInvoke((MethodInvoker)delegate { modeLabel.Text = activeAux < 0 ? (gainMode ? "GAIN" : "PAN") : "AUX " + (activeAux + 1); modeLabel.ForeColor = activeAux < 0 ? (gainMode ? Color.Orange : Color.Cyan) : Color.LimeGreen; }); } catch { }
            if (outCompact != null) { for (int i = 0; i < 24; i++) outCompact.Send(0x9F, (byte)(32 + i), activeAux == i ? (byte)127 : (byte)0); outCompact.Send(0x9F, 56, activeAux < 0 ? (byte)127 : (byte)0); }
        }
        void SendRelativeVpot(byte strip, byte raw, int compactIndex)
        {
            byte rel = raw;
            if (compactIndex >= 0)
            {
                lock (stateGate) { int last = compactEncoderLast[compactIndex]; compactEncoderLast[compactIndex] = raw; if (last < 0 || last == raw) return; int delta = raw - last; if (delta > 64) delta -= 128; if (delta < -64) delta += 128; rel = delta > 0 ? (byte)Math.Min(15, 64 + delta) : (byte)Math.Min(15, -delta); }
            }
            if (SendLv1(0xB0, (byte)(16 + strip), rel)) CountToLv1();
        }
        void SendEq(byte index, byte raw, int compactIndex)
        {
            if (!enableEq.Checked || outEq == null) return; int value; lock (stateGate) { int last = compactEncoderLast[compactIndex]; compactEncoderLast[compactIndex] = raw; if (last < 0) return; int delta = raw - last; if (delta > 64) delta -= 128; if (delta < -64) delta += 128; value = delta > 0 ? Math.Min(127, 64 + delta) : Math.Min(63, -delta); }
            outEq.Send((byte)(0xB0 | ((int)eqChannel.Value - 1)), (byte)((int)eqBase.Value + index), (byte)value);
        }
        void CountToLv1() { Interlocked.Increment(ref toLv1); lastEvent = DateTime.Now; }

        void OnLv1Short(byte st, byte d1, byte d2)
        {
            Interlocked.Increment(ref fromLv1); lastReceived = DateTime.Now; receivedFromLv1 = true; lastEvent = DateTime.Now; int type = st & 0xF0, ch = st & 15;
            if (type == 0xE0) { int v14 = d1 | (d2 << 7); byte v7 = (byte)(v14 >> 7); SetFader(ch, v7); }
            else if (type == 0xA0 && d1 < 16) SetFader(d1, d2);
            else if (type == 0xB0 && d1 >= 16 && d1 <= 31) SetRing(d1 - 16, d2);
            else if (type == 0x90 || type == 0x80)
            {
                bool on = type == 0x90 && d2 > 0;
                if (d1 == 80 && on) SetGainMode(false, false); else if (d1 == 81 && on) SetGainMode(true, false); else SetButton(d1, on);
            }
        }

        void ObserveLv1Process()
        {
            try
            {
                System.Diagnostics.Process[] processes = System.Diagnostics.Process.GetProcessesByName("eMotion LV1");
                if (processes.Length > 0)
                {
                    int pid = processes[0].Id;
                    if (lastLv1ProcessId != pid)
                    {
                        lastLv1ProcessId = pid; receivedFromLv1 = false; lastReceived = DateTime.MinValue; lv1FlipActive = false; userModeActive = false; activeAux = -1; auxPage = -1; UpdateAuxUi();
                    }
                    foreach (System.Diagnostics.Process process in processes) process.Dispose();
                    return;
                }
                lastLv1ProcessId = 0; receivedFromLv1 = false;
                if ((DateTime.Now - lastSessionProvision).TotalSeconds < 10) return;
                lastSessionProvision = DateTime.Now;
                // The native Bridge module now binds when LV1 creates or loads it.
            }
            catch { }
        }
        void SetFader(int strip, byte value)
        {
            if (IconProfile) { MidiOutput o = strip < 8 ? outCompact : outExtender; if (o != null && strip < 16) { int local = strip & 7, v = (value << 7) | value; o.Send((byte)(0xE0 | local), (byte)(v & 127), (byte)(v >> 7)); } return; }
            if (strip < 8) { if (outCompact != null) outCompact.Send(0xB0, (byte)(1 + strip), value); } else if (strip < 16) { if (outExtender != null) outExtender.Send(0xB0, (byte)(70 + strip - 8), value); }
        }
        void SetRing(int strip, byte value)
        {
            if (IconProfile) { if (strip >= 0 && strip < 16) compactParameter[strip] = value; MidiOutput o = strip < 8 ? outCompact : outExtender; if (o != null && strip < 16) { int pos = Math.Max(1, Math.Min(11, 1 + value * 10 / 127)); o.Send(0xB0, (byte)(48 + (strip & 7)), (byte)((gainMode ? 0x20 : 0x10) | pos)); } return; }
            if (strip < 8)
            {
                compactParameter[strip] = value;
                if (!compactRingMeters.Checked || DateTime.Now < compactParameterUntil[strip]) ShowCompactParameter(strip);
            }
            else if (strip < 16 && outExtender != null) outExtender.Send(0xB0, (byte)(80 + strip - 8), value);
        }
        void SetButton(byte note, bool on)
        {
            if (IconProfile)
            {
                int strip, mcuNote;
                if (note < 16) { strip = note; mcuNote = 24 + (strip & 7); }
                else if (note < 32) { strip = note - 16; mcuNote = strip & 7; }
                else if (note < 48) { strip = note - 32; mcuNote = 8 + (strip & 7); }
                else if (note < 64) { strip = note - 48; mcuNote = 16 + (strip & 7); }
                else return;
                MidiOutput o = strip < 8 ? outCompact : outExtender; if (o != null && strip < 16) o.Send(0x90, (byte)mcuNote, on ? (byte)127 : (byte)0); return;
            }
            if (note < 16) { int s = note; if (s < 8 && outCompact != null) outCompact.Send(0x90, (byte)(32 + s), on ? (byte)1 : (byte)0); else if (s < 16 && outExtender != null) outExtender.Send(0x90, (byte)(32 + s - 8), on ? (byte)127 : (byte)0); }
            else if (note < 32) { int s = note - 16; if (s < 8 && outCompact != null) outCompact.Send(0x90, (byte)s, on ? (byte)1 : (byte)0); else if (s < 16 && outExtender != null) outExtender.Send(0x90, (byte)(s - 8), on ? (byte)127 : (byte)0); }
            else if (note < 48) { int s = note - 32; if (s < 8 && outCompact != null) outCompact.Send(0x90, (byte)(16 + s), on ? (byte)1 : (byte)0); else if (s < 16 && outExtender != null) outExtender.Send(0x90, (byte)(16 + s - 8), on ? (byte)127 : (byte)0); }
            else if (note < 64) { int s = note - 48; if (s < 8 && outCompact != null) outCompact.Send(0x90, (byte)(24 + s), on ? (byte)1 : (byte)0); else if (s < 16 && outExtender != null) outExtender.Send(0x90, (byte)(24 + s - 8), on ? (byte)127 : (byte)0); }
        }
        void SetGainMode(bool gain, bool notifyLv1)
        {
            gainMode = gain; try { BeginInvoke((MethodInvoker)delegate { if (activeAux < 0) { modeLabel.Text = gain ? "GAIN" : "PAN"; modeLabel.ForeColor = gain ? Color.Orange : Color.Cyan; } }); } catch { }
            if (outCompact != null && !IconProfile) { for (byte i = 0; i < 8; i++) { compactParameterUntil[i] = DateTime.Now.AddMilliseconds(1400); SetCompactRingBehavior(i, gain ? (byte)4 : (byte)1); } outCompact.Send(0x90, (byte)toggleNote.Value, gain ? (byte)1 : (byte)2); }
            if (IconProfile) for (int i = 0; i < 16; i++) SetRing(i, compactParameter[i]);
            if (notifyLv1) { SendNote(gain ? (byte)81 : (byte)80, true); SendNote(gain ? (byte)81 : (byte)80, false); }
            Log("Encoder mode: " + (gain ? "GAIN" : "PAN"));
        }

        void OnLv1Long(byte[] p)
        {
            // Waves' Fader Bank discovery sends the standard universal MIDI
            // identity request. The original LV1 Classic bank answers with a
            // universal identity reply; emulate that answer so the surface
            // driver creates its single Fader Bank device row.
            if (p != null && p.Length >= 5 && p[0] == 0xF0 && p[1] == 0x7E && p[3] == 0x06 && p[4] == 0x01)
            {
                SendFaderBankIdentity();
                return;
            }
            if (p.Length < 8 || p[0] != 0xF0 || p[1] != 0 || p[2] != 0 || p[3] != 0x74 || p[4] != 0x3C || p[5] != 0x1A || p[p.Length - 1] != 0xF7) return;
            Interlocked.Increment(ref fromLv1); lastReceived = DateTime.Now; receivedFromLv1 = true; lastEvent = DateTime.Now;
            if (p[6] == 4 && p.Length == 47) DecodeMeters(p); else if (p[6] == 3 && p.Length >= 202) DecodeNames(p); else if (meterPackets < 3) Log("LV1 SysEx opcode " + p[6] + ", " + p.Length + " bytes");
        }
        void SendFaderBankIdentityIfDue()
        {
            if (!running || virtualLv1 == null) return;
            // Discovery can run while the module is loading or after Reset
            // MIDI Devices. Advertise frequently at first, then keep a slow
            // heartbeat so a later driver reload is also detected.
            double seconds = identityBursts < 30 ? 1.0 : 5.0;
            if (lastIdentity == DateTime.MinValue || (DateTime.Now - lastIdentity).TotalSeconds >= seconds) SendFaderBankIdentity();
        }
        void SendFaderBankIdentity()
        {
            if (virtualLv1 == null) return;
            byte[] reply = { 0xF0, 0x7E, 0x00, 0x06, 0x02, 0x00, 0x00, 0x74, 0x01, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0xF7 };
            if (virtualLv1.Send(reply)) { identityBursts++; lastIdentity = DateTime.Now; CountToLv1(); if (identityBursts == 1) Log("Fader Bank identity emulation active."); }
        }
        void DecodeMeters(byte[] p)
        {
            Interlocked.Increment(ref meterPackets);
            for (int strip = 0; strip < 16; strip++)
            {
                int li = strip * 2, raw = Math.Max(p[12 + li], p[13 + li]); bool clip = (p[7 + li / 7] & (1 << (li % 7))) != 0 || (p[7 + (li + 1) / 7] & (1 << ((li + 1) % 7))) != 0;
                byte level = (byte)(clip ? 127 : raw);
                if (IconProfile) SendMcuMeter(strip, level);
                else if (strip < 8) { compactMeter[strip] = level; if (compactRingMeters.Checked && DateTime.Now >= compactParameterUntil[strip]) ShowCompactMeter(strip); }
                else if (outExtender != null) outExtender.Send(0xB0, (byte)(90 + strip - 8), level);
            }
        }
        void SendMcuMeter(int strip, byte level) { MidiOutput o = strip < 8 ? outCompact : outExtender; if (o == null || strip >= 16) return; int meter = level == 0 ? 0 : Math.Min(13, 1 + level * 12 / 127); o.Send(0xD0, (byte)(((strip & 7) << 4) | meter), 0); }
        void SetCompactRingBehavior(int strip, byte behavior) { if (outCompact == null || strip < 0 || strip > 7) return; if (compactRingBehavior[strip] == behavior) return; outCompact.Send(0xB0, (byte)(10 + strip), behavior); compactRingBehavior[strip] = behavior; }
        void ShowCompactParameter(int strip) { if (outCompact == null || strip < 0 || strip > 7) return; SetCompactRingBehavior(strip, gainMode ? (byte)4 : (byte)1); outCompact.Send(0xB0, (byte)(26 + strip), (byte)Math.Max(1, Math.Min(13, 1 + compactParameter[strip] * 12 / 127))); }
        void ShowCompactMeter(int strip) { if (outCompact == null || strip < 0 || strip > 7) return; SetCompactRingBehavior(strip, 2); int pos = compactMeter[strip] == 0 ? 0 : 1 + compactMeter[strip] * 12 / 127; outCompact.Send(0xB0, (byte)(26 + strip), (byte)pos); }
        void RefreshCompactRings() { for (int i = 0; i < 8; i++) { if (compactRingMeters.Checked) { compactParameterUntil[i] = DateTime.MinValue; ShowCompactMeter(i); } else ShowCompactParameter(i); } }
        void DecodeNames(byte[] p)
        {
            if (outCompact == null && outExtender == null) return; int payload = p.Length - 10, width = payload / 16; if (width < 6 || width > 16) return;
            for (int strip = IconProfile ? 0 : 8; strip < 16; strip++)
            {
                int off = 9 + strip * width; string text = Encoding.ASCII.GetString(p, off, Math.Min(width, p.Length - 1 - off)).Replace('\0', ' ').Trim(); string top = text.Length > 7 ? text.Substring(0, 7) : text; string bottom = text.Length > 7 ? text.Substring(7, Math.Min(7, text.Length - 7)) : "";
                if (IconProfile) SendMcuName(strip, top, bottom);
                else { byte[] sx = new byte[23]; byte[] head = { 0xF0, 0x00, 0x20, 0x32, 0x15, 0x4C, (byte)(strip - 8), 0x07 }; Array.Copy(head, sx, head.Length); FillAscii(sx, 8, 7, top); FillAscii(sx, 15, 7, bottom); sx[22] = 0xF7; outExtender.SendSysEx(sx); }
            }
        }
        void SendMcuName(int strip, string top, string bottom)
        {
            MidiOutput o = strip < 8 ? outCompact : outExtender; if (o == null) return; int local = strip & 7;
            byte[] line1 = new byte[15], line2 = new byte[15]; byte device = strip < 8 ? (byte)0x14 : (byte)0x15;
            byte[] h1 = { 0xF0, 0x00, 0x00, 0x66, device, 0x12, (byte)(local * 7) }; byte[] h2 = { 0xF0, 0x00, 0x00, 0x66, device, 0x12, (byte)(56 + local * 7) };
            Array.Copy(h1, line1, h1.Length); Array.Copy(h2, line2, h2.Length); FillAscii(line1, 7, 7, top); FillAscii(line2, 7, 7, bottom); line1[14] = line2[14] = 0xF7; o.SendSysEx(line1); o.SendSysEx(line2);
        }
        static void FillAscii(byte[] dst, int off, int len, string s) { for (int i = 0; i < len; i++) dst[off + i] = (byte)(i < s.Length ? Math.Max(32, Math.Min(126, (int)s[i])) : 32); }

        void TestOutputs()
        {
            if (!running) { MessageBox.Show("Start the bridge first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            if (IconProfile) { for (int i = 0; i < 16; i++) { SetFader(i, (byte)(16 + (i & 7) * 14)); SetButton((byte)(32 + i), true); SendMcuMeter(i, (byte)(16 + (i & 7) * 14)); SendMcuName(i, "CH " + (i + 1), "LV1"); } Log("iCON MCU output test sent."); return; }
            for (int i = 0; i < 8; i++) { if (outCompact != null) { outCompact.Send(0xB0, (byte)(1 + i), (byte)(16 + i * 14)); outCompact.Send(0x90, (byte)(16 + i), 1); outCompact.Send(0xB0, (byte)(26 + i), (byte)(1 + i * 12 / 7)); } if (outExtender != null) { outExtender.Send(0xB0, (byte)(70 + i), (byte)(16 + i * 14)); outExtender.Send(0x90, (byte)(16 + i), 127); outExtender.Send(0xB0, (byte)(90 + i), (byte)(16 + i * 14)); } }
            Log("Output test sent.");
        }
        void UpdateStatus()
        {
            if (!running) { counters.Text = ""; return; }
            status.Text = receivedFromLv1 ? "Receiving MIDI from LV1" : "Virtual MIDI ready; no LV1 feedback";
            status.ForeColor = receivedFromLv1 ? Color.LightGreen : Color.Orange;
            double age = lastReceived == DateTime.MinValue ? -1 : (DateTime.Now - lastReceived).TotalSeconds;
            counters.Text = "LV1 RX " + fromLv1 + "   LV1 TX " + toLv1 + "   VU " + meterPackets
                + (age >= 0 ? "   last received " + age.ToString("0.0") + "s" : "   no data received this connection");
        }
        void Log(string s) { try { BeginInvoke((MethodInvoker)delegate { monitor.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine); }); } catch { } }

        void LoadSettings()
        {
            try { if (!File.Exists(settingsPath)) return; string[] lines = File.ReadAllLines(settingsPath); foreach (string line in lines) { int q = line.IndexOf('='); if (q > 0 && line.Substring(0, q) == "Profile") profile.SelectedIndex = line.Substring(q + 1) == "ICON" ? 1 : 0; } foreach (string line in lines) { int q = line.IndexOf('='); if (q < 0) continue; string k = line.Substring(0, q), v = line.Substring(q + 1); if (k == "LV1In") SelectText(lv1In, v); else if (k == "LV1Out") SelectText(lv1Out, v); else if (k == "CompactIn") SelectText(compactIn, v); else if (k == "CompactOut") SelectText(compactOut, v); else if (k == "ExtenderIn") SelectText(extenderIn, v); else if (k == "ExtenderOut") SelectText(extenderOut, v); else if (k == "EqOut") SelectText(eqOut, v); else if (k == "ToggleNote") toggleNote.Value = Clamp(v, 0, 127, 49); else if (k == "EqBase") eqBase.Value = Clamp(v, 0, 120, 102); else if (k == "EqChannel") eqChannel.Value = Clamp(v, 1, 16, 16); else if (k == "AutoStart") autoStart.Checked = v == "1"; else if (k == "EnableEq") enableEq.Checked = v == "1"; else if (k == "CompactRingMeters") compactRingMeters.Checked = v != "0"; else if (k == "Gain") gainMode = v == "1"; } } catch { }
        }
        void SaveSettings()
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)); File.WriteAllLines(settingsPath, new[] { "Profile=" + (IconProfile ? "ICON" : "BEHRINGER"), "LV1In=" + lv1In.Text, "LV1Out=" + lv1Out.Text, "CompactIn=" + compactIn.Text, "CompactOut=" + compactOut.Text, "ExtenderIn=" + extenderIn.Text, "ExtenderOut=" + extenderOut.Text, "EqOut=" + eqOut.Text, "ToggleNote=" + toggleNote.Value, "EqBase=" + eqBase.Value, "EqChannel=" + eqChannel.Value, "AutoStart=" + (autoStart.Checked ? "1" : "0"), "EnableEq=" + (enableEq.Checked ? "1" : "0"), "CompactRingMeters=" + (compactRingMeters.Checked ? "1" : "0"), "Gain=" + (gainMode ? "1" : "0") }); } catch { }
        }
        static int Clamp(string s, int min, int max, int fallback) { int v; return int.TryParse(s, out v) ? Math.Max(min, Math.Min(max, v)) : fallback; }
        static void SelectText(ComboBox box, string text) { if (String.IsNullOrEmpty(text)) return; for (int i = 0; i < box.Items.Count; i++) if (String.Equals(box.Items[i].ToString(), text, StringComparison.OrdinalIgnoreCase)) { box.SelectedIndex = i; return; } }
    }

    internal static class Program
    {
        public static bool StartHidden;
        [STAThread] static void Main()
        {
            foreach (string a in Environment.GetCommandLineArgs()) if (String.Equals(a, "--background", StringComparison.OrdinalIgnoreCase)) StartHidden = true;
            StopOlderBridgeInstances();
            // The native Bridge module now binds when LV1 creates or loads it.
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new MainForm());
        }
        static void StopOlderBridgeInstances()
        {
            try
            {
                int self = System.Diagnostics.Process.GetCurrentProcess().Id;
                string[] names = { "FaderBankXTouchBridge", "FaderBankBridge" };
                foreach (string name in names)
                    foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName(name))
                    {
                        try { if (p.Id != self) { p.Kill(); p.WaitForExit(3000); } }
                        catch { }
                        finally { p.Dispose(); }
                    }
                Thread.Sleep(500);
            }
            catch { }
        }
    }
}
