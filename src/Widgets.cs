using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ModToolEN
{
    /// Colour tokens copied from the NBA 2K27 Exporter stylesheet.
    class Theme
    {
        public bool Dark;
        public Color Bg, Panel, Panel2, Line, Text, Muted, Accent, Accent2, Green, Danger, Ink, Sidebar, Field, Hover, Selected, StrongLine;

        static Color H(string s) { return ColorTranslator.FromHtml(s); }

        // Electro blue on black.
        public static readonly Theme DarkTheme = new Theme
        {
            Dark = true, Bg = H("#05080f"), Panel = H("#0b121d"), Panel2 = H("#111c2c"), Line = H("#1d2e44"),
            Text = H("#e9f3ff"), Muted = H("#8198b4"), Accent = H("#17a8ff"), Accent2 = H("#5cc8ff"),
            Green = H("#38e0b0"), Danger = H("#ff5d6c"), Ink = H("#04070e"), Sidebar = H("#070b14"),
            Field = H("#081019"), Hover = H("#132740"), Selected = H("#0f2b48"), StrongLine = H("#2a6fb0")
        };

        public static readonly Theme LightTheme = new Theme
        {
            Dark = false, Bg = H("#eaf1f8"), Panel = H("#ffffff"), Panel2 = H("#e0e9f3"), Line = H("#c2d3e4"),
            Text = H("#102030"), Muted = H("#51647a"), Accent = H("#0a72d4"), Accent2 = H("#085bb0"),
            Green = H("#1f7a52"), Danger = H("#c0392b"), Ink = H("#ffffff"), Sidebar = H("#e4edf6"),
            Field = H("#ffffff"), Hover = H("#d4e6f8"), Selected = H("#cfe4fb"), StrongLine = H("#89b4e0")
        };

        public static Theme Cur = DarkTheme;
    }

    static class UI
    {
        public static float S = 1f;
        public static int D(int v) { return (int)Math.Round(v * S); }

        static string uiFamily, headFamily, iconFamily;
        // True when a symbol font with the MDL2 icon glyphs is available (Windows 10+).
        // On older Windows the glyphs are missing, so the UI falls back to text-only buttons.
        public static bool HasIconFont;

        public static void Init()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) S = g.DpiX / 96f;
            uiFamily = Has("Segoe UI") ? "Segoe UI" : "Microsoft Sans Serif";
            headFamily = Has("Bahnschrift") ? "Bahnschrift" : (Has("Segoe UI Semibold") ? "Segoe UI Semibold" : uiFamily);
            iconFamily = Has("Segoe MDL2 Assets") ? "Segoe MDL2 Assets" : (Has("Segoe Fluent Icons") ? "Segoe Fluent Icons" : null);
            HasIconFont = iconFamily != null;
            if (Environment.GetEnvironmentVariable("MODTOOL_NOICONS") == "1") { HasIconFont = false; iconFamily = null; }
        }

        static bool Has(string fam)
        {
            try { using (var f = new FontFamily(fam)) return f.Name == fam; } catch { return false; }
        }

        public static Font Font(float size, FontStyle st = FontStyle.Regular) { return new Font(uiFamily, size, st); }
        public static Font Head(float size, FontStyle st = FontStyle.Bold) { return new Font(headFamily, size, st); }
        public static Font Mono(float size) { return new Font("Consolas", size); }
        public static Font Icon(float size) { return new Font(iconFamily ?? uiFamily, size); }

        static Image logo; static bool logoTried;
        public static Image Logo
        {
            get
            {
                if (!logoTried)
                {
                    logoTried = true;
                    try { using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("app.logo.png")) if (s != null) logo = Image.FromStream(s); }
                    catch { }
                }
                return logo;
            }
        }

        public static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            if (rad <= 0) { p.AddRectangle(r); return p; }
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        /// Colour of the surface a control sits on.
        public static Color Surface(Control c)
        {
            for (var p = c.Parent; p != null; p = p.Parent)
            {
                var b = p as Box;
                if (b != null && b.FillFn != null) return b.FillFn(Theme.Cur);
                if (b == null && p.BackColor.A == 255) return p.BackColor;
            }
            return Theme.Cur.Bg;
        }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
    }

    /// Panel with an optional rounded fill/border, and optional vertical/horizontal stacking of its children.
    class Box : Panel
    {
        public Func<Theme, Color> FillFn;          // null = transparent
        public Func<Theme, Color> BorderFn;        // null = no border
        public int Radius;
        public bool Stack, Horizontal, AutoHeight;
        public int Gap;
        public bool LineTop, LineBottom, LineRight, LineLeft;
        public Action<Graphics, Box> Decorate;

        public Box()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        public void ApplyTheme(Theme t)
        {
            BackColor = FillFn != null && Radius == 0 ? FillFn(t) : UI.Surface(this);
            if (FillFn != null && Radius > 0) BackColor = UI.Surface(this);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var t = Theme.Cur;
            var g = e.Graphics;
            g.Clear(Radius > 0 || FillFn == null ? UI.Surface(this) : FillFn(t));
            if (Radius > 0 && (FillFn != null || BorderFn != null))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var path = UI.Round(r, Radius))
                {
                    if (FillFn != null) using (var b = new SolidBrush(FillFn(t))) g.FillPath(b, path);
                    if (BorderFn != null) using (var pen = new Pen(BorderFn(t))) g.DrawPath(pen, path);
                }
                g.SmoothingMode = SmoothingMode.None;
            }
            else if (BorderFn != null)
            {
                using (var pen = new Pen(BorderFn(t))) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
            using (var pen = new Pen(t.Line))
            {
                if (LineTop) g.DrawLine(pen, 0, 0, Width, 0);
                if (LineBottom) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
                if (LineRight) g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
                if (LineLeft) g.DrawLine(pen, 0, 0, 0, Height);
            }
            if (Decorate != null) Decorate(g, this);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            if (!Stack) { base.OnLayout(e); return; }
            var pad = Padding;
            if (Horizontal)
            {
                int n = 0;
                foreach (Control c in Controls) if (c.Visible) n++;
                if (n == 0) return;
                int w = (Width - pad.Horizontal - Gap * (n - 1)) / n, x = pad.Left, h = 0;
                foreach (Control c in Controls)
                {
                    if (!c.Visible) continue;
                    c.SetBounds(x, pad.Top, w, c.Height);
                    x += w + Gap; h = Math.Max(h, c.Height);
                }
                if (AutoHeight) Height = h + pad.Vertical;
                return;
            }
            int y = pad.Top, cw = Width - pad.Horizontal;
            foreach (Control c in Controls)
            {
                if (!c.Visible) continue;
                var tl = c as TLabel;
                if (tl != null && tl.Wrap) tl.Height = tl.Measure(cw);
                c.SetBounds(pad.Left, y, cw, c.Height);
                y += c.Height + Gap;
            }
            if (AutoHeight)
            {
                int want = (y == pad.Top ? y : y - Gap) + pad.Bottom;
                if (Height != want) Height = want;
            }
        }
    }

    /// Label whose colour/font follow the theme.
    class TLabel : Label
    {
        public Func<Theme, Color> ColorFn = t => t.Text;
        public bool Wrap;

        public TLabel(string text, Font font, Func<Theme, Color> color = null)
        {
            Text = text; Font = font; AutoSize = false; UseMnemonic = false;
            if (color != null) ColorFn = color;
            Height = TextRenderer.MeasureText("Ag", font).Height + UI.D(2);
            BackColor = Color.Transparent;
            AutoEllipsis = true;
        }

        public int Measure(int width)
        {
            var sz = TextRenderer.MeasureText(string.IsNullOrEmpty(Text) ? "A" : Text, Font, new Size(width, 10000),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return sz.Height + UI.D(2);
        }

        public void ApplyTheme(Theme t) { ForeColor = ColorFn(t); }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (Wrap && Parent != null) Parent.PerformLayout();
        }
    }

    enum BtnKind { Primary, Secondary, Ghost, Nav, NavActive, Toggle, ToggleOn }

    /// Flat, rounded button in the Exporter style.
    class FlatBtn : Control
    {
        public BtnKind Kind;
        public string Glyph;
        public bool AlignLeft;
        bool hover, down;

        public FlatBtn(string text, BtnKind kind, string glyph = null)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Text = text; Kind = kind; Glyph = glyph;
            Cursor = Cursors.Hand;
            Font = UI.Font(9f, FontStyle.Bold);
            Height = UI.D(36);
            AlignLeft = kind == BtnKind.Nav || kind == BtnKind.NavActive;
        }

        bool ShowGlyph { get { return Glyph != null && UI.HasIconFont; } }

        public int PreferredWidth()
        {
            int w = TextRenderer.MeasureText(Text, Font).Width + UI.D(32);
            if (ShowGlyph) w += UI.D(22);
            return w;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) OnClick(EventArgs.Empty);
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Cur;
            var g = e.Graphics;
            Color surface = UI.Surface(this);
            g.Clear(surface);
            Color bg, border, fg;
            switch (Kind)
            {
                case BtnKind.Primary:
                    bg = hover ? t.Accent2 : t.Accent; border = bg; fg = t.Dark ? Color.White : t.Ink; break;
                case BtnKind.Secondary:
                    bg = hover ? t.Hover : t.Panel2; border = t.Line; fg = t.Text; break;
                case BtnKind.Ghost:
                    bg = hover ? t.Hover : surface; border = hover ? t.Line : surface; fg = hover ? t.Text : t.Muted; break;
                case BtnKind.NavActive:
                    bg = t.Selected; border = t.StrongLine; fg = t.Accent2; break;
                case BtnKind.Toggle:
                    bg = hover ? t.Hover : surface; border = surface; fg = t.Muted; break;
                case BtnKind.ToggleOn:
                    bg = t.Selected; border = t.StrongLine; fg = t.Text; break;
                default: // Nav
                    bg = hover ? t.Hover : surface; border = bg; fg = hover ? t.Text : t.Muted; break;
            }
            if (Kind == BtnKind.Primary && !t.Dark) fg = Color.White;
            if (down) bg = UI.Mix(bg, t.Bg, 0.25f);
            if (!Enabled)
            {
                fg = UI.Mix(t.Muted, surface, 0.45f);
                if (Kind == BtnKind.Primary) { bg = UI.Mix(t.Accent, surface, 0.6f); border = bg; }
                else { bg = UI.Mix(bg, surface, 0.5f); border = UI.Mix(border, surface, 0.5f); }
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = UI.Round(r, UI.D(6)))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                using (var p = new Pen(border)) g.DrawPath(p, path);
            }
            g.SmoothingMode = SmoothingMode.None;
            if (Kind == BtnKind.NavActive)
                using (var b = new SolidBrush(t.Accent)) g.FillRectangle(b, 0, UI.D(6), UI.D(3), Height - UI.D(12));

            int pad = UI.D(14);
            var textRect = new Rectangle(pad, 0, Width - pad * 2, Height);
            if (ShowGlyph)
            {
                using (var gf = UI.Icon(10f))
                {
                    int gw = UI.D(18);
                    int textW = TextRenderer.MeasureText(Text, Font).Width;
                    int startX = AlignLeft ? pad : Math.Max(pad / 2, (Width - (gw + UI.D(6) + textW)) / 2);
                    Color gc = (Kind == BtnKind.Nav || Kind == BtnKind.Ghost) && !hover ? t.Muted : fg;
                    if (Kind == BtnKind.NavActive) gc = t.Accent;
                    TextRenderer.DrawText(g, Glyph, gf, new Rectangle(startX, 0, gw, Height), Enabled ? gc : fg,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    textRect = new Rectangle(startX + gw + UI.D(6), 0, Width - startX - gw - UI.D(6) - pad / 2, Height);
                    TextRenderer.DrawText(g, Text, Font, textRect, fg,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    return;
                }
            }
            TextRenderer.DrawText(g, Text, Font, textRect, fg,
                (AlignLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// "Resources ........ 12" row used in the sidebar catalog card.
    class KvRow : Control
    {
        string val = "0";
        public string Value { get { return val; } set { if (val != value) { val = value; Invalidate(); } } }

        public KvRow(string key)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = key; Font = UI.Font(9f); Height = UI.D(24);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Cur;
            e.Graphics.Clear(UI.Surface(this));
            using (var bold = UI.Font(9f, FontStyle.Bold))
            {
                int vw = Math.Min(Width * 2 / 3, TextRenderer.MeasureText(val, bold).Width + UI.D(4));
                TextRenderer.DrawText(e.Graphics, val, bold, new Rectangle(Width - vw, 0, vw, Height), t.Text,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(0, 0, Width - vw - UI.D(6), Height), t.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// Coloured status dot + text ("Indexed and ready").
    class StatusRow : Control
    {
        public bool Ok;
        public StatusRow(string text)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = text; Font = UI.Font(9f); Height = UI.D(24);
        }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Cur; var g = e.Graphics;
            g.Clear(UI.Surface(this));
            int d = UI.D(8), y = (Height - d) / 2;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Ok ? t.Green : t.Muted)) g.FillEllipse(b, UI.D(1), y, d, d);
            g.SmoothingMode = SmoothingMode.None;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(d + UI.D(9), 0, Width - d - UI.D(9), Height), t.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// Small outlined badge ("IFF EDITOR").
    class Badge : Control
    {
        public Func<Theme, Color> ColorFn = t => t.Green;
        public Badge(string text)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = text; Font = UI.Font(7.5f, FontStyle.Bold); Height = UI.D(30);
        }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Cur; var g = e.Graphics; var c = ColorFn(t);
            var surface = UI.Surface(this);
            g.Clear(surface);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = UI.Round(new Rectangle(0, 0, Width - 1, Height - 1), UI.D(5)))
            {
                using (var b = new SolidBrush(UI.Mix(surface, c, 0.08f))) g.FillPath(b, path);
                using (var p = new Pen(UI.Mix(surface, c, 0.55f))) g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(UI.D(8), 0, Width - UI.D(12), Height), c,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// App brand block at the top of the sidebar.
    class Brand : Control
    {
        public Brand()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = UI.D(54);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Cur; var g = e.Graphics;
            g.Clear(UI.Surface(this));
            int s = UI.D(40), y = (Height - s) / 2;
            var box = new Rectangle(0, y, s, s);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var logo = UI.Logo;
            if (logo != null)
            {
                using (var path = UI.Round(box, UI.D(9)))
                {
                    var oldClip = g.Clip; g.SetClip(path);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(logo, box);
                    g.Clip = oldClip;
                }
            }
            else
            {
                using (var path = UI.Round(box, UI.D(8)))
                using (var b = new LinearGradientBrush(box, t.Accent, UI.Mix(t.Accent, Color.Black, 0.35f), 45f))
                    g.FillPath(b, path);
                using (var f = UI.Head(10f))
                    TextRenderer.DrawText(g, "2K", f, box, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            g.SmoothingMode = SmoothingMode.None;
            int x = s + UI.D(12);
            using (var f1 = UI.Head(11.5f))
                TextRenderer.DrawText(g, "NBA2KHUB", f1, new Point(x, y + UI.D(2)), t.Text, TextFormatFlags.NoPadding);
            using (var f2 = UI.Font(7f, FontStyle.Bold))
                TextRenderer.DrawText(g, "MOD TOOL  ·  v1.0.0", f2, new Point(x + UI.D(1), y + UI.D(24)), t.Muted, TextFormatFlags.NoPadding);
        }
    }

    /// Horizontal strip of open-file tabs with a close button each and a "+" to open another.
    class TabStrip : Control
    {
        public readonly System.Collections.Generic.List<string> Paths = new System.Collections.Generic.List<string>();
        public int Active = -1;
        public Action<int> OnSelect, OnClose;
        public Action OnNew;

        readonly System.Collections.Generic.List<Rectangle> tabR = new System.Collections.Generic.List<Rectangle>();
        readonly System.Collections.Generic.List<Rectangle> closeR = new System.Collections.Generic.List<Rectangle>();
        Rectangle plusR;
        int hoverTab = -1, hoverClose = -1;

        public TabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = UI.D(46);
        }

        public void Refresh2() { Invalidate(); }

        static readonly float TabFont = 10f;

        void Recalc()
        {
            tabR.Clear(); closeR.Clear();
            int top = UI.D(6), h = Height - UI.D(7);
            int x = UI.D(10), pad = UI.D(16), closeW = UI.D(20), maxW = UI.D(280), minW = UI.D(150);
            using (var f = UI.Font(TabFont, FontStyle.Bold))
            {
                foreach (var p in Paths)
                {
                    string name = NameOf(p);
                    int tw = TextRenderer.MeasureText(name, f).Width + pad * 2 + closeW;
                    tw = Math.Max(minW, Math.Min(maxW, tw));
                    tabR.Add(new Rectangle(x, top, tw, h));
                    closeR.Add(new Rectangle(x + tw - closeW - UI.D(8), top + (h - closeW) / 2, closeW, closeW));
                    x += tw + UI.D(4);
                }
            }
            plusR = new Rectangle(x + UI.D(2), (Height - UI.D(30)) / 2, UI.D(32), UI.D(30));
        }

        static string NameOf(string p) { try { return System.IO.Path.GetFileName(p); } catch { return p; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            Recalc();
            var t = Theme.Cur; var g = e.Graphics;
            g.Clear(UI.Mix(t.Bg, t.Sidebar, 0.5f));
            using (var pen = new Pen(t.Line)) g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
            using (var f = UI.Font(TabFont, FontStyle.Bold))
            {
                for (int i = 0; i < tabR.Count; i++)
                {
                    var r = tabR[i]; bool act = i == Active;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var path = UI.Round(new Rectangle(r.X, r.Y, r.Width, r.Height + UI.D(8)), UI.D(8)))
                    {
                        using (var b = new SolidBrush(act ? t.Panel : (i == hoverTab ? t.Hover : UI.Mix(t.Bg, t.Panel, 0.4f)))) g.FillPath(b, path);
                        using (var pen = new Pen(act ? t.StrongLine : t.Line)) g.DrawPath(pen, path);
                    }
                    g.SmoothingMode = SmoothingMode.None;
                    if (act) using (var b = new SolidBrush(t.Accent)) g.FillRectangle(b, r.X + UI.D(1), r.Y, r.Width - UI.D(2), UI.D(2));
                    var cr = closeR[i];
                    var textRect = new Rectangle(r.X + UI.D(12), r.Y, cr.X - r.X - UI.D(14), r.Height);
                    TextRenderer.DrawText(g, NameOf(Paths[i]), f, textRect, act ? t.Text : t.Muted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    if (i == hoverClose) { g.SmoothingMode = SmoothingMode.AntiAlias; using (var b = new SolidBrush(t.Selected)) g.FillEllipse(b, cr); g.SmoothingMode = SmoothingMode.None; }
                    TextRenderer.DrawText(g, "×", UI.Font(10f, FontStyle.Bold), cr, i == hoverClose ? t.Danger : t.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                TextRenderer.DrawText(g, "+", UI.Head(16f), plusR, plusR.Contains(PointToClient(MousePosition)) ? t.Accent2 : t.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int ht = -1, hc = -1;
            for (int i = 0; i < tabR.Count; i++) { if (closeR[i].Contains(e.Location)) hc = i; else if (tabR[i].Contains(e.Location)) ht = i; }
            if (ht != hoverTab || hc != hoverClose) { hoverTab = ht; hoverClose = hc; Invalidate(); }
            Cursor = (ht >= 0 || hc >= 0 || plusR.Contains(e.Location)) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hoverTab = hoverClose = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            for (int i = 0; i < tabR.Count; i++)
            {
                if (closeR[i].Contains(e.Location)) { if (OnClose != null) OnClose(i); return; }
                if (tabR[i].Contains(e.Location)) { if (OnSelect != null) OnSelect(i); return; }
            }
            if (plusR.Contains(e.Location) && OnNew != null) OnNew();
            base.OnMouseDown(e);
        }
    }

    /// Search field: rounded box with a magnifier glyph and a borderless TextBox.
    class SearchBox : Box
    {
        public readonly TextBox Input = new TextBox();
        readonly Label glyph = new Label();
        public SearchBox(string placeholder)
        {
            Radius = UI.D(6);
            FillFn = t => t.Field;
            BorderFn = t => t.Line;
            Height = UI.D(38);
            glyph.Text = ""; glyph.Font = UI.Icon(10f); glyph.AutoSize = false; glyph.BackColor = Color.Transparent;
            glyph.TextAlign = ContentAlignment.MiddleCenter;
            glyph.Visible = UI.HasIconFont;
            Input.BorderStyle = BorderStyle.None; Input.Font = UI.Font(10f);
            Controls.Add(glyph); Controls.Add(Input);
            Input.GotFocus += (s, e) => Invalidate();
            Input.LostFocus += (s, e) => Invalidate();
            Placeholder = placeholder;
            Cursor = Cursors.IBeam;
            Click += (s, e) => Input.Focus();
        }
        public string Placeholder;
        protected override void OnLayout(LayoutEventArgs e)
        {
            int gw = UI.HasIconFont ? UI.D(34) : UI.D(10);
            glyph.SetBounds(UI.D(4), 0, UI.D(34), Height);
            int ih = Input.PreferredHeight;
            Input.SetBounds(gw + UI.D(4), (Height - ih) / 2, Width - gw - UI.D(14), ih);
        }
        public void ApplyThemeColors(Theme t)
        {
            Input.BackColor = t.Field; Input.ForeColor = t.Text; glyph.ForeColor = t.Accent;
        }
    }
}
