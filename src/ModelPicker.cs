using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Shiyi
{
    internal sealed class ModelPicker : IDisposable
    {
        private sealed class ModelRow
        {
            public string Id { get; set; }
            public string Mark { get; set; }
        }

        private readonly Translator service;
        private readonly TextBox address, name, search;
        private readonly PasswordBox key;
        private readonly Button pick, refresh;
        private readonly Popup menu;
        private readonly ListBox list;
        private readonly TextBlock status, empty;
        private readonly Grid field;
        private readonly Border body;
        private readonly StackPanel page;
        private readonly DispatcherTimer delay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        private CancellationTokenSource request;
        private string[] models = new string[0];
        private string keyOrigin;
        private bool tried, clearingKey, closed, autoLoad;

        public ModelPicker(Window window, Translator service, Preferences prefs)
        {
            this.service = service;
            address = window.Get<TextBox>("Address"); name = window.Get<TextBox>("ModelName"); key = window.Get<PasswordBox>("KeyBox");
            search = window.Get<TextBox>("ModelSearch"); list = window.Get<ListBox>("ModelList"); menu = window.Get<Popup>("ModelMenu");
            pick = window.Get<Button>("PickModel"); refresh = window.Get<Button>("RefreshModels");
            status = window.Get<TextBlock>("CatalogStatus"); empty = window.Get<TextBlock>("MenuEmpty");
            field = window.Get<Grid>("ModelField"); body = window.Get<Border>("MenuBody");
            page = window.Get<StackPanel>("ModelsPage");
            address.Text = prefs.BaseUrl; name.Text = prefs.Model; key.Password = prefs.ApiKey; keyOrigin = Origin(prefs.BaseUrl);
            autoLoad = prefs.Configured || key.Password.Length > 0 || IsLocal(address.Text);
            status.Text = "填写服务信息后自动读取模型，也可直接输入模型 ID。";

            delay.Tick += delegate { delay.Stop(); Load(false); };
            address.TextChanged += delegate { InputsChanged(); };
            key.PasswordChanged += delegate {
                if (clearingKey) return;
                keyOrigin = Origin(address.Text);
                InputsChanged();
            };
            refresh.Click += delegate { Load(true); };
            pick.Click += delegate {
                if (menu.IsOpen) { menu.IsOpen = false; return; }
                body.Width = Math.Max(200, field.ActualWidth);
                search.Clear();
                Filter();
                menu.IsOpen = true;
                Load(false);
                window.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { if (!closed && menu.IsOpen) search.Focus(); }));
            };
            search.TextChanged += delegate { Filter(); };
            list.PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) {
                var hit = e.OriginalSource as DependencyObject;
                var row = hit == null ? null : ItemsControl.ContainerFromElement(list, hit) as ListBoxItem;
                if (row != null && row.Content is ModelRow) Choose((ModelRow)row.Content);
            };
            body.PreviewKeyDown += MenuKey;
            address.PreviewKeyDown += ConnectionKey; key.PreviewKeyDown += ConnectionKey;
            page.IsVisibleChanged += delegate {
                if (!page.IsVisible) { delay.Stop(); menu.IsOpen = false; }
                else if (!closed && autoLoad && !tried) delay.Start();
            };
        }

        private static string Origin(string input)
        {
            try { return Translator.ModelsEndpoint(input).GetLeftPart(UriPartial.Authority); }
            catch (TranslationException) { return null; }
        }
        private static bool IsLocal(string input)
        {
            Uri uri;
            return Uri.TryCreate(input, UriKind.Absolute, out uri) && uri.IsLoopback;
        }

        public string KeyForCurrentAddress()
        {
            var origin = Translator.ModelsEndpoint(address.Text).GetLeftPart(UriPartial.Authority);
            // Editing or saving an address must never forward the previous service's bearer token.
            if (key.Password.Length > 0 && !string.Equals(keyOrigin, origin, StringComparison.OrdinalIgnoreCase))
            {
                clearingKey = true;
                try { key.Clear(); } finally { clearingKey = false; }
            }
            keyOrigin = origin;
            string value = key.Password.Trim();
            if (value.Any(char.IsControl)) throw new TranslationException("API Key 中包含无效的控制字符。");
            return value;
        }

        private void InputsChanged()
        {
            if (closed) return;
            CancelRequest();
            tried = false; autoLoad = true; models = new string[0];
            status.Text = "等待服务信息输入完成…";
            if (menu.IsOpen) Filter();
            delay.Stop(); if (page.IsVisible) delay.Start();
        }

        private void ConnectionKey(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Load(true);
        }

        private async void Load(bool force)
        {
            delay.Stop();
            if (closed || (!force && (tried || request != null))) return;
            CancelRequest();
            tried = true;
            var current = new CancellationTokenSource();
            request = current;
            status.Text = "正在读取模型列表…";
            if (menu.IsOpen) Filter();
            try
            {
                string token = KeyForCurrentAddress();
                var result = await service.ListModelsAsync(address.Text, token, current.Token);
                if (request != current || closed) return;
                models = result;
                status.Text = result.Length == 0 ? "服务返回了空列表，可手动输入模型 ID。" : "已读取 " + result.Length + " 个模型，可搜索选择或手动输入。";
            }
            catch (Exception ex)
            {
                if (request != current || closed) return;
                models = new string[0];
                string reason = ex is TranslationException ? ex.Message : ex is HttpRequestException ? "无法连接模型服务。" : "未能读取模型列表。";
                status.Text = reason + " 可手动输入模型 ID。";
            }
            finally
            {
                if (request == current) { request = null; if (menu.IsOpen) Filter(); }
                current.Dispose();
            }
        }

        private void Filter()
        {
            string term = search.Text.Trim();
            string selected = name.Text.Trim();
            var rows = models.Where(m => m.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(m => new ModelRow { Id = m, Mark = m == selected ? "✓" : "" }).ToArray();
            list.ItemsSource = rows;
            list.SelectedItem = rows.FirstOrDefault(m => m.Mark.Length != 0) ?? rows.FirstOrDefault();
            if (list.SelectedItem != null) list.ScrollIntoView(list.SelectedItem);
            list.Visibility = rows.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            empty.Visibility = rows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            empty.Text = models.Length == 0 ? status.Text : "没有匹配的模型";
        }

        private void MenuKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { e.Handled = true; menu.IsOpen = false; pick.Focus(); return; }
            if (e.Key == Key.Enter) { e.Handled = true; Choose(list.SelectedItem as ModelRow); return; }
            if ((e.Key == Key.Down || e.Key == Key.Up) && search.IsKeyboardFocusWithin && list.Items.Count > 0)
            {
                e.Handled = true;
                list.SelectedIndex = Math.Max(0, Math.Min(list.Items.Count - 1, list.SelectedIndex + (e.Key == Key.Down ? 1 : -1)));
                list.ScrollIntoView(list.SelectedItem);
            }
        }

        private void Choose(ModelRow row)
        {
            if (row == null) return;
            name.Text = row.Id;
            menu.IsOpen = false;
            pick.Focus();
        }

        private void CancelRequest()
        {
            var old = request; request = null;
            if (old != null) old.Cancel();
        }

        public void Dispose()
        {
            closed = true; delay.Stop(); menu.IsOpen = false; CancelRequest();
            key.Clear();
        }
    }
}
