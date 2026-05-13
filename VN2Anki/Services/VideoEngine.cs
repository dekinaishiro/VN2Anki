using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.DirectX;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using VN2Anki.Helpers;

namespace VN2Anki.Services
{
    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
    }

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

        [DllImport("d3d11.dll", EntryPoint = "D3D11CreateDevice", SetLastError = false, ExactSpelling = true)]
        private static extern int D3D11CreateDevice(IntPtr pAdapter, int driverType, IntPtr Software, uint flags, IntPtr pFeatureLevels, uint FeatureLevels, uint SDKVersion, out IntPtr ppDevice, out IntPtr pFeatureLevel, out IntPtr ppImmediateContext);

        [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = false, ExactSpelling = true)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

        public async Task<byte[]?> CaptureWindowAsync(string processName, int maxWidth = 0)
        {
            if (string.IsNullOrEmpty(processName)) return null;

            IntPtr hWnd = IntPtr.Zero;
            var procs = Process.GetProcessesByName(processName);

            foreach (var p in procs)
            {
                if (hWnd == IntPtr.Zero && p.MainWindowHandle != IntPtr.Zero)
                    hWnd = p.MainWindowHandle;
                p.Dispose();
            }

            if (hWnd == IntPtr.Zero) return null;

            // Tentativa de Captura Moderna via WinRT (Hardware Accelerated Support)
            if (GraphicsCaptureSession.IsSupported())
            {
                try
                {
                    return await CaptureWindowWinRTAsync(hWnd, maxWidth);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[VideoEngine] WinRT Capture failed, falling back to PrintWindow: {ex.Message}");
                    // Fallback to legacy PrintWindow on error
                }
            }

            return CaptureWindowLegacy(hWnd, maxWidth);
        }

        private async Task<byte[]?> CaptureWindowWinRTAsync(IntPtr hWnd, int maxWidth)
        {
            var factoryObj = WinRT.ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
            var factoryRcw = Marshal.GetObjectForIUnknown(factoryObj.ThisPtr);
            var interop = (IGraphicsCaptureItemInterop)factoryRcw;
            
            var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            IntPtr itemPointer = interop.CreateForWindow(hWnd, ref iid);
            var item = WinRT.MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
            Marshal.Release(itemPointer);

            var device = CreateWinRTD3D11Device();

            var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                item.Size);

            var session = framePool.CreateCaptureSession(item);
            var tcs = new TaskCompletionSource<SoftwareBitmap>();

            framePool.FrameArrived += async (s, e) =>
            {
                var frame = s.TryGetNextFrame();
                if (frame == null) return;

                if (tcs.Task.IsCompleted)
                {
                    frame.Dispose();
                    return;
                }

                try
                {
                    var bmp = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface);
                    tcs.TrySetResult(bmp);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
                finally
                {
                    frame.Dispose();
                }
            };

            session.StartCapture();

            // Timeout de 2 segundos caso a janela esteja invisivel/minimizada sem redrawn
            var timeoutTask = Task.Delay(2000);
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            session.Dispose();
            framePool.Dispose();
            device.Dispose();

            if (completedTask == timeoutTask)
            {
                throw new TimeoutException("GraphicsCaptureSession did not produce a frame in time.");
            }

            var softwareBitmap = await tcs.Task;

            using var ms = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, ms);
            encoder.SetSoftwareBitmap(softwareBitmap);

            if (maxWidth > 0 && softwareBitmap.PixelWidth > maxWidth)
            {
                encoder.BitmapTransform.ScaledWidth = (uint)maxWidth;
                encoder.BitmapTransform.ScaledHeight = (uint)((float)softwareBitmap.PixelHeight * ((float)maxWidth / softwareBitmap.PixelWidth));
                encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
            }

            await encoder.FlushAsync();
            softwareBitmap.Dispose();

            using var dataReader = new DataReader(ms.GetInputStreamAt(0));
            var bytes = new byte[ms.Size];
            await dataReader.LoadAsync((uint)ms.Size);
            dataReader.ReadBytes(bytes);
            return bytes;
        }

        private IDirect3DDevice CreateWinRTD3D11Device()
        {
            int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out IntPtr d3dDevicePtr, out _, out _);
            if (hr != 0) throw new Exception($"Failed to create D3D11 Device (HR: {hr})");

            var dxgiDeviceIid = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            hr = Marshal.QueryInterface(d3dDevicePtr, ref dxgiDeviceIid, out IntPtr dxgiDevicePtr);
            if (hr != 0)
            {
                Marshal.Release(d3dDevicePtr);
                throw new Exception($"Failed to query IDXGIDevice (HR: {hr})");
            }

            hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevicePtr, out IntPtr winrtDevicePtr);
            if (hr != 0)
            {
                Marshal.Release(dxgiDevicePtr);
                Marshal.Release(d3dDevicePtr);
                throw new Exception($"Failed to create WinRT D3D Device (HR: {hr})");
            }

            var device = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(winrtDevicePtr);
            Marshal.Release(winrtDevicePtr);
            Marshal.Release(dxgiDevicePtr);
            Marshal.Release(d3dDevicePtr);

            return device;
        }

        private byte[]? CaptureWindowLegacy(IntPtr hWnd, int maxWidth = 0)
        {
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
    }
}