using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Forms = System.Windows.Forms;

namespace Shiyi
{
    internal sealed class SettingsDialog : IDisposable
    {
        private readonly Preferences initial, draft;
        private readonly SettingsStore store;
        private readonly Appearance appearance;
        private readonly Action<Preferences> preview;
        private readonly ModelPicker picker;
        private readonly Dictionary<ThemePreset, Ellipse> swatches = new Dictionary<ThemePreset, Ellipse>();
        private readonly TextBox hex;
        private readonly Slider opacity;
        private bool writingHex;
        public readonly Window Window;
        public Preferences Result { get; private set; }

        public SettingsDialog(Window owner, Preferences prefs, SettingsStore store, Translator service, Appearance appearance,
            Action<Preferences> preview, bool openLook)
        {
            this.initial = prefs; this.draft = prefs.Copy(); this.store = store; this.appearance = appearance; this.preview = preview;
            Window = Ui.Load<Window>("Settings.xaml");
            appearance.StyleTitleBar(Window);
            Window.Owner = owner; Window.Icon = Application.Current.MainWindow.Icon;
            var area = SystemParameters.WorkArea;
            Window.MinWidth = Math.Min(Window.MinWidth, area.Width); Window.MinHeight = Math.Min(Window.MinHeight, area.Height);
            Window.Width = Math.Min(Window.Width, area.Width); Window.Height = Math.Min(Window.Height, area.Height);
            picker = new ModelPicker(Window, service, prefs);
            hex = Window.Get<TextBox>("HexColor"); opacity = Window.Get<Slider>("OpacityLevel");
            Window.Get<CheckBox>("EnableChip").IsChecked = prefs.SelectionButton;
            Window.Get<RadioButton>("ModelTab").Checked += delegate { Page(false); };
            Window.Get<RadioButton>("LookTab").Checked += delegate { Page(true); };
            SetupMode("LightMode", "light"); SetupMode("DarkMode", "dark"); SetupMode("SystemMode", "system");
            AddSwatches();
            appearance.Changed += PaintSwatches;
            PaintSwatches(); ShowHex();
            hex.TextChanged += delegate {
                if (writingHex) return;
                string value = ThemePreset.Hex(hex.Text);
                if (value.Length == 0 && hex.Text.Trim().Length != 0) return;
                draft.AccentColor = value == PresetAccent() ? "" : value;
                preview(draft);
            };
            hex.LostKeyboardFocus += delegate { if (ThemePreset.Hex(hex.Text).Length > 0 || hex.Text.Length == 0) ShowHex(); };
            Window.Get<Button>("PresetColor").Click += delegate { draft.AccentColor = ""; preview(draft); ShowHex(); };
            Window.Get<Button>("PickColor").Click += delegate { PickColor(); };
            opacity.Minimum = Preferences.MinOpacity;
            opacity.Value = prefs.PopupOpacity;
            ShowOpacity();
            opacity.ValueChanged += delegate {
                draft.PopupOpacity = (int)Math.Round(opacity.Value);
                ShowOpacity(); preview(draft);
            };
            Window.Get<Button>("Save").Click += delegate { Save(); };
            if (openLook) Window.Get<RadioButton>("LookTab").IsChecked = true;
        }

        private void Page(bool look)
        {
            Window.Get<StackPanel>("ModelsPage").Visibility = look ? Visibility.Collapsed : Visibility.Visible;
            Window.Get<StackPanel>("LookPage").Visibility = look ? Visibility.Visible : Visibility.Collapsed;
            Window.Get<ScrollViewer>("PageScroll").ScrollToTop();
        }
        private void SetupMode(string control, string mode)
        {
            var option = Window.Get<RadioButton>(control);
            option.IsChecked = draft.AppearanceMode == mode;
            option.Checked += delegate { draft.AppearanceMode = mode; preview(draft); ShowHex(); };
        }

