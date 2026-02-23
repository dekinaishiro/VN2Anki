using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using VN2Anki.Models;

namespace VN2Anki
{
    public partial class MiningWindow : Window
    {
        private static MiningWindow _instance;
        private static Action<MiningSlot> _onMineAction;
        private static Action<MiningSlot> _onDeleteAction; 

        public static void ShowWindow(ObservableCollection<MiningSlot> history, Action<MiningSlot> onMineAction, Action<MiningSlot> onDeleteAction)
        {
            _onMineAction = onMineAction;
            _onDeleteAction = onDeleteAction;

            if (_instance == null)
            {
                _instance = new MiningWindow(history);
                _instance.Show();
            }
            else
            {
                if (_instance.WindowState == WindowState.Minimized)
                    _instance.WindowState = WindowState.Normal;
                _instance.Activate();
            }
        }

        private MiningWindow(ObservableCollection<MiningSlot> history)
        {
            InitializeComponent();
            ListHistory.ItemsSource = history;
        }

        private void BtnMiner_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.Tag is MiningSlot slot) _onMineAction?.Invoke(slot);
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.Tag is MiningSlot slot) _onDeleteAction?.Invoke(slot);
        }

        protected override void OnClosed(EventArgs e)
        {
            _instance = null;
            base.OnClosed(e);
        }
    }
}