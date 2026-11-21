using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ModToolEN
{
    /// Screenshot-driven smoke test. Only runs when MODTOOL_SELFTEST=<output folder> is set.
    /// Captures only this program's own windows (works even if they are behind other apps) and
    /// never sends keystrokes; dialogs are closed by messaging them directly.
    static class SelfTest
    {
        delegate bool EnumThreadProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint tid, EnumThreadProc p, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int m, IntPtr w, IntPtr l);

        static List<IntPtr> Dialogs()
        {
            var list = new List<IntPtr>();
            EnumThreadWindows(Native.GetCurrentThreadId(), (h, l) =>
            {
                if (IsWindowVisible(h) && Native.GetClass(h) == "#32770") list.Add(h);
                return true;
            }, IntPtr.Zero);
            return list;
        }

        static void Capture(IntPtr h, string path)
        {
            Native.RECT r; Native.GetWindowRect(h, out r);
            int w = r.Right - r.Left, hh = r.Bottom - r.Top;
            if (w <= 0 || hh <= 0) return;
            using (var bmp = new Bitmap(w, hh))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr dc = g.GetHdc();
                    PrintWindow(h, dc, 2);
                    g.ReleaseHdc(dc);
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }

        // Headless check of smart Replace: open file, replace texture at MODTOOL_REPLACE_IDX with MODTOOL_REPLACE_IMG.
        static void AttachReplaceTest(Form f, Shell shell, string dir)
        {
            Directory.CreateDirectory(dir);
            string log = Path.Combine(dir, "replace-log.txt");
            string src = Environment.GetEnvironmentVariable("MODTOOL_OPEN");
            int idx = int.Parse(Environment.GetEnvironmentVariable("MODTOOL_REPLACE_IDX") ?? "1");
            var steps = new Queue<Action>();
            Action<string> w = s => File.AppendAllText(log, s + "\r\n");
            int n = 0;
            Action snap = () => { n++; try { Capture(f.Handle, Path.Combine(dir, n.ToString("00") + ".png")); } catch { } };

            steps.Enqueue(() => { File.WriteAllText(log, ""); shell.OpenFile(src); });
            steps.Enqueue(() => { w("before curfile=" + shell.TestCurFile2()); });
            bool viaMenu = Environment.GetEnvironmentVariable("MODTOOL_REPLACE_VIAMENU") == "1";
            steps.Enqueue(() => f.BeginInvoke((Action)(() => { if (viaMenu) shell.TestMenuReplace(idx); else shell.TestSmartReplace(idx); })));
            steps.Enqueue(snap);
            steps.Enqueue(() => { foreach (var d in Dialogs()) PostMessage(d, 0x111, (IntPtr)1, IntPtr.Zero); });
            steps.Enqueue(() => { w("after curfile=" + shell.TestCurFile2()); });
            steps.Enqueue(snap);
            steps.Enqueue(() => { w("DONE"); Application.Exit(); });

            var timer = new Timer { Interval = 1500 };
            timer.Tick += (s, e) => { if (steps.Count == 0) { timer.Stop(); return; } var a = steps.Dequeue(); try { a(); } catch (Exception ex) { w("ERR " + ex); } };
            f.Shown += (s, e) => timer.Start();
            var closer = new Timer { Interval = 500 };
            closer.Tick += (s, e) => { foreach (var d in Dialogs()) PostMessage(d, 0x111, (IntPtr)1, IntPtr.Zero); };
            f.Shown += (s, e) => closer.Start();
        }

        static void AttachTabTest(Form f, Shell shell, string dir)
        {
            Directory.CreateDirectory(dir);
            string log = Path.Combine(dir, "tab-log.txt");
            string src = Environment.GetEnvironmentVariable("MODTOOL_OPEN");
            string src2 = Environment.GetEnvironmentVariable("MODTOOL_OPEN2");
            var steps = new Queue<Action>();
            Action<string> w = s => File.AppendAllText(log, s + "\r\n");
            int n = 0; Action snap = () => { n++; try { Capture(f.Handle, Path.Combine(dir, n.ToString("00") + ".png")); } catch { } };

            steps.Enqueue(() => { File.WriteAllText(log, ""); shell.OpenFile(src); });
            steps.Enqueue(() => { w("after open1: tabs=" + shell.TestTabCount() + " active=" + shell.TestActiveTab()); });
            steps.Enqueue(snap);
            steps.Enqueue(() => shell.TestAddTab(src2));
            steps.Enqueue(() => { w("after addtab2: tabs=" + shell.TestTabCount() + " active=" + shell.TestActiveTab() + " curfile=" + shell.TestCurFile2()); });
            steps.Enqueue(snap);
            steps.Enqueue(() => shell.TestSwitchTab(0));
            steps.Enqueue(() => { w("after switch to 0: active=" + shell.TestActiveTab() + " curfile=" + shell.TestCurFile2()); });
            steps.Enqueue(snap);
            steps.Enqueue(() => shell.TestCloseTab(1));
            steps.Enqueue(() => { w("after close tab1: tabs=" + shell.TestTabCount() + " active=" + shell.TestActiveTab()); });
            steps.Enqueue(snap);
            steps.Enqueue(() => { w("DONE"); Application.Exit(); });

            var timer = new Timer { Interval = 1500 };
            timer.Tick += (s, e) => { if (steps.Count == 0) { timer.Stop(); return; } var a = steps.Dequeue(); try { a(); } catch (Exception ex) { w("ERR " + ex); } };
            f.Shown += (s, e) => timer.Start();
        }

        static void AttachAboutTest(Form f, Shell shell, string dir)
        {
            Directory.CreateDirectory(dir);
            var steps = new Queue<Action>();
            int n = 0; Action snapAll = () =>
            {
                n++;
                foreach (Form o in Application.OpenForms) { try { Capture(o.Handle, Path.Combine(dir, n.ToString("00") + "-" + o.Handle.ToInt64() + ".png")); } catch { } }
            };
            steps.Enqueue(() => f.BeginInvoke((Action)(() => shell.TestAbout())));
            steps.Enqueue(snapAll);
            steps.Enqueue(() => { foreach (Form o in Application.OpenForms) if (o != f) { try { o.BeginInvoke((Action)o.Close); } catch { } } });
            steps.Enqueue(() => Application.Exit());
            var timer = new Timer { Interval = 1200 };
            timer.Tick += (s, e) => { if (steps.Count == 0) { timer.Stop(); return; } var a = steps.Dequeue(); try { a(); } catch { } };
            f.Shown += (s, e) => timer.Start();
        }

        public static void Attach(Form f, Shell shell)
        {
            string aboutDir = Environment.GetEnvironmentVariable("MODTOOL_ABOUTTEST");
            if (!string.IsNullOrEmpty(aboutDir)) { AttachAboutTest(f, shell, aboutDir); return; }

            string tabDir = Environment.GetEnvironmentVariable("MODTOOL_TABTEST");
            if (!string.IsNullOrEmpty(tabDir)) { AttachTabTest(f, shell, tabDir); return; }

            string repDir = Environment.GetEnvironmentVariable("MODTOOL_REPLACETEST");
            if (!string.IsNullOrEmpty(repDir)) { AttachReplaceTest(f, shell, repDir); return; }

            string outDir = Environment.GetEnvironmentVariable("MODTOOL_SELFTEST");
            if (string.IsNullOrEmpty(outDir)) return;
            Directory.CreateDirectory(outDir);
            string open = Environment.GetEnvironmentVariable("MODTOOL_OPEN");
            var steps = new Queue<Action>();
            int n = 0;
            Action snap = () =>
            {
                n++;
                string tag = n.ToString("00");
                Capture(f.Handle, Path.Combine(outDir, tag + "-main.png"));
                int k = 0;
                foreach (Form o in Application.OpenForms)
                    if (o != f && o.Visible) Capture(o.Handle, Path.Combine(outDir, tag + "-form" + (++k) + ".png"));
                k = 0;
                foreach (var d in Dialogs())
                {
                    Capture(d, Path.Combine(outDir, tag + "-dialog" + (++k) + ".png"));
                    File.AppendAllText(Path.Combine(outDir, "log.txt"), tag + " dialog: " + Native.GetText(d) + "\r\n");
                }
                File.AppendAllText(Path.Combine(outDir, "log.txt"), tag + ": title=" + f.Text + " forms=" + Application.OpenForms.Count + "\r\n");
            };
            Action closeDialogs = () =>
            {
                foreach (var d in Dialogs())
                {
                    PostMessage(d, 0x111, (IntPtr)7, IntPtr.Zero);  // IDNO
                    PostMessage(d, 0x111, (IntPtr)1, IntPtr.Zero);  // IDOK
                }
            };
            var lists = new List<ListBox>();
            Action<Control> find = null;
            find = c => { var lb = c as ListBox; if (lb != null) lists.Add(lb); foreach (Control k in c.Controls) find(k); };

            steps.Enqueue(snap);
            if (!string.IsNullOrEmpty(open))
            {
                steps.Enqueue(() => shell.OpenFile(open));
                steps.Enqueue(() => { lists.Clear(); find(f); foreach (var lb in lists) if (lb.Items.Count > 0 && lb.Name == "listBox1") lb.SelectedIndex = 0; });
                steps.Enqueue(snap);
                steps.Enqueue(() => { foreach (var lb in lists) if (lb.Items.Count > 0 && lb.Name == "listBox2") lb.SelectedIndex = 0; });
                steps.Enqueue(snap);
            }
            steps.Enqueue(() => f.BeginInvoke((Action)shell.TestAbout));
            steps.Enqueue(snap);
            steps.Enqueue(closeDialogs);
            steps.Enqueue(() => f.BeginInvoke((Action)(() =>
                MessageBox.Show(f, "贴图会被程序转换，是否继续？", "保存提醒", MessageBoxButtons.YesNo, MessageBoxIcon.Warning))));
            steps.Enqueue(snap);
            steps.Enqueue(closeDialogs);
            steps.Enqueue(() => shell.TestSetTheme(false));
            steps.Enqueue(snap);
            steps.Enqueue(() => shell.TestSetTheme(true));
            steps.Enqueue(() => { closeDialogs(); Application.Exit(); });

            var timer = new Timer { Interval = 1500 };
            timer.Tick += (s, e) =>
            {
                if (steps.Count == 0) { timer.Stop(); return; }
                var a = steps.Dequeue();
                try { a(); }
                catch (Exception ex) { File.AppendAllText(Path.Combine(outDir, "log.txt"), "ERR " + ex + "\r\n"); }
            };
            f.Shown += (s, e) => timer.Start();
        }
    }
}
