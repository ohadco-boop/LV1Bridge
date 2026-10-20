using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace FaderBankXTouchBridge
{
    internal sealed class BridgeApiServer : IDisposable
    {
        readonly Func<string, string> handler;
        TcpListener listener;
        Thread thread;
        volatile bool stopping;

        public BridgeApiServer(int port, Func<string, string> requestHandler)
        {
            handler = requestHandler;
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            thread = new Thread(ListenLoop); thread.IsBackground = true; thread.Name = "LV1 Stream Deck API"; thread.Start();
        }

        void ListenLoop()
        {
            while (!stopping)
            {
                try
                {
                    TcpClient c = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate { Serve(c); });
                }
                catch { if (!stopping) Thread.Sleep(100); }
            }
        }

        void Serve(TcpClient client)
        {
            using (client)
            {
                client.ReceiveTimeout = 1500; client.SendTimeout = 1500;
                try
                {
                    using (NetworkStream s = client.GetStream())
                    using (StreamReader r = new StreamReader(s, Encoding.UTF8, false, 1024, true))
                    using (StreamWriter w = new StreamWriter(s, new UTF8Encoding(false), 1024, true))
                    {
                        w.AutoFlush = true;
                        string line = r.ReadLine();
                        w.WriteLine(handler == null ? "ERR" : handler(line ?? ""));
                    }
                }
                catch { }
            }
        }

        public void Dispose()
        {
            stopping = true; try { listener.Stop(); } catch { }
            if (thread != null && thread.IsAlive) thread.Join(500);
        }
    }

    internal static class SessionNames
    {
        const int SQLITE_OPEN_READONLY = 1, SQLITE_ROW = 100;
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern int sqlite3_open_v2(byte[] file, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)] static extern int sqlite3_prepare_v2(IntPtr db, string sql, int bytes, out IntPtr stmt, IntPtr tail);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_step(IntPtr stmt);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern int sqlite3_column_int(IntPtr stmt, int col);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);

        public static void Load(string[] aux, string[] users)
        {
            for (int i = 0; i < aux.Length; i++) aux[i] = "AUX " + (i + 1);
            for (int i = 0; i < users.Length; i++) users[i] = "USER " + (i + 1);
            string publicProfile = Directory.GetParent(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments)).FullName;
            string p = Path.Combine(publicProfile, "Waves Audio", "eMotion", "Sessions", "CurrentLV1.dat");
            LoadFromFile(p, aux, users);
        }

        internal static void LoadFromFile(string p, string[] aux, string[] users)
        {
            if (!File.Exists(p)) return;
            IntPtr db;
            try
            {
                if (sqlite3_open_v2(Encoding.UTF8.GetBytes(p + "\0"), out db, SQLITE_OPEN_READONLY, IntPtr.Zero) != 0 || db == IntPtr.Zero) return;
                try
                {
                    // cluster_type in LV1 V17: 1=Group, 2=FX Aux, 25=Monitor sends.
                    // Deck AUX keys currently target the 24 monitor sends.
                    Query(db, "select obj_index,name from object where obj_type=25 and obj_index between 0 and 23", aux);
                    QueryUserKeys(db, users);
                }
                finally { sqlite3_close(db); }
            }
            catch (DllNotFoundException) { }
            catch (BadImageFormatException) { }
            catch { }
        }

        static void Query(IntPtr db, string sql, string[] dst)
        {
            IntPtr st;
            if (sqlite3_prepare_v2(db, sql, -1, out st, IntPtr.Zero) != 0 || st == IntPtr.Zero) return;
            try { while (sqlite3_step(st) == SQLITE_ROW) { int i = sqlite3_column_int(st, 0); string v = Text(st, 1); if (i >= 0 && i < dst.Length && !String.IsNullOrWhiteSpace(v)) dst[i] = v.Trim(); } }
            finally { sqlite3_finalize(st); }
        }

        static void QueryUserKeys(IntPtr db, string[] dst)
        {
            IntPtr st;
            const string sql = "select key_index,uk_friendly_name from user_keys where key_index between 0 and 15";
            if (sqlite3_prepare_v2(db, sql, -1, out st, IntPtr.Zero) != 0 || st == IntPtr.Zero) return;
            try { while (sqlite3_step(st) == SQLITE_ROW) { int i = sqlite3_column_int(st, 0); string v = Text(st, 1); if (i >= 0 && i < dst.Length && !String.IsNullOrWhiteSpace(v)) dst[i] = v.Trim(); } }
            finally { sqlite3_finalize(st); }
        }

        static string Text(IntPtr st, int col)
        {
            IntPtr p = sqlite3_column_text(st, col); if (p == IntPtr.Zero) return "";
            int length = 0; while (Marshal.ReadByte(p, length) != 0) length++;
            byte[] bytes = new byte[length]; Marshal.Copy(p, bytes, 0, length); return Encoding.UTF8.GetString(bytes);
        }

        public static string Pack(string value) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? "")); }
    }
}
