using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ModToolEN
{
    /// Re-skins the original tool window (Form1) into the NBA 2K27 Exporter layout.
    /// All original controls and their event handlers are kept; they are only moved and restyled.
    class Shell
    {
        const BindingFlags BF = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, string l);

        readonly Form f;
        readonly Type ft;

        // Original controls
        ListBox texList, modelList;
        Panel preview, oldSide;
        PictureBox pic;
        Label info, mipLabel;
        CheckBox alpha, parser2;
        NumericUpDown mip;
        Label lnkGame, lnkForum, lnkBoard, lnkBabu;
        MenuStrip menu;
        Button oldButton;
        ContextMenuStrip ctxTex, ctxModel;
        ToolStripMenuItem miOpen, miSave, miSaveAs, miAbout, miOrig, miFit, miExport, miReplace, miSetTex, miClearTex, miModelExport, miModelReplace;
        FieldInfo curFileField;

        // New UI
        Panel sidebar, main;
        StatusRow fileStatus;
        TLabel pathSide, headPath, texCount, modelCount, prevName, inspTexName, inspTexInfo, inspModelName, actionsTitle, actionsSub;
        KvRow kvTextures, kvModels, kvSelected;
        FlatBtn bSave, bSaveAs, bExport, bReplace, bSetTex, bClearTex, bModelExport, bModelReplace, bLight, bDark, bOrig, bFit, pathToggle;
        SearchBox search;
        TabStrip tabStrip;
        bool tabLoading;   // true while we load a file for a tab switch (suppresses tab bookkeeping in Sync)
        readonly List<ContextMenuStrip> menus = new List<ContextMenuStrip>();
        readonly HashSet<Form> styledForms = new HashSet<Form>();
        HelpForm help;

        public Shell(Form form) { f = form; ft = form.GetType(); }

        T Get<T>(string name) where T : class
        {
            var fi = ft.GetField(name, BF);
            return fi == null ? null : fi.GetValue(f) as T;
        }

        static void Click(ToolStripItem it) { if (it != null && it.Enabled) it.PerformClick(); }

        public void OpenFile(string path)
        {
            var m = ft.GetMethod("Loadfile", BF);
            if (m != null) m.Invoke(f, new object[] { path });
        }

        // ---------- tabs (multiple open files) ----------
        void OpenNewTab()
        {
            using (var dlg = new OpenFileDialog { Filter = "NBA 2K IFF|*.iff|All files|*.*", Title = "Open IFF file in a new tab" })
            {
                if (dlg.ShowDialog(f) != DialogResult.OK) return;
                AddTab(dlg.FileName);
            }
        }

        void AddTab(string path)
        {
            int ex = tabStrip.Paths.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            if (ex >= 0) { SwitchToTab(ex); return; }
            tabStrip.Paths.Add(path);
            tabStrip.Active = tabStrip.Paths.Count - 1;
            tabStrip.Refresh2();
            LoadTab(path);
        }

        void SwitchToTab(int i)
        {
            if (i < 0 || i >= tabStrip.Paths.Count || i == tabStrip.Active) return;
            if (!System.IO.File.Exists(tabStrip.Paths[i]))
            {
                MessageBox.Show(f, "This file no longer exists:\r\n" + tabStrip.Paths[i], AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            tabStrip.Active = i; tabStrip.Refresh2();
            LoadTab(tabStrip.Paths[i]);
        }

        void CloseTab(int i)
        {
            if (i < 0 || i >= tabStrip.Paths.Count) return;
            bool wasActive = i == tabStrip.Active;
            tabStrip.Paths.RemoveAt(i);
            if (tabStrip.Paths.Count == 0) { tabStrip.Active = -1; tabStrip.Refresh2(); ClearLoaded(); return; }
            if (tabStrip.Active > i) tabStrip.Active--;
            else if (tabStrip.Active == i) tabStrip.Active = Math.Min(i, tabStrip.Paths.Count - 1);
            tabStrip.Refresh2();
            if (wasActive) LoadTab(tabStrip.Paths[tabStrip.Active]);
        }

        void LoadTab(string path)
        {
            tabLoading = true;
            try { OpenFile(path); } finally { tabLoading = false; }
        }

        // Empties the loaded state (after the last tab is closed).
        void ClearLoaded()
        {
            try { texList.Items.Clear(); modelList.Items.Clear(); } catch { }
            try { if (pic.Image != null) pic.Image = null; pic.Visible = false; } catch { }
            try { if (curFileField != null) curFileField.SetValue(f, ""); } catch { }
            try { var ls = ft.GetField("loadsess", BF); if (ls != null) ls.SetValue(f, false); } catch { }
            if (preview != null) preview.Invalidate();
        }

        // Keeps the tab bar in step with whatever file is loaded (first load, drag-drop, grow-import).
        void SyncTabs(string file, bool hasFile)
        {
            if (tabLoading || tabStrip == null) return;
            if (!hasFile) return;
            if (tabStrip.Paths.Count == 0) { tabStrip.Paths.Add(file); tabStrip.Active = 0; tabStrip.Refresh2(); }
            else if (tabStrip.Active >= 0 && tabStrip.Active < tabStrip.Paths.Count &&
                     !string.Equals(tabStrip.Paths[tabStrip.Active], file, StringComparison.OrdinalIgnoreCase))
            { tabStrip.Paths[tabStrip.Active] = file; tabStrip.Refresh2(); }
        }

        // ---------- grow / smart-replace bridge ----------

        // Replace the selected texture with any image, growing the slot to the image's size if it is bigger
        // (like RED MC). Runs the bundled replace_tex helper, which resizes just that slot and re-encodes.
        void SmartReplace()
        {
            string cur = curFileField != null ? curFileField.GetValue(f) as string : null;
            if (string.IsNullOrEmpty(cur) || !System.IO.File.Exists(cur)) { System.Media.SystemSounds.Beep.Play(); return; }
            int idx = texList.SelectedIndex;
            if (idx < 0) { System.Media.SystemSounds.Beep.Play(); return; }

            int w, type;
            if (!SlotInfo(idx, out w, out type)) { MessageBox.Show(f, "Could not read the selected texture.", "Replace", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            string fmt = type == 1 ? "DXT1" : type == 5 ? "DXT5" : type == -43 ? "ARGB" : null;
            if (fmt == null) { MessageBox.Show(f, "This texture's format can't be grown here.", "Replace", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            int ordinal = 0;
            for (int j = 0; j < idx; j++) { int w2, t2; if (SlotInfo(j, out w2, out t2) && w2 == w && t2 == type) ordinal++; }

            string image = Environment.GetEnvironmentVariable("MODTOOL_REPLACE_IMG");
            if (string.IsNullOrEmpty(image))
            {
                using (var dlg = new OpenFileDialog { Filter = "Images|*.dds;*.png;*.bmp;*.jpg;*.jpeg;*.tga;*.gif|All files|*.*", Title = "Replace texture with image (resizes the slot to fit)" })
                {
                    if (dlg.ShowDialog(f) != DialogResult.OK) return;
                    image = dlg.FileName;
                }
            }

            string outFile = cur.EndsWith("_1024.iff", StringComparison.OrdinalIgnoreCase) ? cur
                : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(cur), System.IO.Path.GetFileNameWithoutExtension(cur) + "_1024.iff");
            string tmp = outFile + ".tmp";
            try { if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp); } catch { }

            string outp;
            string err = RunConverter("replace_tex.py",
                new[] { cur, tmp, fmt, w.ToString(), ordinal.ToString(), image }, out outp);
            if (err != null || !System.IO.File.Exists(tmp))
            {
                try { if (System.IO.File.Exists(tmp)) System.IO.File.Delete(tmp); } catch { }
                MessageBox.Show(f, "Replace failed:\r\n\r\n" + (err ?? outp), "Replace", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            try { if (System.IO.File.Exists(outFile)) System.IO.File.Delete(outFile); System.IO.File.Move(tmp, outFile); }
            catch (Exception ex) { MessageBox.Show(f, "Could not write the result:\r\n" + ex.Message, "Replace", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

            bool wasNew = !string.Equals(outFile, cur, StringComparison.OrdinalIgnoreCase);
            OpenFile(outFile);
            if (idx < texList.Items.Count) texList.SelectedIndex = idx;
            if (wasNew)
                MessageBox.Show(f, "Imported into a new file (your original is untouched):\r\n" + outFile +
                    "\r\n\r\n" + outp.Trim() + "\r\n\r\nFurther edits go to this file. Test it in NBA 2K14.",
                    "Replace", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Removes a ToolStripItem's existing Click handlers (the core's) and attaches ours.
        void RewireClick(ToolStripItem item, Action action)
        {
            if (item == null) return;
            try
            {
                var keyF = typeof(ToolStripItem).GetField("EventClick", BindingFlags.Static | BindingFlags.NonPublic);
                var eventsP = typeof(System.ComponentModel.Component).GetProperty("Events", BindingFlags.Instance | BindingFlags.NonPublic);
                if (keyF != null && eventsP != null)
                {
                    object key = keyF.GetValue(null);
                    var list = (System.ComponentModel.EventHandlerList)eventsP.GetValue(item, null);
                    var existing = list[key];
                    if (existing != null) list.RemoveHandler(key, existing);
                }
            }
            catch { }
            item.Click += (s, e) => action();
        }

        const string GitHubUrl = "https://github.com/BlueCode10";
        void ShowAbout()
        {
            var t = Theme.Cur;
            using (var dlg = new Form())
            {
                dlg.Text = "NBA2KHUB Mod Tool v1.0.2.0";
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = dlg.MinimizeBox = false; dlg.ShowInTaskbar = false;
                dlg.ClientSize = new Size(UI.D(400), UI.D(176));
                dlg.BackColor = t.Panel; dlg.ForeColor = t.Text; dlg.Font = UI.Font(9.5f);
                try { dlg.Icon = f.Icon; } catch { }

                var logo = UI.Logo;
                if (logo != null)
                    dlg.Controls.Add(new PictureBox { Image = logo, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent, Bounds = new Rectangle(UI.D(22), UI.D(22), UI.D(60), UI.D(60)) });
                int x = UI.D(98);
                dlg.Controls.Add(new Label { Text = "NBA2KHUB Mod Tool", Font = UI.Head(13.5f), ForeColor = t.Text, AutoSize = true, BackColor = Color.Transparent, Location = new Point(x, UI.D(26)) });
                dlg.Controls.Add(new Label { Text = "Version 1.0.2.0", Font = UI.Font(9f), ForeColor = t.Muted, AutoSize = true, BackColor = Color.Transparent, Location = new Point(x, UI.D(52)) });
                var git = new LinkLabel { Text = "github.com/BlueCode10", AutoSize = true, BackColor = Color.Transparent, Font = UI.Font(9.5f, FontStyle.Bold), LinkColor = t.Accent2, ActiveLinkColor = t.Accent, Location = new Point(x, UI.D(78)) };
                git.LinkClicked += (s, e) => { try { System.Diagnostics.Process.Start(GitHubUrl); } catch { } };
                dlg.Controls.Add(git);

                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = t.Accent, ForeColor = Color.White, Size = new Size(UI.D(92), UI.D(30)) };
                ok.FlatAppearance.BorderColor = t.Accent;
                ok.Location = new Point(dlg.ClientSize.Width - ok.Width - UI.D(20), dlg.ClientSize.Height - ok.Height - UI.D(18));
                dlg.Controls.Add(ok); dlg.AcceptButton = ok;
                dlg.HandleCreated += (s, e) => Native.ThemeWindow(dlg, t.Dark);
                dlg.ShowDialog(f);
            }
        }

        public void TestAddTab(string path) { AddTab(path); }
        public void TestSwitchTab(int i) { SwitchToTab(i); }
        public void TestCloseTab(int i) { CloseTab(i); }
        public int TestTabCount() { return tabStrip != null ? tabStrip.Paths.Count : 0; }
        public int TestActiveTab() { return tabStrip != null ? tabStrip.Active : -1; }
        public void TestSmartReplace(int idx) { if (idx < texList.Items.Count) texList.SelectedIndex = idx; SmartReplace(); }
        public void TestMenuReplace(int idx) { if (idx < texList.Items.Count) texList.SelectedIndex = idx; if (miReplace != null) miReplace.PerformClick(); }
        public string TestCurFile2() { return curFileField != null ? curFileField.GetValue(f) as string : null; }

        // Reads a texture slot's width and format-type from the loaded tool state.
        bool SlotInfo(int idx, out int w, out int type)
        {
            w = 0; type = 0;
            try
            {
                var fi = ft.GetField("iff", BF);
                object iffObj = fi != null ? fi.GetValue(f) : null;
                if (iffObj == null) return false;
                var imagF = iffObj.GetType().GetField("imagchnk", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var arr = imagF != null ? imagF.GetValue(iffObj) as Array : null;
                if (arr == null || idx < 0 || idx >= arr.Length) return false;
                object slot = arr.GetValue(idx); var st = slot.GetType();
                w = (int)st.GetField("w").GetValue(slot);
                type = (int)st.GetField("type").GetValue(slot);
                return true;
            }
            catch { return false; }
        }

        // Runs the helper (bundled exe, or a .py via Python), returns null on success
        // (with stdout in outp) or an error message.
        string RunConverter(string scriptName, string[] args, out string outp)
        {
            outp = "";
            string baseName = System.IO.Path.GetFileNameWithoutExtension(scriptName);
            string fileName, workDir;
            var a = new System.Text.StringBuilder();

            // Prefer the bundled standalone exe (no Python needed); fall back to running the .py via Python.
            string exe = FindConverterExe(baseName);
            if (exe != null)
            {
                fileName = exe; workDir = System.IO.Path.GetDirectoryName(exe);
            }
            else
            {
                string script = FindConverterScript(scriptName);
                if (script == null)
                {
                    MessageBox.Show(f,
                        "This feature's helper (" + baseName + ".exe) was not found next to the app.\r\n\r\nReinstall the full (standalone) download, which includes it.",
                        AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    using (var dlg = new OpenFileDialog { Filter = scriptName + "|" + scriptName + "|Python scripts|*.py", Title = "Locate " + scriptName })
                    {
                        if (dlg.ShowDialog(f) != DialogResult.OK) return "cancelled";
                        script = dlg.FileName;
                        try { System.IO.File.WriteAllText(ConverterDirFile, System.IO.Path.GetDirectoryName(script)); } catch { }
                    }
                }
                string py = FindPython();
                if (py == null) return "Neither the bundled " + baseName + ".exe nor Python 3 (with numpy + pillow) was found.";
                fileName = py; workDir = System.IO.Path.GetDirectoryName(script);
                a.Append("\"").Append(script).Append("\"");
            }

            foreach (var x in args) { if (a.Length > 0) a.Append(' '); a.Append("\"").Append(x).Append("\""); }
            string err = "";
            var old = Cursor.Current; Cursor.Current = Cursors.WaitCursor;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileName, Arguments = a.ToString(),
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    CreateNoWindow = true, WorkingDirectory = workDir
                };
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    outp = p.StandardOutput.ReadToEnd();
                    err = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0) return (err.Trim().Length > 0 ? err : outp).Trim();
                }
            }
            catch (Exception ex) { return "Could not run the helper:\r\n" + ex.Message; }
            finally { Cursor.Current = old; }
            return null;
        }

        // Looks for a bundled standalone exe (e.g. replace_tex.exe) next to this app or in a runtime subfolder.
        static string FindConverterExe(string baseName)
        {
            string dir = "";
            try { dir = AppDomain.CurrentDomain.BaseDirectory; } catch { }
            var cands = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(dir))
            {
                cands.Add(System.IO.Path.Combine(dir, baseName, baseName + ".exe"));
                cands.Add(System.IO.Path.Combine(dir, "runtime", baseName + ".exe"));
                cands.Add(System.IO.Path.Combine(dir, baseName + ".exe"));
            }
            foreach (var c in cands) { try { if (System.IO.File.Exists(c)) return c; } catch { } }
            return null;
        }

        static string ConverterDirFile { get { return System.IO.Path.Combine(Program.DataDir ?? System.IO.Path.GetTempPath(), "helper_dir.txt"); } }

        // Fallback (dev only): a .py helper next to the app or at a remembered location.
        static string FindConverterScript(string name)
        {
            var dirs = new System.Collections.Generic.List<string>();
            try { string s = System.IO.File.Exists(ConverterDirFile) ? System.IO.File.ReadAllText(ConverterDirFile).Trim() : null; if (!string.IsNullOrEmpty(s)) dirs.Add(s); } catch { }
            try { dirs.Add(AppDomain.CurrentDomain.BaseDirectory); } catch { }
            foreach (var d in dirs) { try { string p = System.IO.Path.Combine(d, name); if (System.IO.File.Exists(p)) return p; } catch { } }
            return null;
        }

        static string FindPython()
        {
            foreach (string cand in new[] { "py", "python" })
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = cand, Arguments = "--version",
                        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
                    };
                    using (var p = System.Diagnostics.Process.Start(psi)) { p.WaitForExit(4000); if (p.HasExited && p.ExitCode == 0) return cand; }
                }
                catch { }
            }
            return null;
        }

        public void Build()
        {
            UI.Init();
            LoadSettings();

            texList = Get<ListBox>("listBox1");
            modelList = Get<ListBox>("listBox2");
            preview = Get<Panel>("panel1");
            oldSide = Get<Panel>("panel2");
            pic = Get<PictureBox>("pictureBox1");
            info = Get<Label>("label1");
            mipLabel = Get<Label>("label5");
            alpha = Get<CheckBox>("checkBox1");
            parser2 = Get<CheckBox>("checkBox2");
            mip = Get<NumericUpDown>("numericUpDown1");
            lnkGame = Get<Label>("label2");
            lnkBoard = Get<Label>("label3");
            lnkForum = Get<Label>("label4");
            lnkBabu = Get<Label>("label6");
            menu = Get<MenuStrip>("menuStrip1");
            oldButton = Get<Button>("button1");
            ctxTex = Get<ContextMenuStrip>("contextMenuStrip1");
            ctxModel = Get<ContextMenuStrip>("ModelMenuStrip");
            miOpen = Get<ToolStripMenuItem>("打开ToolStripMenuItem");
            miSave = Get<ToolStripMenuItem>("保存ToolStripMenuItem");
            miSaveAs = Get<ToolStripMenuItem>("另存为ToolStripMenuItem");
            miAbout = Get<ToolStripMenuItem>("关于ToolStripMenuItem");
            miOrig = Get<ToolStripMenuItem>("原始大小ToolStripMenuItem");
            miFit = Get<ToolStripMenuItem>("适应窗口ToolStripMenuItem");
            miExport = Get<ToolStripMenuItem>("导出ToolStripMenuItem");
            miReplace = Get<ToolStripMenuItem>("替换ToolStripMenuItem");
            miSetTex = Get<ToolStripMenuItem>("设为模型贴图ToolStripMenuItem");
            miClearTex = Get<ToolStripMenuItem>("清除模型贴图ToolStripMenuItem");
            miModelExport = Get<ToolStripMenuItem>("toolStripMenuItem1");
            miModelReplace = Get<ToolStripMenuItem>("toolStripMenuItem2");
            curFileField = ft.GetField("curfile", BF);
            // Rewire the right-click "Replace" (and the model menu's) to the smart grow-import.
            RewireClick(miReplace, () => SmartReplace());
            RewireClick(miOpen, () => OpenNewTab());
            RewireClick(miAbout, () => ShowAbout());

            TranslateEverything();

            f.SuspendLayout();
            f.AutoScaleMode = AutoScaleMode.None;
            f.Font = UI.Font(9f);

            // Detach the original controls from their old containers.
            foreach (Control c in new Control[] { texList, modelList, preview, info, mipLabel, alpha, parser2, mip, lnkGame, lnkBoard, lnkForum, lnkBabu })
                if (c != null && c.Parent != null) c.Parent.Controls.Remove(c);
            if (oldSide != null) oldSide.Visible = false;
            if (oldButton != null) oldButton.Visible = false;
            if (menu != null) menu.Visible = false;
            if (modelList != null && ctxModel != null) modelList.ContextMenuStrip = ctxModel;

            BuildSidebar();
            BuildMain();

            f.Controls.Add(main);
            f.Controls.Add(sidebar);
            main.BringToFront();

            f.MinimumSize = new Size(UI.D(1080), UI.D(700));
            var wa = Screen.PrimaryScreen.WorkingArea;
            f.Size = new Size(Math.Min(UI.D(1380), wa.Width - UI.D(40)), Math.Min(UI.D(880), wa.Height - UI.D(40)));
            f.StartPosition = FormStartPosition.CenterScreen;
            f.KeyPreview = true;
            f.KeyDown += OnKey;
            f.ResumeLayout(true);

            f.HandleCreated += (s, e) => ApplyTheme();
            foreach (Control c in new Control[] { texList, modelList, preview })
                c.HandleCreated += (s, e) => Native.ThemeWindow((Control)s, Theme.Cur.Dark);
            Application.Idle += (s, e) => Sync();
            ApplyTheme();
        }

        // ---------- translation of the original UI ----------

        public const string AppTitle = "NBA2KHUB Mod Tool v1.0.2";
        void Retitle()
        {
            if (f.Text != AppTitle) f.Text = AppTitle;
        }

        void TranslateEverything()
        {
            Retitle();
            f.TextChanged += (s, e) => Retitle();
            try { using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.icon.ico")) if (s != null) f.Icon = new System.Drawing.Icon(s); } catch { }
            HookControls(f);
            foreach (var fi in ft.GetFields(BF))
            {
                object v = fi.GetValue(f);
                var ts = v as ToolStrip;
                if (ts != null) { HookItems(ts.Items); var cms = ts as ContextMenuStrip; if (cms != null) menus.Add(cms); }
                var fd = v as FileDialog;
                if (fd != null) { fd.Filter = Translator.T(fd.Filter); fd.Title = Translator.T(fd.Title); }
                var tt = v as ToolTip;
                if (tt != null && parser2 != null) tt.SetToolTip(parser2, Translator.T(tt.GetToolTip(parser2)));
            }
        }

        static void HookControls(Control c)
        {
            if (Translator.HasCjk(c.Text)) c.Text = Translator.T(c.Text);
            c.TextChanged += (s, e) =>
            {
                var cc = (Control)s;
                if (Translator.HasCjk(cc.Text)) { string n = Translator.T(cc.Text); if (n != cc.Text) cc.Text = n; }
            };
            foreach (Control k in c.Controls) HookControls(k);
        }

        static void HookItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem it in items)
            {
                it.Text = Translator.T(it.Text);
                it.ToolTipText = Translator.T(it.ToolTipText);
                it.TextChanged += (s, e) => { var i = (ToolStripItem)s; string n = Translator.T(i.Text); if (n != i.Text) i.Text = n; };
                var mi = it as ToolStripMenuItem;
                if (mi != null) HookItems(mi.DropDownItems);
            }
        }

        // ---------- layout ----------

        static Box Card(bool hot = false)
        {
            var b = new Box
            {
                Stack = true, AutoHeight = true, Radius = UI.D(8), Gap = UI.D(6),
                Padding = new Padding(UI.D(14), UI.D(13), UI.D(14), UI.D(14)),
                FillFn = hot ? (Func<Theme, Color>)(t => UI.Mix(t.Sidebar, t.Selected, 0.75f)) : (t => t.Panel),
                BorderFn = hot ? (Func<Theme, Color>)(t => t.StrongLine) : (t => t.Line),
            };
            return b;
        }

        static TLabel Caption(string text)
        {
            return new TLabel(text.ToUpperInvariant(), UI.Font(7.5f, FontStyle.Bold), t => t.Accent);
        }

        static void AddAll(Control parent, params Control[] kids) { foreach (var k in kids) parent.Controls.Add(k); }

        void BuildSidebar()
        {
            sidebar = new Panel { Dock = DockStyle.Left, Width = UI.D(252) };
            var top = new Box { Dock = DockStyle.Fill, Stack = true, Gap = UI.D(12), FillFn = t => t.Sidebar, LineRight = true,
                Padding = new Padding(UI.D(14), UI.D(16), UI.D(15), UI.D(8)) };
            var bottom = new Box { Dock = DockStyle.Bottom, Stack = true, AutoHeight = true, Gap = UI.D(10), FillFn = t => t.Sidebar, LineRight = true,
                Padding = new Padding(UI.D(14), UI.D(8), UI.D(15), UI.D(14)) };

            var brand = new Brand();
            var navBrowse = new FlatBtn("IFF browser", BtnKind.NavActive, "");
            var navHelp = new FlatBtn("Readme && guide", BtnKind.Nav, "");
            navHelp.Text = "Readme & guide";
            var navAbout = new FlatBtn("About", BtnKind.Nav, "");
            navBrowse.Click += (s, e) => texList.Focus();
            navHelp.Click += (s, e) => ShowHelp();
            navAbout.Click += (s, e) => Click(miAbout);
            var nav = new Box { Stack = true, AutoHeight = true, Gap = UI.D(2) };
            AddAll(nav, navBrowse, navHelp, navAbout);

            // Game target card
            var game = Card(true);
            fileStatus = new StatusRow("No file loaded");
            var openBtn = new FlatBtn("Open IFF file", BtnKind.Secondary, "");
            openBtn.Click += (s, e) => Click(miOpen);
            pathToggle = new FlatBtn("▸  File path", BtnKind.Ghost) { Height = UI.D(26), AlignLeft = true, Font = UI.Font(8.5f) };
            pathSide = new TLabel("No file open", UI.Font(8.25f), t => t.Muted) { Wrap = true, Visible = false };
            pathToggle.Click += (s, e) =>
            {
                pathSide.Visible = !pathSide.Visible;
                pathToggle.Text = (pathSide.Visible ? "▾" : "▸") + "  File path";
                game.PerformLayout(); top.PerformLayout();
            };
            AddAll(game, Caption("Game target"), new TLabel("NBA 2K", UI.Head(15f)) { Height = UI.D(30) }, fileStatus, Spacer(4), openBtn, pathToggle, pathSide);

            // Catalog card
            var catalog = Card();
            kvTextures = new KvRow("Textures"); kvModels = new KvRow("Models"); kvSelected = new KvRow("Selected");
            kvSelected.Value = "—";
            AddAll(catalog, Caption("Catalog"), kvTextures, kvModels, kvSelected);

            // Community links (original link labels, translated)
            var links = Card();
            links.Controls.Add(Caption("Community"));
            var hub = new TLabel("nba2khub.app  ↗", UI.Font(9f, FontStyle.Bold), t => t.Accent2)
            {
                Height = UI.D(24), Cursor = Cursors.Hand, TextAlign = ContentAlignment.MiddleLeft
            };
            hub.Click += (s, e) => { try { System.Diagnostics.Process.Start("https://nba2khub.app"); } catch { } };
            hub.MouseEnter += (s, e) => hub.Font = UI.Font(9f, FontStyle.Bold | FontStyle.Underline);
            hub.MouseLeave += (s, e) => hub.Font = UI.Font(9f, FontStyle.Bold);
            links.Controls.Add(hub);
            // (3DM / Babu links removed; only nba2khub.app is shown.)

            AddAll(top, brand, nav, game, catalog, links);

            var badgeRow = new Box { Stack = true, Horizontal = true, AutoHeight = true, Gap = UI.D(8) };
            var badge = new Badge("IFF EDITOR");
            var ver = new Badge("v1.0.2") { ColorFn = t => t.Muted };
            AddAll(badgeRow, badge, ver);
            var toggle = new Box { Stack = true, Horizontal = true, AutoHeight = true, Gap = UI.D(4), Radius = UI.D(8),
                FillFn = t => t.Field, BorderFn = t => t.Line, Padding = new Padding(UI.D(4)) };
            bLight = new FlatBtn("Light", BtnKind.Toggle, "") { Height = UI.D(32), Font = UI.Font(8.75f) };
            bDark = new FlatBtn("Dark", BtnKind.Toggle, "") { Height = UI.D(32), Font = UI.Font(8.75f) };
            bLight.Click += (s, e) => SetTheme(false);
            bDark.Click += (s, e) => SetTheme(true);
            AddAll(toggle, bLight, bDark);
            AddAll(bottom, badgeRow, toggle);

            sidebar.Controls.Add(top);
            sidebar.Controls.Add(bottom);
            top.BringToFront();
        }

        static Control Spacer(int h) { return new Panel { Height = UI.D(h), BackColor = Color.Transparent }; }

        void BuildMain()
        {
            main = new Panel { Dock = DockStyle.Fill };

            // ----- header -----
            var header = new Box { Dock = DockStyle.Top, Height = UI.D(122), FillFn = t => t.Bg };
            header.Decorate = (g, b) =>
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var p = new Pen(Color.FromArgb(Theme.Cur.Dark ? 22 : 40, Theme.Cur.Muted), UI.D(1)))
                {
                    int cx = (int)(b.Width * 0.62), cy = UI.D(10);
                    foreach (int r in new[] { 70, 120, 175 }) g.DrawEllipse(p, cx - UI.D(r), cy - UI.D(r), UI.D(r) * 2, UI.D(r) * 2);
                }
                g.SmoothingMode = SmoothingMode.None;
            };
            var eyebrow = new TLabel("NBA 2K12  /  IFF ARCHIVES", UI.Font(7.5f, FontStyle.Bold), t => t.Accent);
            var title = new TLabel("Texture browser", UI.Head(21f));
            headPath = new TLabel("No IFF file open. Drag an .iff file onto this window or click Open file.", UI.Font(9f), t => t.Muted);
            var bOpen = new FlatBtn("Open file", BtnKind.Secondary, "");
            bSaveAs = new FlatBtn("Save as", BtnKind.Secondary, "");
            bSave = new FlatBtn("Save IFF", BtnKind.Primary, "");
            bOpen.Click += (s, e) => OpenNewTab();
            bSaveAs.Click += (s, e) => Click(miSaveAs);
            bSave.Click += (s, e) => Click(miSave);
            AddAll(header, eyebrow, title, headPath, bOpen, bSaveAs, bSave);
            header.Layout += (s, e) =>
            {
                int x = UI.D(28);
                eyebrow.SetBounds(x, UI.D(22), UI.D(400), eyebrow.Height);
                title.SetBounds(x, UI.D(38), UI.D(520), UI.D(44));
                int bx = header.Width - UI.D(24);
                foreach (var b in new[] { bSave, bSaveAs, bOpen })
                {
                    int w = b.PreferredWidth();
                    bx -= w;
                    b.SetBounds(bx, UI.D(40), w, UI.D(38));
                    bx -= UI.D(10);
                }
                headPath.SetBounds(x, UI.D(86), Math.Max(UI.D(100), header.Width - x - UI.D(24)), headPath.Height);
            };

            // ----- bottom action bar -----
            var bar = new Box { Dock = DockStyle.Bottom, Height = UI.D(84), FillFn = t => t.Panel, LineTop = true };
            var barIcon = new Box { Radius = UI.D(6), FillFn = t => t.Selected, BorderFn = t => t.StrongLine, Size = new Size(UI.D(34), UI.D(34)) };
            var barGlyph = new TLabel(UI.HasIconFont ? "" : "", UI.Icon(12f), t => t.Accent) { TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill };
            barIcon.Controls.Add(barGlyph);
            var barCap = new TLabel("TEXTURE ACTIONS", UI.Font(7.5f, FontStyle.Bold), t => t.Muted);
            actionsTitle = new TLabel("No texture selected", UI.Font(10f, FontStyle.Bold));
            actionsSub = new TLabel("Export or replace the selected texture, then click Save IFF.", UI.Font(8.5f), t => t.Muted);
            bReplace = new FlatBtn("Replace", BtnKind.Secondary, "");
            bSetTex = new FlatBtn("Set as model texture", BtnKind.Secondary, "");
            bClearTex = new FlatBtn("Clear model texture", BtnKind.Secondary, "");
            bExport = new FlatBtn("Export texture", BtnKind.Primary, "");
            bReplace.Click += (s, e) => SmartReplace();
            bSetTex.Click += (s, e) => Click(miSetTex);
            bClearTex.Click += (s, e) => Click(miClearTex);
            bExport.Click += (s, e) => Click(miExport);
            AddAll(bar, barIcon, barCap, actionsTitle, actionsSub, bReplace, bSetTex, bClearTex, bExport);
            bar.Layout += (s, e) =>
            {
                int x = UI.D(24);
                barIcon.SetBounds(x, (bar.Height - barIcon.Height) / 2, barIcon.Width, barIcon.Height);
                int bx = bar.Width - UI.D(24);
                foreach (var b in new[] { bExport, bClearTex, bSetTex, bReplace })
                {
                    int w = b == bExport ? Math.Max(UI.D(170), b.PreferredWidth()) : b.PreferredWidth();
                    bx -= w;
                    b.SetBounds(bx, (bar.Height - UI.D(40)) / 2, w, UI.D(40));
                    bx -= UI.D(10);
                }
                int tx = x + barIcon.Width + UI.D(14), tw = Math.Max(UI.D(60), bx - tx - UI.D(10));
                barCap.SetBounds(tx, UI.D(14), tw, barCap.Height);
                actionsTitle.SetBounds(tx, UI.D(30), tw, actionsTitle.Height);
                actionsSub.SetBounds(tx, UI.D(52), tw, actionsSub.Height);
            };

            // ----- content: browser card + inspector -----
            var content = new Box { Dock = DockStyle.Fill, FillFn = t => t.Bg, Padding = new Padding(UI.D(24), UI.D(2), UI.D(24), UI.D(18)) };

            var inspector = BuildInspector();
            var gap = new Panel { Dock = DockStyle.Right, Width = UI.D(16), BackColor = Color.Transparent };

            var browser = new Box { Dock = DockStyle.Fill, Radius = UI.D(10), FillFn = t => t.Panel, BorderFn = t => t.Line, Padding = new Padding(UI.D(1)) };

            // toolbar
            var toolbar = new Box { Dock = DockStyle.Top, Height = UI.D(66), LineBottom = true };
            search = new SearchBox("Search textures and models…");
            SendCue(search.Input, "Search textures and models…");
            var bFind = new FlatBtn("Find next", BtnKind.Secondary) { Font = UI.Font(8.75f, FontStyle.Bold) };
            bFind.Click += (s, e) => FindNext();
            search.Input.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; FindNext(); } };
            var hint = new Badge("CTRL  F") { ColorFn = t => t.Muted, Font = UI.Font(7f, FontStyle.Bold) };
            AddAll(toolbar, search, bFind, hint);
            toolbar.Layout += (s, e) =>
            {
                int x = UI.D(16), h = UI.D(38), y = (toolbar.Height - h) / 2;
                int fw = bFind.PreferredWidth();
                int sw = Math.Max(UI.D(200), toolbar.Width - x * 2 - fw - UI.D(10) - UI.D(70) - UI.D(10));
                search.SetBounds(x, y, sw, h);
                bFind.SetBounds(x + sw + UI.D(10), y, fw, h);
                hint.SetBounds(x + sw + UI.D(10) + fw + UI.D(10), y + UI.D(4), UI.D(70), h - UI.D(8));
            };

            // left column: textures (fill) + models (bottom)
            var listCol = new Box { Dock = DockStyle.Left, Width = UI.D(300), LineRight = true };
            var modelSec = new Box { Dock = DockStyle.Bottom, Height = UI.D(230), LineTop = true };
            var modelHead = SectionHeader("Models", out modelCount);
            StyleList(modelList);
            modelList.Dock = DockStyle.Fill;
            modelSec.Controls.Add(modelList);
            modelSec.Controls.Add(modelHead);
            modelList.BringToFront();

            var texSec = new Box { Dock = DockStyle.Fill };
            var texHead = SectionHeader("Textures", out texCount);
            StyleList(texList);
            texList.Dock = DockStyle.Fill;
            texSec.Controls.Add(texList);
            texSec.Controls.Add(texHead);
            texList.BringToFront();
            listCol.Controls.Add(texSec);
            listCol.Controls.Add(modelSec);
            texSec.BringToFront();
            listCol.Padding = new Padding(0, 0, UI.D(1), 0);

            // preview
            var prevBox = new Box { Dock = DockStyle.Fill };
            var prevHead = new Box { Dock = DockStyle.Top, Height = UI.D(52), LineBottom = true };
            var prevCap = new TLabel("TEXTURE PREVIEW", UI.Font(7.5f, FontStyle.Bold), t => t.Muted);
            prevName = new TLabel("Right-click for more options", UI.Font(8.5f), t => t.Muted);
            bOrig = new FlatBtn("Original size", BtnKind.Ghost, "") { Height = UI.D(32), Font = UI.Font(8.5f, FontStyle.Bold) };
            bFit = new FlatBtn("Fit to window", BtnKind.Ghost, "") { Height = UI.D(32), Font = UI.Font(8.5f, FontStyle.Bold) };
            bOrig.Click += (s, e) => Click(miOrig);
            bFit.Click += (s, e) => Click(miFit);
            AddAll(prevHead, prevCap, prevName, bOrig, bFit);
            prevHead.Layout += (s, e) =>
            {
                int bx = prevHead.Width - UI.D(12);
                foreach (var b in new[] { bFit, bOrig })
                {
                    int w = b.PreferredWidth(); bx -= w;
                    b.SetBounds(bx, (prevHead.Height - b.Height) / 2, w, b.Height);
                    bx -= UI.D(4);
                }
                int tw = Math.Max(UI.D(60), bx - UI.D(28));
                prevCap.SetBounds(UI.D(18), UI.D(9), tw, prevCap.Height);
                prevName.SetBounds(UI.D(18), UI.D(26), tw, prevName.Height);
            };
            preview.Dock = DockStyle.Fill;
            preview.BorderStyle = BorderStyle.None;
            preview.Paint += PaintEmptyPreview;
            preview.Resize += (s, e) => preview.Invalidate();
            pic.BackgroundImageLayout = ImageLayout.Tile;
            prevBox.Controls.Add(preview);
            prevBox.Controls.Add(prevHead);
            preview.BringToFront();

            browser.Controls.Add(prevBox);
            browser.Controls.Add(listCol);
            browser.Controls.Add(toolbar);
            prevBox.BringToFront();

            content.Controls.Add(browser);
            content.Controls.Add(gap);
            content.Controls.Add(inspector);
            browser.BringToFront();

            tabStrip = new TabStrip { Dock = DockStyle.Top };
            tabStrip.OnSelect = SwitchToTab;
            tabStrip.OnClose = CloseTab;
            tabStrip.OnNew = () => OpenNewTab();

            main.Controls.Add(content);
            main.Controls.Add(bar);
            main.Controls.Add(header);
            main.Controls.Add(tabStrip);
            tabStrip.SendToBack();
            content.BringToFront();

            texList.SelectedIndexChanged += (s, e) => { infoFromModel = false; Sync(); };
            modelList.SelectedIndexChanged += (s, e) => { infoFromModel = true; Sync(); };
        }

        Box BuildInspector()
        {
            var insp = new Box { Dock = DockStyle.Right, Width = UI.D(286), Radius = UI.D(10), FillFn = t => t.Panel, BorderFn = t => t.Line,
                Stack = true, Gap = UI.D(6), Padding = new Padding(UI.D(18), UI.D(16), UI.D(18), UI.D(16)) };
            var head = new TLabel("RESOURCE INSPECTOR", UI.Font(7.5f, FontStyle.Bold), t => t.Muted);

            inspTexName = new TLabel("No texture selected", UI.Head(13f)) { Wrap = true };
            inspTexInfo = new TLabel("Pick a texture from the list to see its size and format.", UI.Mono(9f), t => t.Muted) { Wrap = true };

            // View options: the original "Use alpha channel" checkbox and MIPMAP level spinner
            StyleCheck(alpha);
            var mipRow = new Box { Height = UI.D(30) };
            mipLabel.AutoSize = false; mipLabel.TextAlign = ContentAlignment.MiddleLeft; mipLabel.Font = UI.Font(9f); mipLabel.BackColor = Color.Transparent;
            mip.Font = UI.Font(9f); mip.BorderStyle = BorderStyle.FixedSingle; mip.TextAlign = HorizontalAlignment.Center;
            mipRow.Controls.Add(mipLabel); mipRow.Controls.Add(mip);
            mipRow.Layout += (s, e) =>
            {
                int w = UI.D(64);
                mip.SetBounds(mipRow.Width - w, (mipRow.Height - mip.Height) / 2, w, mip.Height);
                mipLabel.SetBounds(0, 0, mipRow.Width - w - UI.D(8), mipRow.Height);
            };

            inspModelName = new TLabel("No model selected", UI.Font(9.5f, FontStyle.Bold)) { Wrap = true };
            StyleCheck(parser2);
            var parserHint = new TLabel(Translator.T("部分模型可能需要选中此项才能正常解析"), UI.Font(8f), t => t.Muted) { Wrap = true };
            bModelExport = new FlatBtn("Export model (.N2KM)", BtnKind.Secondary, "");
            bModelReplace = new FlatBtn("Replace model", BtnKind.Secondary, "");
            bModelExport.Click += (s, e) => Click(miModelExport);
            bModelReplace.Click += (s, e) => Click(miModelReplace);

            AddAll(insp, head, Spacer(4), inspTexName, inspTexInfo, Divider(),
                Caption("View options"), alpha, mipRow, Divider(),
                Caption("3D model"), inspModelName, parser2, parserHint, Spacer(2), bModelExport, bModelReplace);
            return insp;
        }

        static Control Divider()
        {
            var b = new Box { Height = UI.D(17) };
            b.Decorate = (g, bx) => { using (var p = new Pen(Theme.Cur.Line)) g.DrawLine(p, 0, bx.Height / 2, bx.Width, bx.Height / 2); };
            return b;
        }

        static Box SectionHeader(string text, out TLabel count)
        {
            var h = new Box { Dock = DockStyle.Top, Height = UI.D(40), LineBottom = true };
            var cap = new TLabel(text.ToUpperInvariant(), UI.Font(7.5f, FontStyle.Bold), t => t.Muted);
            var cnt = new TLabel("0", UI.Font(8f, FontStyle.Bold), t => t.Accent) { TextAlign = ContentAlignment.MiddleRight };
            h.Controls.Add(cap); h.Controls.Add(cnt);
            h.Layout += (s, e) =>
            {
                cap.SetBounds(UI.D(18), (h.Height - cap.Height) / 2, h.Width - UI.D(100), cap.Height);
                cnt.SetBounds(h.Width - UI.D(90), (h.Height - cnt.Height) / 2, UI.D(74), cnt.Height);
            };
            count = cnt;
            return h;
        }

        static void StyleCheck(CheckBox c)
        {
            c.AutoSize = false; c.Height = UI.D(28); c.Font = UI.Font(9f);
            c.FlatStyle = FlatStyle.Flat; c.BackColor = Color.Transparent;
            c.Text = Translator.T(c.Text);
        }

        void StyleList(ListBox lb)
        {
            lb.DrawMode = DrawMode.OwnerDrawFixed;
            lb.ItemHeight = UI.D(28);
            lb.BorderStyle = BorderStyle.None;
            lb.IntegralHeight = false;
            lb.Font = UI.Mono(9.5f);
            lb.DrawItem += (s, e) =>
            {
                if (e.Index < 0 || e.Index >= lb.Items.Count) return;
                var t = Theme.Cur;
                bool sel = (e.State & DrawItemState.Selected) != 0;
                var g = e.Graphics;
                using (var b = new SolidBrush(sel ? t.Selected : t.Panel)) g.FillRectangle(b, e.Bounds);
                if (sel) using (var b = new SolidBrush(t.Accent)) g.FillRectangle(b, e.Bounds.X, e.Bounds.Y, UI.D(3), e.Bounds.Height);
                using (var p = new Pen(UI.Mix(t.Panel, t.Line, 0.45f))) g.DrawLine(p, e.Bounds.X, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                string num = (e.Index + 1).ToString();
                int nw = UI.D(44);
                using (var nf = UI.Font(7.5f, FontStyle.Bold))
                    TextRenderer.DrawText(g, num, nf, new Rectangle(e.Bounds.Right - nw - UI.D(8), e.Bounds.Y, nw, e.Bounds.Height),
                        sel ? t.Accent2 : UI.Mix(t.Muted, t.Panel, 0.3f), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, Convert.ToString(lb.Items[e.Index]), lb.Font,
                    new Rectangle(e.Bounds.X + UI.D(18), e.Bounds.Y, e.Bounds.Width - UI.D(18) - nw - UI.D(12), e.Bounds.Height),
                    sel ? t.Text : UI.Mix(t.Text, t.Muted, 0.35f),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            lb.Resize += (s, e) => lb.Invalidate();
        }

        static void SendCue(TextBox tb, string text)
        {
            tb.HandleCreated += (s, e) => SendMessage(tb.Handle, 0x1501, (IntPtr)1, text);
            if (tb.IsHandleCreated) SendMessage(tb.Handle, 0x1501, (IntPtr)1, text);
        }

        void PaintEmptyPreview(object sender, PaintEventArgs e)
        {
            if (pic.Image != null && pic.Visible) return;
            var t = Theme.Cur; var g = e.Graphics;
            int cx = preview.ClientSize.Width / 2, cy = preview.ClientSize.Height / 2 - UI.D(30);
            int r = UI.D(26);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(t.Selected)) g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
            using (var p = new Pen(t.StrongLine)) g.DrawEllipse(p, cx - r, cy - r, r * 2, r * 2);
            g.SmoothingMode = SmoothingMode.None;
            if (UI.HasIconFont)
                using (var gf = UI.Icon(15f))
                    TextRenderer.DrawText(g, "", gf, new Rectangle(cx - r, cy - r, r * 2, r * 2), t.Accent,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            bool loaded = texList.Items.Count > 0;
            using (var hf = UI.Font(12f, FontStyle.Bold))
                TextRenderer.DrawText(g, loaded ? "Select a texture" : "No IFF file open", hf,
                    new Rectangle(0, cy + r + UI.D(14), preview.ClientSize.Width, UI.D(28)), t.Text, TextFormatFlags.HorizontalCenter);
            using (var sf = UI.Font(9f))
                TextRenderer.DrawText(g, loaded ? "Click a texture in the list to preview it here." : "Click Open file, or drag an .iff file onto this window.", sf,
                    new Rectangle(0, cy + r + UI.D(44), preview.ClientSize.Width, UI.D(22)), t.Muted, TextFormatFlags.HorizontalCenter);
        }

        // ---------- state sync ----------

        string lastFile = "\0"; int lastTex = -1, lastModel = -1, lastTexSel = -2, lastModelSel = -2; string lastInfo = null; bool lastPic = true;
        bool infoFromModel; string texInfo = "", modelInfo = "", lastModelInfo = null;

        void Sync()
        {
            if (f.IsDisposed) return;
            string file = curFileField != null ? curFileField.GetValue(f) as string : null;
            bool hasFile = !string.IsNullOrEmpty(file);
            SyncTabs(file, hasFile);
            if (file != lastFile)
            {
                lastFile = file;
                fileStatus.Ok = hasFile;
                fileStatus.Text = hasFile ? "Loaded and ready" : "No file loaded";
                pathSide.Text = hasFile ? file : "No file open";
                headPath.Text = hasFile ? file : "No IFF file open. Drag an .iff file onto this window or click Open file.";
                bSave.Enabled = bSaveAs.Enabled = hasFile;
                fileStatus.Invalidate();
            }
            int tc = texList.Items.Count, mc = modelList.Items.Count;
            if (tc != lastTex) { lastTex = tc; kvTextures.Value = tc.ToString(); texCount.Text = tc.ToString(); preview.Invalidate(); }
            if (mc != lastModel) { lastModel = mc; kvModels.Value = mc.ToString(); modelCount.Text = mc.ToString(); }

            int ts = texList.SelectedIndex, ms = modelList.SelectedIndex;
            // The tool writes texture info and model info into the same label; route it by the list last clicked.
            string raw = Translator.T((info.Text ?? "").Trim());
            if (infoFromModel) modelInfo = raw; else texInfo = raw;
            if (ms != lastModelSel || modelInfo != lastModelInfo)
            {
                lastModelSel = ms; lastModelInfo = modelInfo;
                inspModelName.Text = ms >= 0 ? "Model " + Convert.ToString(modelList.Items[ms]) +
                    (modelInfo.Length > 0 ? "   ·   " + modelInfo : "") : "No model selected";
                bModelExport.Enabled = bModelReplace.Enabled = ms >= 0;
            }
            string inf = texInfo;
            if (ts != lastTexSel || inf != lastInfo)
            {
                lastTexSel = ts; lastInfo = inf;
                string name = ts >= 0 ? Convert.ToString(texList.Items[ts]) : null;
                kvSelected.Value = name ?? "—";
                inspTexName.Text = name ?? "No texture selected";
                inspTexInfo.Text = name == null ? "Pick a texture from the list to see its size and format."
                    : (inf.Length > 0 ? inf : "—") + "\r\nTexture " + (ts + 1) + " of " + tc;
                actionsTitle.Text = name == null ? "No texture selected" : "Texture " + name + (inf.Length > 0 ? "   ·   " + inf : "");
                prevName.Text = name == null ? "Right-click for more options" : name + (inf.Length > 0 ? "  ·  " + inf : "");
                foreach (var b in new[] { bExport, bReplace, bSetTex }) b.Enabled = ts >= 0;
            }
            bClearTex.Enabled = hasFile;
            bOrig.Enabled = bFit.Enabled = pic.Image != null;
            bool hasPic = pic.Image != null;
            if (hasPic != lastPic) { lastPic = hasPic; pic.Visible = hasPic; preview.Invalidate(); }

            // Style any extra windows the tool opens (the 3D model preview).
            foreach (Form other in Application.OpenForms)
            {
                if (other == f || other == help || styledForms.Contains(other)) continue;
                styledForms.Add(other);
                other.FormClosed += (s, e) => styledForms.Remove((Form)s);
                StyleExtraForm(other);
            }
        }

        void StyleExtraForm(Form o)
        {
            HookControls(o);
            if (o.Text == "RenderForm") o.Text = "3D Model Preview";
            ThemeExtraForm(o);
            o.HandleCreated += (s, e) => Native.ThemeWindow(o, Theme.Cur.Dark);
        }

        void ThemeExtraForm(Form o)
        {
            var t = Theme.Cur;
            // Only the title bar follows the theme; the 3D window's own button keeps its native look so it stays readable.
            Native.ThemeWindow(o, t.Dark);
        }

        // ---------- actions ----------

        void FindNext()
        {
            string q = search.Input.Text.Trim();
            if (q.Length == 0) { search.Input.Focus(); return; }
            if (FindIn(texList, q) || FindIn(modelList, q)) return;
            System.Media.SystemSounds.Beep.Play();
        }

        static bool FindIn(ListBox lb, string q)
        {
            int n = lb.Items.Count;
            if (n == 0) return false;
            int start = lb.SelectedIndex;
            for (int k = 1; k <= n; k++)
            {
                int i = (start + k + n) % n;
                if (Convert.ToString(lb.Items[i]).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (lb.SelectedIndex == i) return true;
                    lb.SelectedIndex = i; lb.Focus();
                    return true;
                }
            }
            return false;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.O) { OpenNewTab(); e.Handled = true; }
            else if (e.Control && e.Shift && e.KeyCode == Keys.S) { Click(miSaveAs); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.S) { Click(miSave); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.E) { Click(miExport); e.Handled = true; }
            else if (e.Control && e.KeyCode == Keys.F) { search.Input.Focus(); search.Input.SelectAll(); e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.F1) { ShowHelp(); e.Handled = true; }
        }

        public void TestSetTheme(bool dark) { SetTheme(dark); }
        public void TestHelp() { ShowHelp(); }
        public void TestAbout() { Click(miAbout); }


        void ShowHelp()
        {
            if (help == null || help.IsDisposed) help = new HelpForm();
            help.ApplyTheme();
            if (!help.Visible) help.Show(f); else help.Activate();
        }

        // ---------- theme ----------

        void SetTheme(bool dark)
        {
            Theme.Cur = dark ? Theme.DarkTheme : Theme.LightTheme;
            SaveSettings();
            ApplyTheme();
        }

        void ApplyTheme()
        {
            var t = Theme.Cur;
            f.BackColor = t.Bg;
            sidebar.BackColor = t.Sidebar;
            main.BackColor = t.Bg;
            ThemeTree(f, t);

            preview.BackColor = t.Field;
            pic.BackColor = t.Field;
            if (pic.BackgroundImage != null) pic.BackgroundImage.Dispose();
            pic.BackgroundImage = Checker(t);
            foreach (var lb in new[] { texList, modelList }) { lb.BackColor = t.Panel; lb.ForeColor = t.Text; lb.Invalidate(); }
            foreach (var c in new[] { alpha, parser2 })
            {
                c.ForeColor = t.Text;
                c.FlatAppearance.BorderColor = t.StrongLine;
                c.FlatAppearance.CheckedBackColor = t.Selected;
                c.FlatAppearance.MouseOverBackColor = t.Hover;
            }
            mipLabel.ForeColor = t.Text;
            mip.BackColor = t.Field; mip.ForeColor = t.Text;
            foreach (var l in new[] { lnkGame, lnkForum, lnkBoard, lnkBabu }) if (l != null) l.ForeColor = t.Muted;
            search.ApplyThemeColors(t);
            bLight.Kind = t.Dark ? BtnKind.Toggle : BtnKind.ToggleOn;
            bDark.Kind = t.Dark ? BtnKind.ToggleOn : BtnKind.Toggle;

            var renderer = new ToolStripProfessionalRenderer(new MenuColors()) { RoundedEdges = false };
            foreach (var m in menus)
            {
                m.Renderer = renderer;
                m.BackColor = t.Panel;
                m.Font = UI.Font(9f);
                foreach (ToolStripItem it in m.Items) it.ForeColor = t.Text;
            }

            Native.ThemeWindow(f, t.Dark);
            foreach (Control c in new Control[] { texList, modelList, preview }) Native.ThemeWindow(c, t.Dark);
            foreach (var o in styledForms) if (!o.IsDisposed) ThemeExtraForm(o);
            if (help != null && !help.IsDisposed) help.ApplyTheme();
            f.Invalidate(true);
        }

        static void ThemeTree(Control c, Theme t)
        {
            var b = c as Box; if (b != null) b.ApplyTheme(t);
            var l = c as TLabel; if (l != null) l.ApplyTheme(t);
            if (c is FlatBtn || c is KvRow || c is StatusRow || c is Badge || c is Brand) c.Invalidate();
            foreach (Control k in c.Controls) ThemeTree(k, t);
        }

        static Image Checker(Theme t)
        {
            int s = UI.D(10);
            var bmp = new Bitmap(s * 2, s * 2);
            using (var g = Graphics.FromImage(bmp))
            {
                Color a = t.Dark ? UI.Mix(t.Field, Color.White, 0.06f) : UI.Mix(t.Field, Color.Black, 0.06f);
                g.Clear(t.Field);
                using (var br = new SolidBrush(a)) { g.FillRectangle(br, 0, 0, s, s); g.FillRectangle(br, s, s, s, s); }
            }
            return bmp;
        }

        // ---------- settings ----------

        static string SettingsPath { get { return Path.Combine(Program.DataDir ?? Path.GetTempPath(), "settings.ini"); } }

        static void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath) && File.ReadAllText(SettingsPath).Contains("theme=light")) Theme.Cur = Theme.LightTheme;
            }
            catch { }
        }

        static void SaveSettings()
        {
            try { File.WriteAllText(SettingsPath, "theme=" + (Theme.Cur.Dark ? "dark" : "light") + "\r\n"); } catch { }
        }
    }

    class MenuColors : ProfessionalColorTable
    {
        static Theme T { get { return Theme.Cur; } }
        public override Color ToolStripDropDownBackground { get { return T.Panel; } }
        public override Color ImageMarginGradientBegin { get { return T.Panel; } }
        public override Color ImageMarginGradientMiddle { get { return T.Panel; } }
        public override Color ImageMarginGradientEnd { get { return T.Panel; } }
        public override Color MenuBorder { get { return T.Line; } }
        public override Color MenuItemBorder { get { return T.StrongLine; } }
        public override Color MenuItemSelected { get { return T.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return T.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return T.Hover; } }
        public override Color MenuItemPressedGradientBegin { get { return T.Selected; } }
        public override Color MenuItemPressedGradientEnd { get { return T.Selected; } }
        public override Color SeparatorDark { get { return T.Line; } }
        public override Color SeparatorLight { get { return T.Panel; } }
    }
}
