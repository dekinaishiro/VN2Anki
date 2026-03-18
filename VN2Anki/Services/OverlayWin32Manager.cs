using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using static VN2Anki.Services.Win32InteropService;

namespace VN2Anki.Services
{
    public class OverlayWin32Manager : IDisposable
    {
        private Window _window;
        private IntPtr _windowHandle;
        private IntPtr _webViewHandle;
        private SUBCLASSPROC _webViewSubclassProc;
        private DispatcherTimer _holdTimer;

        private Func<bool> _getIsPassThroughToggled;
        private Action<bool> _onPassThroughStateChanged;
        private int _modifierKeyVk;
        
        private bool _isHoldActive = false;
        private bool _isMouseOverHeader = false;

        public bool IsHoldActive => _isHoldActive;

        public OverlayWin32Manager(
            Window window, 
            int modifierKeyVk, 
            Func<bool> getIsPassThroughToggled, 
            Action<bool> onPassThroughStateChanged)
        {
            _window = window;
            _modifierKeyVk = modifierKeyVk;
            _getIsPassThroughToggled = getIsPassThroughToggled;
            _onPassThroughStateChanged = onPassThroughStateChanged;

            _webViewSubclassProc = new SUBCLASSPROC(WebViewSubclassProc);
        }

        public void Initialize(IntPtr windowHandle)
        {
            _windowHandle = windowHandle;
            var source = HwndSource.FromHwnd(_windowHandle);
            source?.AddHook(WndProc);

            SetupHoldTimer();
        }

        public void SetModifierKey(int modifierKeyVk)
        {
            _modifierKeyVk = modifierKeyVk;
        }

        public void InstallWebViewSubclass(IntPtr webViewHandle)
        {
            _webViewHandle = webViewHandle;
            if (_webViewHandle != IntPtr.Zero)
            {
                SetWindowSubclass(_webViewHandle, _webViewSubclassProc, 0, IntPtr.Zero);
            }
        }

        public void ForwardClick(int screenX, int screenY, int button)
        {
            POINT p = new POINT { X = screenX, Y = screenY };

            int style = GetWindowLong(_windowHandle, GWL_EXSTYLE);
            SetWindowLong(_windowHandle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT);
            
            IntPtr target = WindowFromPoint(p);
            
            SetWindowLong(_windowHandle, GWL_EXSTYLE, style);

            if (target != IntPtr.Zero && target != _windowHandle)
            {
                uint downMsg = button == 2 ? WM_RBUTTONDOWN : WM_LBUTTONDOWN;
                uint upMsg   = button == 2 ? WM_RBUTTONUP   : WM_LBUTTONUP;
                
                SetForegroundWindow(target);
                
                POINT clientP = p;
                ScreenToClient(target, ref clientP);
                
                IntPtr lParam = MakeLParam(clientP.X, clientP.Y);
                PostMessage(target, downMsg, IntPtr.Zero, lParam);
                PostMessage(target, upMsg,   IntPtr.Zero, lParam);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));

            IntPtr monitor = MonitorFromWindow(hwnd, 0x00000002); // MONITOR_DEFAULTTONEAREST
            if (monitor != IntPtr.Zero)
            {
                MONITORINFO info = new MONITORINFO();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                GetMonitorInfo(monitor, ref info);

                var workArea = info.rcWork;
                var monitorArea = info.rcMonitor;

                mmi.ptMaxPosition.X = workArea.left - monitorArea.left;
                mmi.ptMaxPosition.Y = workArea.top - monitorArea.top;
                mmi.ptMaxSize.X = workArea.right - workArea.left;
                mmi.ptMaxSize.Y = workArea.bottom - workArea.top;
                mmi.ptMaxTrackSize.X = mmi.ptMaxSize.X;
                mmi.ptMaxTrackSize.Y = mmi.ptMaxSize.Y;
            }

            Marshal.StructureToPtr(mmi, lParam, true);
        }

        private IntPtr WebViewSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == WM_NCHITTEST)
            {
                bool finalPassThrough = _getIsPassThroughToggled() ^ _isHoldActive;
                if (finalPassThrough && !_isMouseOverHeader) return (IntPtr)HTTRANSPARENT;
            }
            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }

        private void SetupHoldTimer()
        {
            _holdTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _holdTimer.Tick += (s, e) =>
            {
                bool isKeyDown = (GetAsyncKeyState(_modifierKeyVk) & 0x8000) != 0;
                
                if (isKeyDown != _isHoldActive)
                {
                    _isHoldActive = isKeyDown;
                    bool finalPassThrough = _getIsPassThroughToggled() ^ _isHoldActive;
                    _onPassThroughStateChanged(finalPassThrough);
                    ApplyWindowExStyle();
                }

                bool finalPassThroughNow = _getIsPassThroughToggled() ^ _isHoldActive;
                if (finalPassThroughNow)
                {
                    GetCursorPos(out POINT p);

                    try
                    {
                        Point mouseRelative = _window.PointFromScreen(new Point(p.X, p.Y));

                        bool isOverHeader = (mouseRelative.X >= 0 && mouseRelative.X <= _window.ActualWidth &&
                                             mouseRelative.Y >= 0 && mouseRelative.Y <= 40);

                        if (isOverHeader != _isMouseOverHeader)
                        {
                            _isMouseOverHeader = isOverHeader;
                            ApplyWindowExStyle(); 
                        }
                    }
                    catch (Exception) { }
                }
                else
                {
                    _isMouseOverHeader = false;
                }
            };
            _holdTimer.Start();
        }

        public void ApplyWindowExStyle()
        {
            if (_windowHandle == IntPtr.Zero) return;

            int extendedStyle = GetWindowLong(_windowHandle, GWL_EXSTYLE);
            bool finalPassThrough = _getIsPassThroughToggled() ^ _isHoldActive;

            if (finalPassThrough && !_isMouseOverHeader)
            {
                SetWindowLong(_windowHandle, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
            }
            else
            {
                SetWindowLong(_windowHandle, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
            }
        }

        public void Dispose()
        {
            _holdTimer?.Stop();
            if (_webViewHandle != IntPtr.Zero)
            {
                RemoveWindowSubclass(_webViewHandle, _webViewSubclassProc, 0);
                _webViewHandle = IntPtr.Zero;
            }
        }
    }
}