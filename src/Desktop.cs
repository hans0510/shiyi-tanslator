using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Shiyi
{
    internal static class Ui
    {
        public static Stream Resource(string name) { return Assembly.GetExecutingAssembly().GetManifestResourceStream("Shiyi." + name); }
        public static T Load<T>(string name) { using (var input = Resource(name)) return (T)XamlReader.Load(input); }
        public static T Get<T>(this Window window, string name) where T : FrameworkElement { return (T)window.FindName(name); }
    }

    internal sealed class Desktop : IDisposable
    {
        private readonly SettingsStore store;
        private Preferences prefs;
        private readonly Translator translator = new Translator();
        private readonly Appearance appearance;
        private readonly Capture capture;
        private readonly Forms.NotifyIcon tray;
        private readonly Window main, popup;
        private Window settings;
        private readonly TextBox source, output, popupResult;
        private readonly TextBlock status, hint, popupStatus;
        private readonly Button run, copy, popupCopy, popupSettings;
        private readonly ComboBox target;
        private readonly Pronouncer voice;
        private readonly WordCard mainCard, popupCard;
        private SelectionAnchor? popupAnchor;
        private CancellationTokenSource pending, popupPending;
        private LookupQuery mainQuery, popupQuery;
        private LookupResult popupValue;
        private string popupError;
        private bool changing, exiting;

        public Desktop(SettingsStore store)
        {
            this.store = store;
            string notice;
            prefs = store.Read(out notice);
            Application.Current.Resources.MergedDictionaries.Add(Ui.Load<ResourceDictionary>("Theme.xaml"));
            appearance = new Appearance(Application.Current);
            appearance.Apply(prefs);
            main = Ui.Load<Window>("Main.xaml");
            appearance.StyleTitleBar(main);
            Application.Current.MainWindow = main;
            using (var icon = Ui.Resource("App.ico"))
                main.Icon = BitmapFrame.Create(icon, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            main.Height = Math.Min(main.Height, SystemParameters.WorkArea.Height);
            main.Width = Math.Min(main.Width, SystemParameters.WorkArea.Width);
            source = main.Get<TextBox>("Source");
            output = main.Get<TextBox>("Output");
            status = main.Get<TextBlock>("Status");
            hint = main.Get<TextBlock>("OutputHint");
            run = main.Get<Button>("TranslateButton");
            copy = main.Get<Button>("CopyButton");
            target = main.Get<ComboBox>("Target");
            target.ItemsSource = Preferences.Languages;
            target.SelectedItem = prefs.Target;
            popup = Ui.Load<Window>("Popup.xaml");
            popup.Left = popup.Top = -32000;
            popupResult = popup.Get<TextBox>("Result");
            popupStatus = popup.Get<TextBlock>("PopupStatus");
            popupCopy = popup.Get<Button>("CopyResult");
            popupSettings = popup.Get<Button>("Connect");
            voice = new Pronouncer(main.Dispatcher);
            mainCard = new WordCard(main.Get<ContentControl>("MainEntry"), voice);
            popupCard = new WordCard(popup.Get<ContentControl>("PopupEntry"), voice);
            popupSettings.Click += delegate {
                if (!popupAnchor.HasValue) return;
                var anchor = popupAnchor.Value;
                var selected = popupQuery;
                if (EditSettings(popup) && prefs.Configured) Translate(selected, anchor);
            };
            popup.Get<Button>("PopupOptions").Click += delegate { EditSettings(popup, true); };
            popup.Get<Button>("Dismiss").Click += delegate { DismissPopup(); };
            popup.Get<Button>("Expand").Click += delegate { ExpandPopup(); };
            popup.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; DismissPopup(); } };
            popup.SizeChanged += delegate { if (popup.IsVisible && popupAnchor.HasValue) Native.Place(popup, popupAnchor.Value); };
            popup.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!exiting) { e.Cancel = true; DismissPopup(); } };
            popupCopy.Click += delegate { CopyResult(popupResult.Text, popupStatus); };
            copy.Click += delegate { CopyResult(output.Text, status); };
            main.Get<Button>("SettingsButton").Click += delegate { EditSettings(); };
            run.Click += delegate { MainCommand(); };
            main.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; MainCommand(); }
            };
            source.TextChanged += delegate { UpdateCount(); if (!changing) { mainQuery = null; ResetOutput(); } };
            target.SelectionChanged += delegate { ChangeTarget(); };
            main.Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) {
                if (!exiting) { e.Cancel = true; main.Hide(); }
            };

            capture = new Capture(main, delegate { return exiting || settings != null; },
                delegate(LookupQuery query, SelectionAnchor anchor) { Translate(query, anchor); }, Notice, DismissPopup);
            tray = CreateTray();
            ApplyPreferences();
            UpdateCount();
            ResetOutput();
            if (notice != null) status.Text = notice;
            ShowMain();
        }

        private Forms.NotifyIcon CreateTray()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("打开拾译", null, delegate { ShowMain(); });
            menu.Items.Add("设置", null, delegate { ShowMain(); EditSettings(); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出拾译", null, delegate { Exit(); });
            var icon = new Forms.NotifyIcon { Text = "拾译 · 划词翻译", ContextMenuStrip = menu };
            using (var stream = Ui.Resource("App.ico"))
            using (var original = new System.Drawing.Icon(stream, 32, 32))
                icon.Icon = (System.Drawing.Icon)original.Clone();
            icon.DoubleClick += delegate { ShowMain(); };
            icon.Visible = true;
            return icon;
        }

        public void ShowMain()
        {
            if (exiting) return;
            if (settings != null) { settings.Activate(); return; }
            DismissPopup();
            if (!main.IsVisible) main.Show();
            if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
            main.Activate();
        }

        private void ApplyPreferences()
        {
            PreviewAppearance(prefs);
            capture.EnableMouse(prefs.SelectionButton);
            main.Get<TextBlock>("ModelLabel").Text = prefs.Configured ? prefs.Model : "尚未连接模型";
            string availability = prefs.SelectionButton && capture.MouseAvailable ? "划词后点蓝点翻译" : "Ctrl+Alt+T 划词翻译";
            if (!capture.HotkeyAvailable) availability = prefs.SelectionButton && capture.MouseAvailable ?
                "划词后点蓝点翻译 · 快捷键被占用" : "Ctrl+Alt+T 已被占用";
            if (prefs.SelectionButton && !capture.MouseAvailable) availability += " · 蓝点不可用";
            main.Get<TextBlock>("HotkeyLabel").Text = availability;
        }

        private void PreviewAppearance(Preferences value)
        {
            appearance.Apply(value);
            popup.Opacity = value.PopupOpacity / 100.0;
        }

        private bool EditSettings(Window owner = null, bool openLook = false)
        {
            if (settings != null) { settings.Activate(); return false; }
            capture.Invalidate();
            if (owner != popup) DismissPopup();
            using (var editor = new SettingsDialog(owner ?? main, prefs, store, translator, appearance, PreviewAppearance, openLook))
            {
                settings = editor.Window;
                try
                {
                    if (settings.ShowDialog() != true || editor.Result == null) return false;
                    var next = editor.Result;
                    bool connectionChanged = prefs.BaseUrl != next.BaseUrl || prefs.Model != next.Model ||
                        prefs.ApiKey != next.ApiKey || prefs.Configured != next.Configured;
                    prefs = next;
                    ApplyPreferences();
                    if (connectionChanged) { DismissPopup(); ResetOutput(); }
                    if (pending == null) status.Text = "设置已保存";
                    return true;
                }
                finally { settings = null; PreviewAppearance(prefs); }
            }
        }

        private void ChangeTarget()
        {
            if (changing || target.SelectedItem == null || (string)target.SelectedItem == prefs.Target) return;
            var next = prefs.Copy();
            next.Target = (string)target.SelectedItem;
            try { store.Save(next); prefs = next; DismissPopup(); ResetOutput(); }
            catch (Exception)
            {
                changing = true;
                try { target.SelectedItem = prefs.Target; } finally { changing = false; }
                status.Text = "目标语言保存失败，请检查本地配置目录的写入权限。";
            }
        }

        private void MainCommand()
        {
            if (pending != null) { ResetOutput(); status.Text = "已取消翻译"; return; }
            Translate(mainQuery ?? new LookupQuery(source.Text), null);
        }

        private void UpdateCount() { main.Get<TextBlock>("Count").Text = source.Text.Length.ToString("N0") + " 字符"; }

        private static void Cancel(ref CancellationTokenSource request)
        {
            var old = request;
            request = null;
            if (old != null) old.Cancel();
        }

        private void ResetOutput()
        {
            Cancel(ref pending);
            mainCard.Clear();
            output.Visibility = Visibility.Visible;
            output.Clear();
            main.Get<TextBlock>("ResultTitle").Text = "译文";
            copy.Content = "复制译文";
            copy.IsEnabled = false;
            hint.Text = "译文会显示在这里";
            run.Content = "翻译";
            status.Text = prefs.Configured ? "就绪" : "请先在设置中连接模型服务";
        }

        private void DismissPopup()
        {
            Cancel(ref popupPending);
            popupAnchor = null;
            popupQuery = null; popupValue = null; popupError = null;
            popupCard.Clear();
            if (popup.IsVisible) popup.Hide();
        }

        private void ExpandPopup()
        {
            var query = popupQuery;
            string error = popupError;
            var result = popupValue;
            // Transfer an in-flight request so expanding the popup never sends the text twice.
            var ongoing = popupPending;
            popupPending = null;
            ShowMain();
            if (query == null) return;
            ResetOutput();
            mainQuery = query;
            changing = true;
            try { source.Text = query.Source; } finally { changing = false; }
            if (ongoing != null) ShowMainWaiting(ongoing, query);
            else if (result != null) ShowMainResult(result);
            else if (error != null) { hint.Text = error; status.Text = "翻译未完成"; }
        }

        private void ShowMainWaiting(CancellationTokenSource request, LookupQuery query)
        {
            pending = request;
            run.Content = "取消";
            hint.Text = "正在等待模型回复…";
            status.Text = query.Word == null ? "正在翻译…" : "正在查词…";
        }

        private void ShowMainResult(LookupResult result)
        {
            output.Text = result.Text;
            hint.Text = "";
            copy.IsEnabled = true;
            if (result.Entry != null)
            {
                output.Visibility = Visibility.Collapsed;
                mainCard.Show(result.Entry);
                copy.Content = "复制词条";
                main.Get<TextBlock>("ResultTitle").Text = "词条";
            }
            status.Text = result.Entry == null ? "翻译完成" : "查词完成";
        }

        private void ShowPopupResult(LookupResult result)
        {
            popupValue = result;
            popupResult.Text = result.Text;
            popupCopy.IsEnabled = true;
            if (result.Entry == null) return;
            popup.Get<TextBlock>("PopupTitle").Text = "词典 · " + prefs.Target;
            popup.Get<TextBlock>("Excerpt").Visibility = Visibility.Collapsed;
            popupResult.Visibility = Visibility.Collapsed;
            popupCopy.Content = "复制词条";
            popupCard.Show(result.Entry);
        }

        private void OpenPopup(string title, string excerpt, string body, string message, SelectionAnchor anchor, bool connect = false)
        {
            popup.Get<TextBlock>("PopupTitle").Text = title;
            popup.Get<TextBlock>("Excerpt").Text = excerpt;
            popup.Get<TextBlock>("Excerpt").Visibility = string.IsNullOrEmpty(excerpt) ? Visibility.Collapsed : Visibility.Visible;
            popupResult.Text = body;
            popupResult.Visibility = Visibility.Visible;
            popupStatus.Text = message;
            popupCopy.Content = "复制译文";
            popupCopy.IsEnabled = false;
            popupCopy.Visibility = connect ? Visibility.Collapsed : Visibility.Visible;
            popupSettings.Visibility = connect ? Visibility.Visible : Visibility.Collapsed;
            popupAnchor = anchor;
            if (!popup.IsVisible) popup.Show();
            popup.UpdateLayout();
            Native.Place(popup, anchor);
        }

        private void Notice(string message, SelectionAnchor anchor)
        {
            DismissPopup();
            OpenPopup("划词翻译", "", message, "", anchor);
        }

        private async void Translate(LookupQuery query, SelectionAnchor? anchor)
        {
            string text = query.Source;
            if (anchor.HasValue) DismissPopup();
            else ResetOutput();
            if (string.IsNullOrWhiteSpace(text) || text.Length > Translator.TextLimit)
            {
                string error = string.IsNullOrWhiteSpace(text) ? "请先选中或输入要翻译的文字。" : "原文超过 24,000 个字符，请分段翻译。";
                if (anchor.HasValue) OpenPopup("划词翻译", "", error, "", anchor.Value);
                else { status.Text = error; source.Focus(); }
                return;
            }
            if (anchor.HasValue) popupQuery = query;
            if (!prefs.Configured)
            {
                if (anchor.HasValue)
                {
                    OpenPopup("划词翻译", text.Replace('\r', ' ').Replace('\n', ' '), "连接模型后即可查询所选内容。", "", anchor.Value, true);
                    return;
                }
                if (!EditSettings() || !prefs.Configured) return;
            }
            var current = new CancellationTokenSource();
            if (anchor.HasValue)
            {
                popupPending = current;
                OpenPopup("拾译 · " + prefs.Target, text.Replace('\r', ' ').Replace('\n', ' '), "正在等待模型回复…", "", anchor.Value);
            }
            else ShowMainWaiting(current, query);
            try
            {
                var result = await translator.TranslateAsync(prefs.Copy(), query, current.Token);
                if (current == popupPending) ShowPopupResult(result);
                else if (current == pending) ShowMainResult(result);
            }
            catch (Exception ex)
            {
                if ((current != pending && current != popupPending) || exiting) return;
                string error = ex is TranslationException ? ex.Message :
                    ex is OperationCanceledException ? "模型响应超时，请重试或检查接口地址。" :
                    ex is HttpRequestException ? "无法连接模型服务，请检查接口地址、网络或本地服务是否已启动。" :
                    "翻译失败，请检查模型设置后重试。";
                if (current == popupPending) { popupError = popupResult.Text = error; popupStatus.Text = "翻译未完成"; }
                else { hint.Text = error; status.Text = "翻译未完成"; }
            }
            finally
            {
                if (current == pending) { pending = null; run.Content = "翻译"; }
                if (current == popupPending) popupPending = null;
                current.Dispose();
            }
        }

        private static void CopyResult(string text, TextBlock feedback)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); feedback.Text = "已复制"; }
            catch (ExternalException) { feedback.Text = "剪贴板正被占用，请重试。"; }
        }

        public void Exit()
        {
            if (exiting) return;
            exiting = true;
            Cancel(ref pending);
            Cancel(ref popupPending);
            Application.Current.Shutdown();
        }

        public void Dispose()
        {
            exiting = true;
            capture.Dispose();
            tray.Visible = false;
            tray.ContextMenuStrip.Dispose();
            tray.Icon.Dispose();
            tray.Dispose();
            appearance.Dispose();
            voice.Dispose();
            translator.Dispose();
        }
    }
}
