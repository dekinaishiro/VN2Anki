using System;
using System.Windows;
using System.Windows.Threading;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class WpfDispatcherService : IDispatcherService
    {
        private readonly Dispatcher _dispatcher;

        public WpfDispatcherService()
        {
            _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        public void Invoke(Action action)
        {
            if (_dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                _dispatcher.Invoke(action);
            }
        }
    }
}