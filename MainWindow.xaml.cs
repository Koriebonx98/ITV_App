using System.Text;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using System.IO;

namespace ITV_App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ITV App", "WebView2UserData");

            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await webView.EnsureCoreWebView2Async(env);

            // Block all ad/tracking domains (hostname match)
            string[] blockedHosts =
            {
        "ads.itv.com",
        "adserver.itv.com",
        "itv-ads.s3.amazonaws.com",
        "doubleclick.net",
        "googlesyndication.com",
        "googleadservices.com",
        "pubads.g.doubleclick.net",
        "securepubads.g.doubleclick.net",
        "imasdk.googleapis.com"
    };

            webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);

            webView.CoreWebView2.WebResourceRequested += (s, args) =>
            {
                try
                {
                    var url = new Uri(args.Request.Uri);
                    var host = url.Host.ToLower();

                    if (blockedHosts.Any(h => host == h || host.EndsWith("." + h)))
                    {
                        args.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                            Stream.Null, 403, "Blocked", "Content-Type: text/plain");
                        return;
                    }

                    // Block HLS ad manifests (ITV uses "ad" segments)
                    if (url.AbsolutePath.Contains("ad") && url.AbsolutePath.EndsWith(".m3u8"))
                    {
                        args.Response = webView.CoreWebView2.Environment.CreateWebResourceResponse(
                            Stream.Null, 403, "Blocked", "Content-Type: text/plain");
                        return;
                    }
                }
                catch { }
            };

            // Inject scriptlet-like blocking (similar to uBlock)
            webView.CoreWebView2.NavigationCompleted += (s, args) =>
            {
                string script = @"
            // Block ITV ad player mode
            Object.defineProperty(window, 'ITVAds', { value: {}, writable: false });

            // Kill ad-related XHR/fetch
            (function() {
                const blocked = [
                    'ads.itv.com',
                    'adserver.itv.com',
                    'doubleclick.net',
                    'googlesyndication.com',
                    'googleadservices.com'
                ];

                const origFetch = window.fetch;
                window.fetch = function() {
                    let url = arguments[0].toString();
                    if (blocked.some(h => url.includes(h))) {
                        return new Promise(() => {}); // never resolves
                    }
                    return origFetch.apply(this, arguments);
                };

                const origXhrOpen = XMLHttpRequest.prototype.open;
                XMLHttpRequest.prototype.open = function(method, url) {
                    if (blocked.some(h => url.includes(h))) {
                        return; // cancel XHR
                    }
                    return origXhrOpen.apply(this, arguments);
                };
            })();

            // Remove ad containers continuously
            setInterval(() => {
                let selectors = [
                    '#ad-container',
                    '.ad-container',
                    '.itv-ad',
                    '.itv-player__ad',
                    '.ad-slot',
                    '.advert',
                    '.sponsor-container',
                    '.sponsor-overlay'
                ];
                selectors.forEach(sel => {
                    document.querySelectorAll(sel).forEach(el => el.remove());
                });
            }, 500);
        ";

                webView.CoreWebView2.ExecuteScriptAsync(script);
            };

            webView.CoreWebView2.Navigate("https://www.itv.com/");
        }

        private OnScreenKeyboardWindow oskWindow;
        private void ShowOnScreenKeyboard()
        {
            if (oskWindow == null)
            {
                oskWindow = new OnScreenKeyboardWindow();
                oskWindow.KeyPressed += OnScreenKeyboard_KeyPressed;
                oskWindow.CloseRequested += () => { oskWindow.Close(); oskWindow = null; };
                oskWindow.Owner = this;
                oskWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                oskWindow.Show();
            }
            else
            {
                oskWindow.Activate();
            }
        }

        private void OnScreenKeyboard_KeyPressed(string key)
        {
            // Send key to search box in WebView2
            string js;
            if (key == "<")
                js = @"var s=document.querySelector('input#search');if(s){s.value=s.value.slice(0,-1);}";
            else if (key == "_")
                js = @"var s=document.querySelector('input#search');if(s){s.value+='_';}";
            else if (key == " ")
                js = @"var s=document.querySelector('input#search');if(s){s.value+=' ';}";
            else
                js = $"var s=document.querySelector('input#search');if(s){{s.value+=\"{key}\";}}";
            webView.CoreWebView2.ExecuteScriptAsync(js);
        }
    }
}