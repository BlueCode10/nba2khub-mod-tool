using System;
using System.Drawing;
using System.Windows.Forms;

namespace ModToolEN
{
    /// English translation of the original "工具说明.txt" readme.
    class HelpForm : Form
    {
        readonly RichTextBox box = new RichTextBox();
        readonly Box head = new Box();

        const string Readme =
@"FEATURES
  • Edit textures in all kinds of player face files
  • Edit 3D models in player face files
  • Edit jersey textures
  • Edit billboards / court-side ads
  • Edit court (arena) files
  • Edit photos (portraits)
  • Edit shoe files
  • Edit basketball files
  • Edit the ""globle"" files (protective gear, accessories and other shared files)

REQUIREMENTS
  • Visual C++ 2008 Redistributable SP1 and Visual C++ 2010 Redistributable (x86)
  • .NET Framework 4.0 or later
  • DirectX 9 (June 2010) or later, which includes D3DX9_43.DLL

  This English edition is a single .exe. It bundles the .NET 4 build of the tool together with
  DXRender_4.dll and zlib.dll, and unpacks them to %LOCALAPPDATA%\3DM NBA2K12 MOD Tool on first run.

HOW TO USE

1. Opening a file
   Click Open file (or Open IFF file in the sidebar), or drag the .iff file you want to edit onto
   the window. The Textures list and the Models list on the left then fill with the file's contents.
   Note: opening combined files such as portrait_hi.iff + portrait_hi.cdf can use a lot of memory
   and take 3-4 seconds.

2. Exporting and importing
   Click a texture in the Textures list to show it in the preview. Right-click the preview (or use
   the buttons in the header and the bottom bar) for these options:

   Original size: show the texture at its real resolution.

   Fit to window: scale the preview to fit the window, so you can see the whole texture (small
   textures get bigger, large ones smaller). This only changes the preview, not the texture.

   Export: save the selected texture. Formats: DDS, PNG or BMP. DDS is recommended.

   Replace: import an edited texture in place of the selected one. The new texture must have the
   same dimensions and format as the original. If the format differs (for example DXT1 vs DXT5 in
   DDS), the tool converts it to the original format. Importing a different size can make the game
   read the texture incorrectly, so edit carefully.

   Set as model texture: apply the selected texture to the model in the 3D preview. This only works
   for models with matching UVs; otherwise the preview looks wrong. It does not affect import or
   export, only the preview. Use Clear model texture to return the 3D preview to no texture.

   Click a model in the Models list to open the 3D preview window. Right-click the Models list (or
   use the buttons in the Resource inspector) for these options:

   Export: save the selected model as an .N2KM file. N2KM files can be opened in 3ds Max or Blender
   with the matching import plug-in.

   Replace: import an edited .N2KM model. The vertex count must match the original or the game
   will fail to read it. In other words, do not add or delete vertices; moving them to change the
   shape is fine.

3. Saving
   After replacing textures or models, click Save IFF or Save as to write the changes:
   • Save IFF overwrites the .iff file you opened (you will be asked to confirm).
   • Save as writes the modified .iff somewhere else and leaves the original untouched.

KEYBOARD SHORTCUTS
   Ctrl+O  Open file          Ctrl+S  Save IFF          Ctrl+Shift+S  Save as
   Ctrl+E  Export texture     Ctrl+F  Search            F1            This guide

VERSION HISTORY
   0.4.83 (Dec 22): Fixed a MipMap parsing error introduced in 0.4.76.
   0.4.77 (Dec 19): Improved jersey-number texture import; better conversion quality.
   0.4.76 (Dec 15): Added support for jersey-number textures.
   0.4.57 (Nov 29): All textures support 8×8 mipmaps; fixed a UV offset when importing models.
   0.4.48 (Nov 28): Reworked the DDS import module for much better conversion quality (slightly
                    slower); fixed a mipmap parsing error in jersey textures.

Original tool by 3DMGAME (www.3dmgame.com). English interface and single-file edition built on top
of the original program; all editing is still done by the original code.";

        public HelpForm()
        {
            Text = "Readme & guide";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(UI.D(820), UI.D(760));
            MinimumSize = new Size(UI.D(520), UI.D(400));
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            Font = UI.Font(9f);

            head.Dock = DockStyle.Top; head.Height = UI.D(92); head.FillFn = t => t.Bg; head.LineBottom = true;
            var eyebrow = new TLabel("3DM NBA2K12 MOD TOOL  /  HELP", UI.Font(7.5f, FontStyle.Bold), t => t.Accent);
            var title = new TLabel("Readme & guide", UI.Head(18f));
            head.Controls.Add(eyebrow); head.Controls.Add(title);
            head.Layout += (s, e) =>
            {
                eyebrow.SetBounds(UI.D(26), UI.D(20), head.Width - UI.D(40), eyebrow.Height);
                title.SetBounds(UI.D(26), UI.D(36), head.Width - UI.D(40), UI.D(40));
            };

            var wrap = new Box { Dock = DockStyle.Fill, FillFn = t => t.Panel, Padding = new Padding(UI.D(26), UI.D(16), UI.D(10), UI.D(10)) };
            box.Dock = DockStyle.Fill;
            box.ReadOnly = true;
            box.BorderStyle = BorderStyle.None;
            box.Font = UI.Font(10f);
            box.Text = Readme;
            box.DetectUrls = true;
            box.LinkClicked += (s, e) => { try { System.Diagnostics.Process.Start(e.LinkText); } catch { } };
            wrap.Controls.Add(box);

            Controls.Add(wrap);
            Controls.Add(head);
            wrap.BringToFront();

            HandleCreated += (s, e) => ApplyTheme();
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        public void ApplyTheme()
        {
            var t = Theme.Cur;
            BackColor = t.Panel;
            box.BackColor = t.Panel;
            box.ForeColor = t.Text;
            Recurse(this, t);
            Native.ThemeWindow(this, t.Dark);
            Native.ThemeWindow(box, t.Dark);
            BoldHeadings(t);
            Invalidate(true);
        }

        void BoldHeadings(Theme t)
        {
            box.SelectAll();
            box.SelectionColor = t.Text;
            box.SelectionFont = UI.Font(10f);
            int pos = 0;
            foreach (string line in box.Lines)
            {
                string trimmed = line.Trim();
                bool heading = trimmed.Length > 2 && trimmed == trimmed.ToUpperInvariant() && char.IsLetter(trimmed[0]) && !trimmed.Contains(".");
                bool numbered = line.Length > 2 && char.IsDigit(line[0]) && line[1] == '.';
                if (heading || numbered)
                {
                    box.Select(pos, line.Length);
                    box.SelectionFont = heading ? UI.Font(8.5f, FontStyle.Bold) : UI.Font(10.5f, FontStyle.Bold);
                    box.SelectionColor = heading ? t.Accent : t.Text;
                }
                pos += line.Length + 1;
            }
            box.Select(0, 0);
        }

        static void Recurse(Control c, Theme t)
        {
            var b = c as Box; if (b != null) b.ApplyTheme(t);
            var l = c as TLabel; if (l != null) l.ApplyTheme(t);
            foreach (Control k in c.Controls) Recurse(k, t);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }
    }
}
