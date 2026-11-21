using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ModToolEN
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

        public delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
        public delegate bool EnumProc(IntPtr h, IntPtr l);

        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, HookProc p, IntPtr mod, uint tid);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetWindowText(IntPtr h, string s);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumProc p, IntPtr l);
        [DllImport("user32.dll")] public static extern int GetDlgCtrlID(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr h, ref POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int DrawText(IntPtr dc, string s, int n, ref RECT r, uint f);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] public static extern int SetWindowTheme(IntPtr h, string app, string idList);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);

        public static string GetText(IntPtr h)
        {
            int n = GetWindowTextLength(h);
            var sb = new StringBuilder(n + 2);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static string GetClass(IntPtr h)
        {
            var sb = new StringBuilder(64);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        /// Dark or light title bar + scrollbars to match the theme (Windows 10 1809+; ignored elsewhere).
        public static void ThemeWindow(Control c, bool dark)
        {
            if (!c.IsHandleCreated) return;
            try
            {
                if (c is Form)
                {
                    int v = dark ? 1 : 0;
                    if (DwmSetWindowAttribute(c.Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(c.Handle, 19, ref v, 4);
                }
                else SetWindowTheme(c.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
            }
            catch { }
        }
    }

    /// Translates message boxes (and any other dialog) the original tool shows, and grows them to fit English text.
    static class MsgBoxHook
    {
        const int WH_CBT = 5, HCBT_ACTIVATE = 5, WM_GETFONT = 0x31;
        const uint DT_CALCRECT = 0x400, DT_WORDBREAK = 0x10, DT_NOPREFIX = 0x800, DT_EXPANDTABS = 0x40, DT_SINGLELINE = 0x20;
        static Native.HookProc proc;
        static readonly HashSet<IntPtr> done = new HashSet<IntPtr>();

        public static void Install()
        {
            proc = Hook;
            Native.SetWindowsHookEx(WH_CBT, proc, IntPtr.Zero, Native.GetCurrentThreadId());
        }

        static IntPtr Hook(int code, IntPtr w, IntPtr l)
        {
            if (code == HCBT_ACTIVATE)
            {
                try { if (Native.GetClass(w) == "#32770" && done.Add(w)) Fix(w); } catch { }
            }
            return Native.CallNextHookEx(IntPtr.Zero, code, w, l);
        }

        static void Fix(IntPtr dlg)
        {
            string cap = Native.GetText(dlg);
            if (Translator.HasCjk(cap)) Native.SetWindowText(dlg, Translator.T(cap));

            var kids = new List<IntPtr>();
            Native.EnumChildWindows(dlg, (h, p) => { kids.Add(h); return true; }, IntPtr.Zero);

            IntPtr msg = IntPtr.Zero; string oldText = null, newText = null;
            foreach (var h in kids)
            {
                string t = Native.GetText(h);
                if (!Translator.HasCjk(t)) continue;
                string n = Translator.T(t);
                Native.SetWindowText(h, n);
                if (Native.GetDlgCtrlID(h) == 0xFFFF && Native.GetClass(h) == "Static") { msg = h; oldText = t; newText = n; }
            }
            if (msg == IntPtr.Zero) return;

            // Grow the message box so the (longer) English text fits.
            Native.RECT sr; Native.GetWindowRect(msg, out sr);
            int curW = sr.Right - sr.Left, curH = sr.Bottom - sr.Top;
            IntPtr dc = Native.GetDC(msg);
            IntPtr font = Native.SendMessage(msg, WM_GETFONT, IntPtr.Zero, IntPtr.Zero);
            IntPtr old = Native.SelectObject(dc, font);
            var r = new Native.RECT { Right = 10000 };
            Native.DrawText(dc, newText, -1, ref r, DT_CALCRECT | DT_NOPREFIX | DT_EXPANDTABS);
            int maxW = (int)(520 * UI.S);
            int targetW = Math.Max(curW, Math.Min(r.Right - r.Left + 4, maxW));
            r = new Native.RECT { Right = targetW };
            Native.DrawText(dc, newText, -1, ref r, DT_CALCRECT | DT_NOPREFIX | DT_EXPANDTABS | DT_WORDBREAK);
            Native.SelectObject(dc, old);
            Native.ReleaseDC(msg, dc);
            int dw = Math.Max(0, targetW - curW), dh = Math.Max(0, (r.Bottom - r.Top) - curH);
            if (dw == 0 && dh == 0) return;

            foreach (var h in kids)
            {
                Native.RECT kr; Native.GetWindowRect(h, out kr);
                var p = new Native.POINT { X = kr.Left, Y = kr.Top };
                Native.ScreenToClient(dlg, ref p);
                int kw = kr.Right - kr.Left, kh = kr.Bottom - kr.Top;
                if (h == msg) Native.MoveWindow(h, p.X, p.Y, kw + dw, kh + dh, true);
                else if (Native.GetClass(h) == "Button") Native.MoveWindow(h, p.X + dw, p.Y + dh, kw, kh, true);
            }
            Native.RECT dr; Native.GetWindowRect(dlg, out dr);
            new FooterPainter(dlg);
            Native.MoveWindow(dlg, dr.Left - dw / 2, dr.Top - dh / 2, dr.Right - dr.Left + dw, dr.Bottom - dr.Top + dh, true);
        }

        /// The message box paints its gray button strip at its original size; repaint it across the resized box.
        class FooterPainter : NativeWindow
        {
            const int WM_PAINT = 0x0F, WM_NCDESTROY = 0x82, COLOR_WINDOW = 5, COLOR_BTNFACE = 15;
            [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out Native.RECT r);
            [DllImport("user32.dll")] static extern int FillRect(IntPtr dc, ref Native.RECT r, IntPtr brush);
            [DllImport("user32.dll")] static extern IntPtr GetSysColorBrush(int i);
            [DllImport("user32.dll")] static extern bool InvalidateRect(IntPtr h, IntPtr r, bool erase);
            [DllImport("user32.dll")] static extern bool UpdateWindow(IntPtr h);

            readonly int marginBelow = -1;
            static readonly List<FooterPainter> alive = new List<FooterPainter>();

            public FooterPainter(IntPtr dlg)
            {
                alive.Add(this);
                IntPtr btn = FirstButton(dlg);
                if (btn == IntPtr.Zero) return;
                Native.RECT cr; GetClientRect(dlg, out cr);
                Native.RECT br; Native.GetWindowRect(btn, out br);
                var p = new Native.POINT { X = br.Left, Y = br.Bottom };
                Native.ScreenToClient(dlg, ref p);
                marginBelow = cr.Bottom - p.Y;
                AssignHandle(dlg);
            }

            static IntPtr FirstButton(IntPtr dlg)
            {
                IntPtr found = IntPtr.Zero;
                Native.EnumChildWindows(dlg, (h, l) =>
                {
                    if (Native.GetClass(h) == "Button") { found = h; return false; }
                    return true;
                }, IntPtr.Zero);
                return found;
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg == WM_PAINT && marginBelow >= 0) Repaint();
                else if (m.Msg == WM_NCDESTROY) { ReleaseHandle(); alive.Remove(this); }
            }

            void Repaint()
            {
                IntPtr btn = FirstButton(Handle);
                if (btn == IntPtr.Zero) return;
                Native.RECT cr; GetClientRect(Handle, out cr);
                Native.RECT br; Native.GetWindowRect(btn, out br);
                var p = new Native.POINT { X = br.Left, Y = br.Top };
                Native.ScreenToClient(Handle, ref p);
                int footerTop = Math.Max(0, p.Y - marginBelow);
                IntPtr dc = Native.GetDC(Handle);
                var top = new Native.RECT { Left = 0, Top = 0, Right = cr.Right, Bottom = footerTop };
                var foot = new Native.RECT { Left = 0, Top = footerTop, Right = cr.Right, Bottom = cr.Bottom };
                FillRect(dc, ref top, GetSysColorBrush(COLOR_WINDOW));
                FillRect(dc, ref foot, GetSysColorBrush(COLOR_BTNFACE));
                Native.ReleaseDC(Handle, dc);
                Native.EnumChildWindows(Handle, (h, l) => { InvalidateRect(h, IntPtr.Zero, true); UpdateWindow(h); return true; }, IntPtr.Zero);
            }
        }
    }
}
