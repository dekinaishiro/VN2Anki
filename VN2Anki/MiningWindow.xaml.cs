using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Extensions.DependencyInjection;
using VN2Anki.Models;
using VN2Anki.Services;
using VN2Anki.Services.Interfaces;

namespace VN2Anki
{
    public partial class MiningWindow : Window
    {
        private static MiningWindow _instance;
        private static Action<MiningSlot> _onDeleteAction;

        private readonly IAudioPlaybackService _audioPlaybackService;
        private readonly IBridgeService _bridgeService;
        private readonly IConfigurationService _configService;
        private readonly ObservableCollection<MiningSlot> _history;

        public static void ShowWindow(ObservableCollection<MiningSlot> history, Action<MiningSlot> onDeleteAction)
        {
            _onDeleteAction = onDeleteAction;

            if (_instance == null)
            {
                _instance = new MiningWindow(history);
                _instance.Show();
            }
            else
            {
                _instance.Show();
                if (_instance.WindowState == WindowState.Minimized)
                    _instance.WindowState = WindowState.Normal;
                _instance.Activate();
            }
        }

        private MiningWindow(ObservableCollection<MiningSlot> history)
        {
            InitializeComponent();
            _history = history;

            _audioPlaybackService = App.Current.Services.GetRequiredService<IAudioPlaybackService>();
            _bridgeService = App.Current.Services.GetRequiredService<IBridgeService>();
            _configService = App.Current.Services.GetRequiredService<IConfigurationService>();

            CreateDynamicHtml();
            InitializeWebViewAsync();

            _history.CollectionChanged += History_CollectionChanged;
            foreach (var slot in _history)
            {
                slot.PropertyChanged += Slot_PropertyChanged;
            }
        }

        private void CreateDynamicHtml()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string vn2ankiFolder = Path.Combine(appData, "VN2Anki");
            if (!Directory.Exists(vn2ankiFolder)) Directory.CreateDirectory(vn2ankiFolder);
            string htmlPath = Path.Combine(vn2ankiFolder, "mining.html");

