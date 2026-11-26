using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Shiyi
{
    internal sealed class Capture : IDisposable
    {
        private readonly Window host, chip;
        private readonly DispatcherTimer dotWatch;
        private readonly SelectionReader reader = new SelectionReader();
        private readonly HwndSource messages;
        private readonly Func<bool> suspended;
        private readonly Action<LookupQuery, SelectionAnchor> translate;
        private readonly Action<string, SelectionAnchor> notify;
        private readonly Action dismissPopup;
        private SelectionMouse mouse;
        private PixelPoint previousDown, downPoint;
        private SelectionAnchor chipAnchor;
        private IntPtr pressWindow, chipWindow, copyWindow;
        private int previousTime, generation, copyTime;
        private uint copySequence, lastInput;
        private bool doubleClick, disposed, checkingSelection;
        private Selection chipSelection;

        public bool HotkeyAvailable { get; private set; }
        public bool MouseAvailable { get { return mouse != null && mouse.Available; } }

        public Capture(Window host, Func<bool> suspended, Action<LookupQuery, SelectionAnchor> translate,
            Action<string, SelectionAnchor> notify, Action dismissPopup)
        {
            this.host = host;
            this.suspended = suspended;
            this.translate = translate;
            this.notify = notify;
            this.dismissPopup = dismissPopup;
            chip = Ui.Load<Window>("Dot.xaml");
            Native.MakeNonActivating(chip);
            dotWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            dotWatch.Tick += CheckVisibleSelection;
            chip.Get<Button>("TranslateSelection").Click += delegate {
                var selected = chipSelection;
                var origin = chipWindow;
                var anchor = chipAnchor;
                HideChip();
                if (!suspended() && selected != null && Native.GetForegroundWindow() == origin)
                {
                    copyWindow = IntPtr.Zero;
                    translate(selected.BeginLookup(), anchor);
                }
            };
            var handle = new WindowInteropHelper(host).EnsureHandle();
            messages = HwndSource.FromHwnd(handle);
            messages.AddHook(Message);
            HotkeyAvailable = Native.RegisterHotKey(handle, Native.HotkeyId, 0x4000 | 0x2 | 0x1, 0x54); // NOREPEAT, Ctrl+Alt+T
        }

        public void EnableMouse(bool enabled)
        {
            if (enabled && MouseAvailable) return;
            if (mouse != null) { mouse.Dispose(); mouse = null; }
            Invalidate();
            if (enabled) mouse = new SelectionMouse(host.Dispatcher, OnMouse);
        }

        public void Invalidate()
        {
            generation++;
            pressWindow = IntPtr.Zero;
            copyWindow = IntPtr.Zero;
            HideChip();
        }

        private void HideChip()
        {
            dotWatch.Stop();
            if (chip.IsVisible) chip.Hide();
            chipSelection = null;
            chipWindow = IntPtr.Zero;
        }

        private async void CheckVisibleSelection(object sender, EventArgs e)
        {
            var origin = chipWindow;
            if (Native.GetForegroundWindow() != origin) { HideChip(); return; }
            uint input = Native.LastInputTime();
            if (checkingSelection || input == lastInput || chip.IsMouseOver) return;
            lastInput = input;
            checkingSelection = true;
            int version = generation;
            try
            {
                var selected = await reader.ReadAsync(origin, chipAnchor.End, false);
                if (origin != chipWindow || !Current(origin, version)) return;
                if (selected.Text != chipSelection.Text) { HideChip(); return; }
                chipSelection = selected;
                if (selected.Anchor.HasValue)
                {
                    chipAnchor = selected.Anchor.Value;
                    Native.Place(chip, chipAnchor, true);
                }
            }
            finally { checkingSelection = false; }
        }

        private IntPtr Message(IntPtr handle, int message, IntPtr w, IntPtr l, ref bool handled)
        {
            if (message == Native.HotkeyMessage && w.ToInt32() == Native.HotkeyId)
            {
                handled = true;
                host.Dispatcher.BeginInvoke(new Action(Hotkey));
            }
            return IntPtr.Zero;
        }

        private void OnMouse(SelectionMouseEvent action, PixelPoint point, int time, IntPtr target)
        {
            if (suspended() || disposed) return;
            if (target == IntPtr.Zero || (Native.ProcessOf(target) == Native.OwnProcess && target != messages.Handle))
            {
                pressWindow = IntPtr.Zero;
                return;
            }
            if (action != SelectionMouseEvent.Up)
            {
                generation++;
                HideChip();
                dismissPopup();
                if (action == SelectionMouseEvent.Scroll) { pressWindow = IntPtr.Zero; return; }
                var distance = Forms.SystemInformation.DoubleClickSize;
                doubleClick = target == lastClickWindow && unchecked((uint)(time - previousTime)) <= Native.GetDoubleClickTime() &&
                    Math.Abs(point.X - previousDown.X) <= distance.Width / 2 && Math.Abs(point.Y - previousDown.Y) <= distance.Height / 2;
                previousDown = downPoint = point;
                previousTime = time;
                lastClickWindow = pressWindow = target;
                return;
            }
            if (target != pressWindow || target != Native.GetAncestor(Native.GetForegroundWindow(), 2)) return;
            pressWindow = IntPtr.Zero;
            var drag = Forms.SystemInformation.DragSize;
            if (doubleClick || Math.Abs(point.X - downPoint.X) >= Math.Max(2, drag.Width / 2) ||
                Math.Abs(point.Y - downPoint.Y) >= Math.Max(2, drag.Height / 2) || Native.GetAsyncKeyState(0x10) < 0)
                Probe(downPoint, point, Native.GetForegroundWindow(), ++generation);
        }
        private IntPtr lastClickWindow;

        private async void Probe(PixelPoint start, PixelPoint point, IntPtr origin, int version)
        {
            await Task.Delay(75);
            if (!Current(origin, version)) return;
            var result = await reader.ReadAsync(origin, point, true);
            // Some applications publish the selection after mouse-up, or the drag ends in the margin.
            if (result.Text == null && !result.IsPassword && Current(origin, version))
            {
                await Task.Delay(140);
                if (!Current(origin, version)) return;
                result = await reader.ReadAsync(origin, start, true, point);
            }
            if (!Current(origin, version) || result.Text == null || result.IsPassword) return;
            chipSelection = result;
            chipWindow = origin;
            chipAnchor = result.Anchor ?? SelectionAnchor.At(point);
            chip.Show();
            chip.UpdateLayout();
            Native.Place(chip, chipAnchor, true);
            lastInput = Native.LastInputTime();
            dotWatch.Start();
        }

        private bool Current(IntPtr origin, int version)
        {
            return !disposed && !suspended() && version == generation && Native.GetForegroundWindow() == origin;
        }

        private async void Hotkey()
        {
            if (disposed || suspended()) return;
            var origin = Native.GetForegroundWindow();
            if (origin == IntPtr.Zero || (Native.ProcessOf(origin) == Native.OwnProcess && origin != messages.Handle)) return;
            PixelPoint point;
            if (!Native.GetCursorPos(out point)) return;
            int version = ++generation;
            HideChip();
            var result = await reader.ReadAsync(origin, point, false);
            if (!Current(origin, version)) return;
            if (result.IsPassword)
            {
                copyWindow = IntPtr.Zero;
                notify("密码输入框不支持划词翻译。", SelectionAnchor.At(point));
                return;
            }
            if (result.Text != null)
            {
                copyWindow = IntPtr.Zero;
                translate(result.BeginLookup(), result.Anchor ?? SelectionAnchor.At(point));
                return;
            }

            // Only accept a new, explicit copy from the same foreground application on a second hotkey.
            uint sequence = Native.GetClipboardSequenceNumber();
            if (origin == copyWindow && unchecked((uint)(Environment.TickCount - copyTime)) < 30000 &&
                sequence != copySequence && Native.ProcessOf(Native.GetClipboardOwner()) == Native.ProcessOf(origin))
            {
                try
                {
                    string text = Clipboard.GetText(TextDataFormat.UnicodeText);
                    if (!string.IsNullOrWhiteSpace(text) && Current(origin, version) && sequence == Native.GetClipboardSequenceNumber())
                    {
                        copyWindow = IntPtr.Zero;
                        translate(new LookupQuery(text), SelectionAnchor.At(point));
                        return;
                    }
                }
                catch (ExternalException) { }
            }
            copyWindow = origin;
            copySequence = sequence;
            copyTime = Environment.TickCount;
            notify("未读取到选区。请选中文字，按 Ctrl+C 复制，在 30 秒内再按一次 Ctrl+Alt+T。也可将原文粘贴到主窗口。", SelectionAnchor.At(point));
        }

        public void Dispose()
        {
            disposed = true;
            Invalidate();
            if (mouse != null) mouse.Dispose();
            if (HotkeyAvailable) Native.UnregisterHotKey(messages.Handle, Native.HotkeyId);
            messages.RemoveHook(Message);
            chip.Close();
        }
    }
}
