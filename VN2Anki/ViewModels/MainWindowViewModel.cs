using System.Threading;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using VN2Anki.Locales;
using VN2Anki.Messages;
using VN2Anki.Services;
using Microsoft.Extensions.DependencyInjection;
using VN2Anki.Data;
using VN2Anki.Models.Entities;

namespace VN2Anki.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject, IRecipient<StatusMessage>, IRecipient<PlayVnMessage>
    {
        private readonly MiningService _miningService;
        private readonly IConfigurationService _configService;
        private readonly AnkiExportService _ankiExportService;
        private readonly AnkiHandler _ankiHandler;
        private CancellationTokenSource _pollingCts;

        public SessionTracker Tracker { get; }

        [ObservableProperty]
        private string _statusText = "";

        [ObservableProperty]
        private Visibility _statusVisibility = Visibility.Collapsed;

        [ObservableProperty]
        private Visibility _miniStatsVisibility = Visibility.Visible;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(BufferBtnText))]
        [NotifyPropertyChangedFor(nameof(BufferBtnBackground))]
        private bool _isBufferActive;

        private readonly VideoEngine _videoEngine;

        [ObservableProperty]
        private VN2Anki.Models.Entities.VisualNovel _currentVN;

        public string BufferBtnText => IsBufferActive ? "ON" : "OFF";
        public Brush BufferBtnBackground => IsBufferActive ? Brushes.Green : Brushes.Crimson;

        public MainWindowViewModel(SessionTracker tracker, MiningService miningService, IConfigurationService configService, AnkiExportService ankiExportService, AnkiHandler ankiHandler, VideoEngine videoEngine)
        {
            Tracker = tracker;
            _miningService = miningService;
            _configService = configService;
            _ankiExportService = ankiExportService;
            _ankiHandler = ankiHandler;
            _videoEngine = videoEngine;

            _miningService.OnBufferStoppedUnexpectedly += () =>
            {
                Application.Current.Dispatcher.Invoke(() => IsBufferActive = false);
            };

            WeakReferenceMessenger.Default.RegisterAll(this); // listen to multiple message types
        }

        public void ApplyConfigToServices()
        {
            var config = _configService.CurrentConfig;
            _miningService.TargetVideoWindow = config.Media.VideoWindow;
            if (int.TryParse(config.Session.MaxSlots, out int parsedMax) && parsedMax > 0) _miningService.MaxSlots = parsedMax;
            if (double.TryParse(config.Session.IdleTime, out double parsedIdle) && parsedIdle > 0) _miningService.IdleTimeoutFixo = parsedIdle;

            _miningService.UseDynamicTimeout = config.Session.UseDynamicTimeout;
            _miningService.MaxImageWidth = config.Media.MaxImageWidth;
            _ankiHandler.UpdateSettings(config.Anki.Url, config.Anki.TimeoutSeconds);
        }

        [RelayCommand]
        private void ToggleBuffer()
        {
            if (!IsBufferActive)
            {
                var config = _configService.CurrentConfig;

                if (string.IsNullOrEmpty(config.Media.VideoWindow))
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Video Source vazia! Selecione o vídeo.", IsError = true }));
                    return;
                }

                // checks if the process is still running, if not, clear the config and alert the user
                var procs = System.Diagnostics.Process.GetProcessesByName(config.Media.VideoWindow);
                bool isRunning = procs.Any(p => p.MainWindowHandle != IntPtr.Zero);
                foreach (var p in procs) p.Dispose();

                if (!isRunning)
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "A janela alvo está fechada! Abra o jogo e tente novamente.", IsError = true }));
                    return;
                }

                var devices = _miningService.Audio.GetDevices();
                var deviceId = devices.FirstOrDefault(d => d.Name == config.Media.AudioDevice)?.Id;

                if (string.IsNullOrEmpty(deviceId))
                {
                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Configure o Áudio primeiro!", IsError = true }));
                    return;
                }

                _miningService.StartBuffer(deviceId);
                IsBufferActive = true;
            }
            else
            {
                _miningService.StopBuffer();
                IsBufferActive = false;
            }
        }

        [RelayCommand]
        private void ToggleStats()
        {
            MiniStatsVisibility = MiniStatsVisibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        [RelayCommand]
        private async Task MiniQuickAddAsync()
        {
            if (_miningService.HistorySlots.Count == 0)
            {
                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = Strings.MsgEmpty, IsError = true }));
                return;
            }

            var config = _configService.CurrentConfig;
            if (string.IsNullOrEmpty(config.Anki.Deck))
            {
                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Please configure the Deck first!", IsError = true }));
                return;
            }

            var slot = _miningService.HistorySlots[0];
            var result = await _ankiExportService.ExportSlotAsync(slot, config.Anki, config.Media);

            WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload
            {
                Message = result.success ? "Card Updated" : "Error Updating",
                IsError = !result.success
            }));
        }

        public async Task ExportSlotAsync(Models.MiningSlot slot)
        {
            if (slot == null) return;

            var config = _configService.CurrentConfig;
            if (string.IsNullOrEmpty(config.Anki.Deck))
            {
                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Please configure the Deck first!", IsError = true }));
                return;
            }

            var result = await _ankiExportService.ExportSlotAsync(slot, config.Anki, config.Media);

            WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload
            {
                Message = result.success ? "Card Updated" : "Error Updating",
                IsError = !result.success
            }));
        }

        public void EndSession()
        {
            // verify if there's progress (chars)
            bool hasProgress = Tracker.Elapsed.TotalSeconds > 0 || Tracker.ValidCharacterCount > 0;

            if (hasProgress)
            {
                int? vnIdToSave = CurrentVN?.Id;

                // progress but no VN linked
                if (CurrentVN == null)
                {
                    var result = MessageBox.Show(
                        "Você leu alguns caracteres, mas nenhuma Visual Novel está selecionada.\nDeseja salvar este progresso no histórico para vinculá-lo a um jogo mais tarde?",
                        "Salvar Sessão Órfã?",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.No)
                    {
                        hasProgress = false; // Cancela o salvamento da sessão
                    }
                }

                // Se tinha VN ou o usuário clicou em 'Sim' no prompt acima, salva no banco
                if (hasProgress)
                {
                    using (var scope = App.Current.Services.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var record = new SessionRecord
                        {
                            VisualNovelId = vnIdToSave,
                            StartTime = System.DateTime.Now - Tracker.Elapsed,
                            EndTime = System.DateTime.Now,
                            DurationSeconds = (int)Tracker.Elapsed.TotalSeconds,
                            CharactersRead = Tracker.ValidCharacterCount,
                            CardsMined = 0
                        };
                        db.Sessions.Add(record);
                        db.SaveChanges();
                    }

                    WeakReferenceMessenger.Default.Send(new SessionSavedMessage());
                }
            }

            // cleaning routine 
            if (IsBufferActive) ToggleBuffer();
            Tracker.Reset();
            foreach (var slot in _miningService.HistorySlots) slot.Dispose();
            _miningService.HistorySlots.Clear();

            CurrentVN = null;

            StatusText = Strings.StatusSessionEnded;
            StatusVisibility = Visibility.Visible;
        }

        public void Receive(StatusMessage message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusText = message.Value;
                StatusVisibility = string.IsNullOrEmpty(message.Value) ? Visibility.Collapsed : Visibility.Visible;
            });
        }

        public async void Receive(PlayVnMessage message)
        {
            var vn = message.VisualNovel;

            // verifies if current session is active and prompts the user to confirm if they want to end it before starting a new one
            if (CurrentVN != null && (Tracker.ValidCharacterCount > 0 || Tracker.Elapsed.TotalSeconds > 0 || IsBufferActive))
            {
                var result = MessageBox.Show($"Uma sessão está ativa com '{CurrentVN.Title}'.\nDeseja encerrá-la e iniciar outra com '{vn.Title}'?", "Atenção", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    EndSession();
                }
                else
                {
                    return;
                }
            }

            // polling duplicate prevention: cancels any existing polling loop before starting a new one
            _pollingCts?.Cancel();
            _pollingCts = new CancellationTokenSource();
            var token = _pollingCts.Token;

            CurrentVN = vn;

            if (IsBufferActive) ToggleBuffer();

            if (string.IsNullOrEmpty(vn.ExecutablePath) || !System.IO.File.Exists(vn.ExecutablePath))
            {
                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Executável não encontrado!", IsError = true }));
                CurrentVN = null;
                return;
            }

            try
            {
                var pInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = vn.ExecutablePath,
                    WorkingDirectory = System.IO.Path.GetDirectoryName(vn.ExecutablePath),
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(pInfo);
            }
            catch (System.Exception)
            {
                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Erro ao abrir o jogo!", IsError = true }));
                CurrentVN = null;
                return;
            }

            WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = $"Aguardando janela: {vn.Title}...", IsError = false }));

            bool windowFound = false;

            try
            {
                // token injected delay loop to wait for the game window to appear
                // timeout is harded coded for now
                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(1000, token); // Passa o token para a espera!

                    var windows = _videoEngine.GetWindows();
                    var targetWin = windows.FirstOrDefault(w => w.ExecutablePath == vn.ExecutablePath || w.ProcessName == vn.ProcessName);

                    if (targetWin != null)
                    {
                        var config = _configService.CurrentConfig;
                        config.Media.VideoWindow = targetWin.ProcessName;
                        _configService.Save();

                        _miningService.TargetVideoWindow = targetWin.ProcessName;
                        windowFound = true;

                        WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Vídeo conectado! Pronto para o Play.", IsError = false }));
                        break;
                    }
                }
            }
            catch (TaskCanceledException)
            {
                // prevents overlapping polls
                return;
            }

            if (!windowFound)
            {
                WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = "Timeout: A janela não apareceu.", IsError = true }));
                CurrentVN = null;

                var config = _configService.CurrentConfig;
                config.Media.VideoWindow = string.Empty;
                _configService.Save();
                _miningService.TargetVideoWindow = string.Empty;
            }
        }

        public async Task CheckRunningVNsAsync()
        {
            await Task.Delay(1500);

            List<VN2Anki.Models.Entities.VisualNovel> vns = null;

            using (var scope = App.Current.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<VN2Anki.Data.AppDbContext>();
                vns = db.VisualNovels.ToList();
            }

            if (vns == null || vns.Count == 0) return;

            var windows = _videoEngine.GetWindows();

            // NOVO: Lista para armazenar todos os matches encontrados
            var matchedVns = new System.Collections.Generic.List<VN2Anki.Models.Entities.VisualNovel>();

            foreach (var win in windows)
            {
                var match = vns.FirstOrDefault(v =>
                    (v.ExecutablePath == win.ExecutablePath && !string.IsNullOrEmpty(win.ExecutablePath)) ||
                    (v.ProcessName == win.ProcessName && !string.IsNullOrEmpty(win.ProcessName)));

                // Evita adicionar a mesma VN duas vezes se o jogo criar múltiplos processos iguais
                if (match != null && !matchedVns.Any(v => v.Id == match.Id))
                {
                    matchedVns.Add(match);
                }
            }

            if (matchedVns.Count == 0) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                VN2Anki.Models.Entities.VisualNovel selectedVn = null;

                if (matchedVns.Count == 1)
                {
                    // Fluxo normal: Apenas 1 VN encontrada
                    var result = MessageBox.Show(
                        $"Detectamos que '{matchedVns[0].Title}' está em execução.\nDeseja vinculá-la e iniciar a sessão agora?",
                        "Visual Novel Detectada", MessageBoxButton.YesNo, MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes) selectedVn = matchedVns[0];
                }
                else
                {
                    // Fluxo Múltiplo: Constrói uma janela de seleção dinamicamente para não poluir o projeto com mais arquivos XAML
                    var win = new Window
                    {
                        Title = "Múltiplas VNs Detectadas",
                        Width = 350,
                        Height = 200,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        WindowStyle = WindowStyle.ToolWindow,
                        ResizeMode = ResizeMode.NoResize,
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252526")),
                        Foreground = Brushes.White
                    };

                    var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(15) };

                    stack.Children.Add(new System.Windows.Controls.TextBlock
                    {
                        Text = "Múltiplas VNs em execução detectadas.\nSelecione qual deseja vincular:",
                        Foreground = Brushes.White,
                        Margin = new Thickness(0, 0, 0, 15)
                    });

                    var combo = new System.Windows.Controls.ComboBox
                    {
                        ItemsSource = matchedVns,
                        DisplayMemberPath = "Title",
                        SelectedIndex = 0,
                        Margin = new Thickness(0, 0, 0, 15),
                        Padding = new Thickness(5)
                    };
                    stack.Children.Add(combo);

                    var btn = new System.Windows.Controls.Button
                    {
                        Content = "Vincular Selecionada",
                        Padding = new Thickness(10, 8, 10, 8),
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC")),
                        Foreground = Brushes.White,
                        BorderThickness = new Thickness(0),
                        Cursor = System.Windows.Input.Cursors.Hand
                    };

                    btn.Click += (s, e) => { selectedVn = combo.SelectedItem as VN2Anki.Models.Entities.VisualNovel; win.DialogResult = true; win.Close(); };
                    stack.Children.Add(btn);

                    win.Content = stack;
                    win.ShowDialog();
                }

                // Se o usuário selecionou algo (seja no Yes/No ou no ComboBox), fazemos o bind!
                if (selectedVn != null)
                {
                    if (CurrentVN != null) EndSession();

                    CurrentVN = selectedVn;

                    // Acha a janela correspondente ao selectedVn para pegar o ProcessName correto
                    var targetWin = windows.First(w => w.ExecutablePath == selectedVn.ExecutablePath || w.ProcessName == selectedVn.ProcessName);

                    var config = _configService.CurrentConfig;
                    config.Media.VideoWindow = targetWin.ProcessName;
                    _configService.Save();

                    _miningService.TargetVideoWindow = targetWin.ProcessName;

                    WeakReferenceMessenger.Default.Send(new ShowFlashMessage(new FlashMessagePayload { Message = $"Sessão vinculada: {selectedVn.Title}", IsError = false }));
                }
            });
        }
    }
}