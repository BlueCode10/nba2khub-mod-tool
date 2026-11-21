using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace ModToolEN
{
    static class Program
    {
        public const string Version = "0.4.83";
        public static Assembly ToolAsm;
        public static string DataDir;

        [STAThread]
        static int Main(string[] args)
        {
            try { Native.SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string libDir;
            try { libDir = Payload.Extract(); }
            catch (Exception ex)
            {
                MessageBox.Show("Could not unpack the tool files:\r\n" + ex.Message, "3DM NBA2K12 MOD Tool",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            // zlib.dll is loaded by name from the unpacked folder.
            Environment.SetEnvironmentVariable("PATH", libDir + ";" + Environment.GetEnvironmentVariable("PATH"));
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                string n = new AssemblyName(e.Name).Name;
                foreach (string ext in new[] { ".dll", ".exe" })
                {
                    string p = Path.Combine(libDir, n + ext);
                    if (File.Exists(p)) return Assembly.LoadFrom(p);
                }
                return null;
            };

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ShowError(e.Exception);

            Form form;
            try
            {
                ToolAsm = Assembly.LoadFrom(Path.Combine(libDir, Payload.CoreName));
                EnglishResources.Install(ToolAsm);
                MsgBoxHook.Install();
                var formType = ToolAsm.GetType("NBA2K12TEXTUREEDITOR.Form1", true);
                form = (Form)Activator.CreateInstance(formType, true);
            }
            catch (Exception ex)
            {
                ShowError(ex);
                return 2;
            }

            var shell = new Shell(form);
            shell.Build();
            if (args.Length > 0 && File.Exists(args[0]))
            {
                string file = args[0];
                form.Shown += (s, e) => shell.OpenFile(file);
            }
            SelfTest.Attach(form, shell);
            Application.Run(form);
            return 0;
        }

        public static void ShowError(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            MessageBox.Show(Translator.T(ex.Message) + "\r\n\r\n" + ex.GetType().Name, "3DM NBA2K12 MOD Tool",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// Unpacks the original tool and its DLLs (embedded in this exe) to a per-user folder.
    static class Payload
    {
        public const string CoreName = "NBA2K12ModCore.exe";
        static readonly string[] Files = { CoreName, "DXRender_4.dll", "zlib.dll" };

        public static string Extract()
        {
            var asm = Assembly.GetExecutingAssembly();
            Program.DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "3DM NBA2K12 MOD Tool");
            string dir = Path.Combine(Program.DataDir, Program.Version);
            Directory.CreateDirectory(dir);
            foreach (string name in Files)
            {
                using (var s = asm.GetManifestResourceStream("payload." + name))
                {
                    if (s == null) throw new FileNotFoundException("Missing embedded file " + name);
                    string path = Path.Combine(dir, name);
                    if (File.Exists(path) && new FileInfo(path).Length == s.Length) continue;
                    using (var o = File.Create(path)) s.CopyTo(o);
                }
            }
            return dir;
        }
    }
}
