using System;
using System.Threading.Tasks;
using Windows.Graphics.Capture;

namespace TestWinRT
{
    public class Test
    {
        public static void Run(IntPtr hwnd)
        {
            var item = GraphicsCaptureItem.CreateFromVisual(null); // Just checking if it exists
        }
    }
}
