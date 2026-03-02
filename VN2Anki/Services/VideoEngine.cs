using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Drawing.Drawing2D;

namespace VN2Anki.Services
{
    public class VideoEngine
    {
        private const uint PW_RENDERFULLCONTENT = 0x00000002;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; public int Width => Right - Left; public int Height => Bottom - Top; }

        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
        [DllImport("user32.dll")] private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

        private string GetProcessFilename(Process p)
        {
            try { return p.MainModule?.FileName; } catch { /* ignores and tries fallback */ }

            int capacity = 2000;
            StringBuilder sb = new StringBuilder(capacity);
            IntPtr ptr = OpenProcess(0x1000, false, p.Id);
            if (ptr != IntPtr.Zero)
            {
                uint size = (uint)capacity;
                if (QueryFullProcessImageName(ptr, 0, sb, ref size))
                {
                    CloseHandle(ptr);
                    return sb.ToString();
                }
                CloseHandle(ptr);
            }
            return null;
        }

        public List<VideoWindowItem> GetWindows()
        {
            var windows = new List<VideoWindowItem>();
            var processes = Process.GetProcesses();

            foreach (var p in processes)
            {
                if (p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle))
                {
                    if (!windows.Exists(w => w.ProcessName == p.ProcessName))
                    {
                        string exePath = GetProcessFilename(p);
                        windows.Add(new VideoWindowItem
                        {
                            Title = p.MainWindowTitle,
                            ProcessName = p.ProcessName,
                            ExecutablePath = exePath
                        });
                    }
                }
                p.Dispose();
            }
            windows.Sort((a, b) => a.DisplayName.CompareTo(b.DisplayName));
            return windows;
        }

        public byte[] CaptureWindow(string processName, int maxWidth = 0)
        {
            if (string.IsNullOrEmpty(processName)) return null;

            IntPtr hWnd = IntPtr.Zero;
            var procs = Process.GetProcessesByName(processName);

            foreach (var p in procs)
            {
                // finds the first visible window ofr the process
                if (hWnd == IntPtr.Zero && p.MainWindowHandle != IntPtr.Zero)
                    hWnd = p.MainWindowHandle;

                p.Dispose(); // avoid memory leaks by disposing process objects instead of relying on GC
            }

            if (hWnd == IntPtr.Zero) return null;

            GetWindowRect(hWnd, out RECT rect);
            if (rect.Width <= 0 || rect.Height <= 0) return null;

            using (Bitmap bmp = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics gfxBmp = Graphics.FromImage(bmp))
                {
                    IntPtr hdcBitmap = gfxBmp.GetHdc();
                    bool success = PrintWindow(hWnd, hdcBitmap, PW_RENDERFULLCONTENT);
                    gfxBmp.ReleaseHdc(hdcBitmap);
                    if (!success) return null;
                }

                if (maxWidth > 0 && bmp.Width > maxWidth)
                {
                    int newWidth = maxWidth;
                    int newHeight = (int)((float)bmp.Height * ((float)newWidth / bmp.Width)); 

                    using (Bitmap resizedBmp = new Bitmap(newWidth, newHeight))
                    {
                        using (Graphics g = Graphics.FromImage(resizedBmp))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(bmp, 0, 0, newWidth, newHeight);
                        }
                        using (MemoryStream ms = new MemoryStream())
                        {
                            resizedBmp.Save(ms, ImageFormat.Jpeg);
                            return ms.ToArray();
                        }
                    }
                }

                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Jpeg);
                    return ms.ToArray();
                }
            }
        }

        public class VideoWindowItem
        {
            public string Title { get; set; }
            public string ProcessName { get; set; }
            public string ExecutablePath { get; set; }
            public string DisplayName => string.IsNullOrWhiteSpace(Title) ? ProcessName : $"{Title} ({ProcessName}.exe)";
        }
    }
}