            string htmlBase = @"
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset='utf-8'>
                <style>
                    body { background-color: #1E1E1E; color: white; font-family: 'Segoe UI', sans-serif; margin: 10px; }
                    .slot { display: flex; align-items: center; background: #2D2D30; border: 2px solid transparent; border-radius: 8px; margin: 5px; padding: 10px; }
                    .slot.open { background: #1E3323; border-color: #28A745; }
                    .thumbnail { width: 150px; object-fit: contain; margin-right: 15px; }
                    .content { flex: 1; margin-right: 10px; display: flex; flex-direction: column; justify-content: center; }
                    .time { color: #007ACC; font-weight: bold; font-size: 12px; margin-bottom: 2px; }
                    .text { color: #EAEAEA; font-size: 16px; word-wrap: break-word; overflow: hidden; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; }
                    .status { color: #FF5555; font-weight: bold; font-size: 12px; margin-top: 4px; display: none; }
                    .slot.open .status { display: block; }
                    .btn { padding: 8px 12px; color: white; font-weight: bold; border: none; cursor: pointer; margin-right: 5px; border-radius: 4px; display: flex; align-items: center; justify-content: center; font-size: 16px; }
                    .btn-play { background: #28A745; }
                    .btn-delete { background: #DC3545; margin-right: 0; }
                    .text-container { position: relative; display: inline-block; cursor: text; }
                </style>
            </head>
            <body>
                <div id='slots-container'></div>
                <script>
                    function renderSlots(slots) {
                        const container = document.getElementById('slots-container');
                        
                        // First, create a map of existing slot elements to avoid full re-renders
                        const existingSlots = {};
                        Array.from(container.children).forEach(child => {
                            if (child.dataset.id) {
                                existingSlots[child.dataset.id] = child;
                            }
                        });

                        // Keep track of IDs in the new state
                        const currentIds = new Set();

                        slots.forEach((slot, index) => {
                            currentIds.add(slot.id);
                            
                            let div = existingSlots[slot.id];
                            let isNew = false;
                            
                            if (!div) {
                                isNew = true;
                                div = document.createElement('div');
                                div.dataset.id = slot.id;
                                
                                const img = document.createElement('img');
                                img.className = 'thumbnail';
                                div.appendChild(img);

                                const content = document.createElement('div');
                                content.className = 'content';

                                const time = document.createElement('div');
                                time.className = 'time';
                                content.appendChild(time);

                                const textContainer = document.createElement('div');
                                textContainer.className = 'text-container';
                                
                                textContainer.addEventListener('mouseenter', () => {
                                    window.chrome.webview.postMessage(JSON.stringify({ action: 'hover', id: slot.id }));
                                });

                                const status = document.createElement('div');
                                status.className = 'status';
                                status.innerText = 'Recording Audio...'; 
                                
                                content.appendChild(textContainer);
                                content.appendChild(status);
                                div.appendChild(content);

                                const btnPlay = document.createElement('button');
                                btnPlay.className = 'btn btn-play';
                                btnPlay.innerText = '▶';
                                btnPlay.title = 'Play Audio';
                                btnPlay.onclick = () => window.chrome.webview.postMessage(JSON.stringify({ action: 'play', id: slot.id }));

                                const btnDelete = document.createElement('button');
                                btnDelete.className = 'btn btn-delete';
                                btnDelete.innerText = '🗑';
                                btnDelete.title = 'Delete slot';
                                btnDelete.onclick = () => window.chrome.webview.postMessage(JSON.stringify({ action: 'delete', id: slot.id }));

                                div.appendChild(btnPlay);
                                div.appendChild(btnDelete);
                            }

                            // Update properties unconditionally to handle state changes
                            div.className = 'slot' + (slot.isOpen ? ' open' : '');
                            
                            const img = div.querySelector('.thumbnail');
                            if (slot.thumbnailUrl && img.src !== slot.thumbnailUrl) {
                                img.src = slot.thumbnailUrl;
                            } else if (!slot.thumbnailUrl) {
                                img.removeAttribute('src');
                            }

                            const time = div.querySelector('.time');
                            if (time.innerText !== slot.displayTime) time.innerText = slot.displayTime;

                            // Insert into DOM at correct position first
                            if (isNew) {
                                if (index >= container.children.length) {
                                    container.appendChild(div);
                                } else {
                                    container.insertBefore(div, container.children[index]);
                                }
                                
                                // To trigger Jiten Reader's MutationObserver, we must add the text element *after* the parent is in the document
                                const textContainer = div.querySelector('.text-container');
                                const text = document.createElement('span');
                                text.className = 'text vn-text-line';
                                text.innerText = slot.text;
                                textContainer.appendChild(text);
                            } else if (container.children[index] !== div) {
                                container.insertBefore(div, container.children[index]);
                            }
                        });

                        // Remove slots that no longer exist
                        Array.from(container.children).forEach(child => {
                            if (!currentIds.has(child.dataset.id)) {
                                container.removeChild(child);
                            }
                        });
                    }

                    window.chrome.webview.addEventListener('message', event => {
                        try {
                            const data = event.data;
                            if (data && data.action === 'updateSlots') {
                                renderSlots(data.slots);
                            }
                        } catch (e) {
                            console.error('Error handling message:', e);
                        }
                    });
                </script>
            </body>
            </html>";

            File.WriteAllText(htmlPath, htmlBase);
        }

        private async void InitializeWebViewAsync()
        {
            try
            {
                var environment = await VN2Anki.Helpers.BrowserExtensionHelper.GetSharedEnvironmentAsync();
                await webView.EnsureCoreWebView2Async(environment);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing WebView2: {ex.Message}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LoadExtensions();

            webView.WebMessageReceived += WebView_WebMessageReceived;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string vn2ankiFolder = Path.Combine(appData, "VN2Anki");
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping("vn.local", vn2ankiFolder, CoreWebView2HostResourceAccessKind.Allow);
            webView.CoreWebView2.Navigate($"http://vn.local/mining.html?t={DateTime.Now.Ticks}");

            webView.NavigationCompleted += (s, e) =>
            {
                UpdateWebViewSlots();
            };
        }

        private async void LoadExtensions()
        {
            if (webView.CoreWebView2 == null) return;
            var profile = webView.CoreWebView2.Profile;

            var loadedExtensions = await profile.GetBrowserExtensionsAsync();
            var enabledPaths = _configService.CurrentConfig.Overlay.CustomExtensions;

            var targetExtensions = new System.Collections.Generic.List<(string Path, string Name)>();
            foreach (var path in enabledPaths)
            {
                if (Directory.Exists(path))
                {
                    var info = VN2Anki.Helpers.BrowserExtensionHelper.GetExtensionsFromPath(path).FirstOrDefault();
                    if (info != null) targetExtensions.Add((path, info.Name));
                }
            }

            foreach (var loadedExt in loadedExtensions)
            {
                if (!targetExtensions.Any(t => t.Name == loadedExt.Name))
                {
                    try { await loadedExt.RemoveAsync(); } catch { }
                }
            }

            foreach (var target in targetExtensions)
            {
                if (!loadedExtensions.Any(l => l.Name == target.Name))
                {
                    try { await profile.AddBrowserExtensionAsync(target.Path); } catch { }
                }
            }
        }

        private void WebView_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string json = e.TryGetWebMessageAsString();
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("action", out JsonElement actionEl))
                    {
                        string action = actionEl.GetString();
                        string id = root.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() : null;

                        if (id != null)
                        {
                            var slot = _history.FirstOrDefault(s => s.Id == id);
                            
                            switch (action)
                            {
                                case "hover":
                                    _bridgeService.ActiveHoverSlotId = id;
                                    break;
                                case "play":
                                    if (slot != null && slot.AudioBytes != null)
                                        _audioPlaybackService.PlayAudio(slot.AudioBytes);
                                    break;
                                case "delete":
                                    if (slot != null) _onDeleteAction?.Invoke(slot);
                                    break;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void History_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (MiningSlot slot in e.NewItems)
                {
                    slot.PropertyChanged += Slot_PropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (MiningSlot slot in e.OldItems)
                {
                    slot.PropertyChanged -= Slot_PropertyChanged;
                }
            }

            Application.Current.Dispatcher.InvokeAsync(UpdateWebViewSlots, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void Slot_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MiningSlot.IsOpen) || e.PropertyName == nameof(MiningSlot.ScreenshotBytes) || e.PropertyName == nameof(MiningSlot.Text))
            {
                Application.Current.Dispatcher.InvokeAsync(UpdateWebViewSlots, System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void UpdateWebViewSlots()
        {
            if (webView.CoreWebView2 == null) return;

            var slotsDto = _history.Select(s => new
            {
                id = s.Id,
                text = s.Text,
                displayTime = s.DisplayTime,
                isOpen = s.IsOpen,
                thumbnailUrl = s.ScreenshotUrl
            }).ToList();

            var payload = new
            {
                action = "updateSlots",
                slots = slotsDto
            };

            string json = JsonSerializer.Serialize(payload);
            webView.CoreWebView2.PostWebMessageAsJson(json);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
        }

        protected override void OnClosed(EventArgs e)
        {
            _history.CollectionChanged -= History_CollectionChanged;
            foreach (var slot in _history)
            {
                slot.PropertyChanged -= Slot_PropertyChanged;
            }

            _audioPlaybackService?.StopAudio();

            _instance = null;
            _onDeleteAction = null;
            
            webView?.Dispose();
            
            base.OnClosed(e);
        }
    }
}