        private void AddSwatches()
        {
            var panel = Window.Get<WrapPanel>("Swatches");
            foreach (var choice in ThemePreset.All)
            {
                var preset = choice;
                var disk = new Ellipse { Width = 19, Height = 19, StrokeThickness = 1 };
                disk.SetResourceReference(Shape.StrokeProperty, "ControlLine");
                var label = new TextBlock { Text = preset.Name, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 11, Margin = new Thickness(0,2,0,0) };
                var stack = new StackPanel(); stack.Children.Add(disk); stack.Children.Add(label);
                var button = new RadioButton { GroupName = "Palette", Style = (Style)Window.FindResource("Choice"),
                    Width = 45, Padding = new Thickness(1,4,1,3), HorizontalContentAlignment = HorizontalAlignment.Center,
                    Content = stack, IsChecked = preset.Id == draft.Theme, ToolTip = preset.Name };
                AutomationProperties.SetName(button, preset.Name + "主题");
                Action select = delegate {
                    if (draft.Theme == preset.Id && draft.AccentColor.Length == 0) { ShowHex(); return; }
                    draft.Theme = preset.Id; draft.AccentColor = ""; preview(draft); ShowHex();
                };
                button.Checked += delegate { select(); };
                button.Click += delegate { select(); };
                swatches.Add(preset, disk); panel.Children.Add(button);
            }
        }

        private string PresetAccent()
        {
            var p = ThemePreset.Find(draft.Theme);
            return (appearance.Dark ? p.Dark : p.Light).Accent;
        }
        private void PaintSwatches()
        {
            foreach (var pair in swatches) pair.Value.Fill = Appearance.Brush(Appearance.ColorOf((appearance.Dark ? pair.Key.Dark : pair.Key.Light).Accent));
            if (!hex.IsKeyboardFocusWithin) ShowHex();
        }
        private void ShowHex()
        {
            writingHex = true;
            try { hex.Text = draft.AccentColor.Length == 0 ? PresetAccent() : draft.AccentColor; }
            finally { writingHex = false; }
        }
        private void ShowOpacity()
        {
            Window.Get<TextBlock>("OpacityValue").Text = draft.PopupOpacity.ToString(CultureInfo.InvariantCulture) + "%";
            Window.Get<Border>("SamplePopup").Opacity = draft.PopupOpacity / 100.0;
        }
        private sealed class DialogOwner : Forms.IWin32Window
        {
            public IntPtr Handle { get; private set; }
            public DialogOwner(Window window) { Handle = new WindowInteropHelper(window).Handle; }
        }
        private void PickColor()
        {
            using (var chooser = new Forms.ColorDialog { FullOpen = true, Color = System.Drawing.ColorTranslator.FromHtml(
                draft.AccentColor.Length == 0 ? PresetAccent() : draft.AccentColor) })
            {
                if (chooser.ShowDialog(new DialogOwner(Window)) == Forms.DialogResult.OK)
                {
                    draft.AccentColor = string.Format("#{0:X2}{1:X2}{2:X2}", chooser.Color.R, chooser.Color.G, chooser.Color.B);
                    preview(draft); ShowHex();
                }
            }
        }

        private void Save()
        {
            var error = Window.Get<TextBlock>("SettingsError");
            if (hex.Text.Trim().Length > 0 && ThemePreset.Hex(hex.Text).Length == 0)
            {
                Window.Get<RadioButton>("LookTab").IsChecked = true;
                error.Text = "请输入有效的颜色，例如 #1B4ED8。"; hex.Focus(); return;
            }
            try
            {
                draft.SelectionButton = Window.Get<CheckBox>("EnableChip").IsChecked == true;
                string url = Window.Get<TextBox>("Address").Text.Trim();
                string model = Window.Get<TextBox>("ModelName").Text.Trim();
                bool changed = url != initial.BaseUrl || model != initial.Model || Window.Get<PasswordBox>("KeyBox").Password.Trim() != initial.ApiKey;
                bool lookChanged = draft.PopupOpacity != initial.PopupOpacity || draft.Theme != initial.Theme ||
                    draft.AppearanceMode != initial.AppearanceMode || draft.AccentColor != initial.AccentColor || draft.SelectionButton != initial.SelectionButton;
                bool confirmFirstConnection = !initial.Configured && !lookChanged && Window.Get<RadioButton>("ModelTab").IsChecked == true;
                if (changed || confirmFirstConnection)
                {
                    string key = picker.KeyForCurrentAddress();
                    if (model.Length == 0) throw new TranslationException("请选择模型或手动输入模型 ID。");
                    draft.BaseUrl = url; draft.Model = model; draft.ApiKey = key; draft.Configured = true;
                }
                store.Save(draft);
                Result = draft;
                Window.DialogResult = true;
            }
            catch (TranslationException ex) { Window.Get<RadioButton>("ModelTab").IsChecked = true; error.Text = ex.Message; }
            catch (Exception) { error.Text = "保存失败，请检查本地配置目录的写入权限。"; }
        }

        public void Dispose()
        {
            appearance.Changed -= PaintSwatches;
            picker.Dispose();
        }
    }
}
