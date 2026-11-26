using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace Shiyi
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct PixelPoint
    {
        public int X, Y;
        public PixelPoint(int x, int y) { X = x; Y = y; }
    }

    internal static class Native
    {
        internal const int HotkeyMessage = 0x0312, HotkeyId = 0x5348;
        internal static readonly uint OwnProcess = (uint)Process.GetCurrentProcess().Id;
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out PixelPoint p);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(PixelPoint p);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] internal static extern uint GetDoubleClickTime();
        [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] internal static extern IntPtr GetClipboardOwner();
        [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
        [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size, Time; }
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(PixelPoint point, uint flags);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr window, int index, int value);

        internal static uint ProcessOf(IntPtr window)
        {
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            return pid;
        }

        internal static IntPtr RootAt(PixelPoint p) { return GetAncestor(WindowFromPoint(p), 2); }

        internal static uint LastInputTime()
        {
            var info = new LastInput { Size = (uint)Marshal.SizeOf(typeof(LastInput)) };
            return GetLastInputInfo(ref info) ? info.Time : 0;
        }

        internal static void MakeNonActivating(Window window)
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            SetWindowLong(handle, -20, GetWindowLong(handle, -20) | 0x08000000 | 0x80); // NOACTIVATE | TOOLWINDOW
            HwndSource.FromHwnd(handle).AddHook(delegate(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled) {
                if (msg == 0x21) { handled = true; return new IntPtr(3); } // WM_MOUSEACTIVATE, MA_NOACTIVATE
                return IntPtr.Zero;
            });
        }

        internal static void Place(Window window, SelectionAnchor anchor, bool dot = false)
        {
            var handle = new WindowInteropHelper(window).Handle;
            var point = anchor.End;
            var work = Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y)).WorkingArea;
            uint dx, dy;
            if (GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out dx, out dy) != 0) { dx = 96; dy = 96; }
            if (!dot)
            {
                window.MaxWidth = work.Width * 96.0 / dx;
                window.MaxHeight = work.Height * 96.0 / dy;
            }
            int width = (int)Math.Ceiling(window.ActualWidth * dx / 96.0), height = (int)Math.Ceiling(window.ActualHeight * dy / 96.0);
            var position = anchor.Position(new Rect(work.Left, work.Top, work.Width, work.Height),
                width, height, Math.Ceiling((dot ? 2 : 6) * dy / 96.0), dot);
            SetWindowPos(handle, IntPtr.Zero, position.X, position.Y, 0, 0, 0x15); // NOSIZE | NOZORDER | NOACTIVATE
        }
    }

    internal enum SelectionMouseEvent { Down, Up, Scroll }

    // Only selection gestures and scrolling are dispatched. UI Automation runs outside the mouse hook.
    internal sealed class SelectionMouse : IDisposable
    {
        private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        private readonly Dispatcher dispatcher;
        private readonly Action<SelectionMouseEvent, PixelPoint, int, IntPtr> report;
        private readonly HookProc callback;
        private IntPtr hook;

        public SelectionMouse(Dispatcher dispatcher, Action<SelectionMouseEvent, PixelPoint, int, IntPtr> report)
        {
            this.dispatcher = dispatcher;
            this.report = report;
            callback = Handle;
            hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
        }
        public bool Available { get { return hook != IntPtr.Zero; } }

        private IntPtr Handle(int code, IntPtr message, IntPtr data)
        {
            int msg = message.ToInt32();
            if (code >= 0 && (msg == 0x201 || msg == 0x202 || msg == 0x20A || msg == 0x20E))
            {
                var point = new PixelPoint(Marshal.ReadInt32(data), Marshal.ReadInt32(data, 4));
                int time = Marshal.ReadInt32(data, 16);
                var action = msg == 0x201 ? SelectionMouseEvent.Down : msg == 0x202 ? SelectionMouseEvent.Up : SelectionMouseEvent.Scroll;
                // The dot may already have been replaced by the popup when the dispatcher handles this click.
                var target = Native.RootAt(point);
                dispatcher.BeginInvoke(new Action(delegate { if (Available) report(action, point, time, target); }));
            }
            return CallNextHookEx(IntPtr.Zero, code, message, data);
        }

        public void Dispose() { if (Available) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; } }
    }

    internal sealed class Selection
    {
        internal readonly string Text;
        internal readonly bool IsPassword;
        internal readonly SelectionAnchor? Anchor;
        private readonly TextPatternRange range;
        private static readonly SemaphoreSlim contextWorkers = new SemaphoreSlim(2, 2);
        internal Selection(string text = null, bool password = false, SelectionAnchor? anchor = null, TextPatternRange range = null)
        { Text = text; IsPassword = password; Anchor = anchor; this.range = range; }

        internal LookupQuery BeginLookup()
        {
            var query = new LookupQuery(Text);
            if (query.Word != null && range != null) query.Context = ReadContextAsync(query.Word);
            return query;
        }

        private async Task<SentenceContext> ReadContextAsync(string word)
        {
            if (!contextWorkers.Wait(0)) return null;
            var read = Task.Run(delegate {
                try
                {
                    // Work on clones so reading the sentence cannot move the user's selection.
                    if (range.GetText(Text.Length + 1) != Text) return null;
                    var left = range.Clone();
                    left.MoveEndpointByRange(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start);
                    left.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -SentenceContext.SideLimit);
                    var right = range.Clone();
                    right.MoveEndpointByRange(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End);
                    right.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, SentenceContext.SideLimit);
                    if (left.CompareEndpoints(TextPatternRangeEndpoint.End, range, TextPatternRangeEndpoint.Start) != 0 ||
                        right.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) != 0) return null;
                    return SentenceContext.Extract(left.GetText(SentenceContext.SideLimit + 1), Text,
                        right.GetText(SentenceContext.SideLimit + 1), word);
                }
                catch (Exception) { return null; }
                finally { contextWorkers.Release(); }
            });
            return await Task.WhenAny(read, Task.Delay(800)).ConfigureAwait(false) == read ? await read.ConfigureAwait(false) : null;
        }
    }

    internal sealed class SelectionReader
    {
        private readonly SemaphoreSlim worker = new SemaphoreSlim(1, 1);

        public async Task<Selection> ReadAsync(IntPtr origin, PixelPoint point, bool preferPoint, PixelPoint? dragEnd = null)
        {
            // A slow or unresponsive accessibility provider must not stall the user's desktop.
            if (!await worker.WaitAsync(250)) return new Selection();
            var task = Task.Run(delegate {
                try { return Read(origin, point, preferPoint, dragEnd ?? point); }
                catch (Exception) { return new Selection(); }
                finally { worker.Release(); }
            });
            if (await Task.WhenAny(task, Task.Delay(preferPoint ? 1000 : 1500)) != task) return new Selection();
            return await task;
        }

        private static Selection Read(IntPtr origin, PixelPoint point, bool preferPoint, PixelPoint end)
        {
            if (origin == IntPtr.Zero || Native.GetForegroundWindow() != origin) return new Selection();
            var root = AutomationElement.FromHandle(origin);
            var first = ReadCandidate(origin, root, point, preferPoint, end);
            if (first.IsPassword || first.Text != null) return first;
            return ReadCandidate(origin, root, point, !preferPoint, end);
        }

        private static Selection ReadCandidate(IntPtr origin, AutomationElement root, PixelPoint point, bool usePoint, PixelPoint end)
        {
            try
            {
                IntPtr top = Native.GetAncestor(origin, 2);
                if (usePoint && Native.RootAt(point) != top) return new Selection();
                var element = usePoint ? AutomationElement.FromPoint(new Point(point.X, point.Y)) : AutomationElement.FocusedElement;
                // A provider can still report an element from the previously focused window.
                for (var parent = element; parent != null; parent = TreeWalker.RawViewWalker.GetParent(parent))
                {
                    if (Automation.Compare(parent, root)) return Walk(element, root, end);
                    int handle = parent.Current.NativeWindowHandle;
                    if (handle != 0)
                        return Native.GetAncestor(new IntPtr(handle), 2) == top ? Walk(element, root, end) : new Selection();
                }
            }
            catch (Exception) { /* The window or one of its accessibility elements may have gone away. */ }
            return new Selection();
        }

        private static bool IsPassword(AutomationElement element)
        {
            return element != null && true.Equals(element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty, true));
        }

        private static Selection Walk(AutomationElement element, AutomationElement root, PixelPoint point)
        {
            for (int depth = 0; element != null && depth < 12; depth++)
            {
                if (Automation.Compare(element, AutomationElement.RootElement)) break;
                if (IsPassword(element)) return new Selection(null, true);
                object pattern;
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out pattern) &&
                    ((TextPattern)pattern).SupportedTextSelection != SupportedTextSelection.None)
                {
                    var buffer = new System.Text.StringBuilder();
                    var lines = new List<Rect>();
                    var ranges = ((TextPattern)pattern).GetSelection();
                    foreach (var range in ranges)
                    {
                        int remaining = Translator.TextLimit + 1 - buffer.Length;
                        if (remaining <= 0) break;
                        string part = range.GetText(remaining);
                        if (!string.IsNullOrEmpty(part))
                        {
                            if (buffer.Length > 0) buffer.AppendLine();
                            buffer.Append(part);
                            try { lines.AddRange(range.GetBoundingRectangles()); }
                            catch (Exception) { /* Some text providers expose the selection but omit its geometry. */ }
                        }
                    }
                    string text = buffer.ToString();
                    if (!string.IsNullOrWhiteSpace(text)) return new Selection(text, false, SelectionAnchor.FromLines(lines, point),
                        ranges.Length == 1 ? ranges[0] : null);
                }
                if (Automation.Compare(element, root)) break;
                element = TreeWalker.RawViewWalker.GetParent(element);
            }
            return new Selection();
        }
    }
}
