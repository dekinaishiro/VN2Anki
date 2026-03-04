using System;
using System.Windows;
using VN2Anki.Services.Interfaces;

namespace VN2Anki.Services
{
    public class WpfDispatcherService : IDispatcherService
    {
        public void Invoke(Action action)
        {
            Application.Current.Dispatcher.Invoke(action);
        }
    }
}