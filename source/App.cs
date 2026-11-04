using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace EasyDownload
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [STAThread]
        static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "Local\\EasyDownload-427a30d9", out created))
            {
                if (!created)
                {
                    foreach (Process p in Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName))
                    {
                        if (p.Id != Process.GetCurrentProcess().Id && p.MainWindowHandle != IntPtr.Zero)
                        { ShowWindow(p.MainWindowHandle, 9); SetForegroundWindow(p.MainWindowHandle); break; }
                        p.Dispose();
                    }
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) {
                    MessageBox.Show("操作未完成：" + e.Exception.Message, "轻松下载", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                };
                Application.Run(new MainForm());
            }
        }
    }

    sealed class MainForm : Form
    {
        readonly Color ink = Color.FromArgb(29, 48, 43);
        readonly Color green = Color.FromArgb(21, 111, 84);
        readonly Color muted = Color.FromArgb(99, 110, 105);
        readonly TextBox url = new TextBox(), folder = new TextBox(), filename = new TextBox();
        readonly Button paste = new Button(), browse = new Button(), start = new Button(), cancel = new Button(), open = new Button();
        readonly Label status = new Label(), detail = new Label(), speed = new Label(), percent = new Label();
        readonly Meter meter = new Meter();
        readonly ToolTip tips = new ToolTip();
        CancellationTokenSource running;
        bool closeAfterCancel;
        string lastFile;
        readonly string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public MainForm()
        {
            Text = "轻松下载"; BackColor = Color.FromArgb(250, 250, 246); ForeColor = ink;
            Font = new Font("Microsoft YaHei UI", 10F); AutoScaleDimensions = new SizeF(96F, 96F); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(748, 580); MinimumSize = new Size(690, 610); StartPosition = FormStartPosition.CenterScreen;
            try { Icon = new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico")); } catch (IOException) { }

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 14, Padding = new Padding(30, 20, 30, 22) };
            Controls.Add(layout);
            var title = new Label { Text = "轻松下载", Font = new Font(Font.FontFamily, 23F, FontStyle.Bold), AutoSize = true, Margin = new Padding(0, 0, 0, 5), ForeColor = ink };
            Add(layout, title);
            Add(layout, new Label { Text = "粘贴文件链接，剩下的交给它。", AutoSize = true, ForeColor = muted, Margin = new Padding(2, 0, 0, 23) });
            Add(layout, FieldLabel("下载链接"));
            url.AccessibleName = "下载链接"; url.TabIndex = 0;
            Add(layout, FieldRow(url, paste, "粘贴", delegate {
                try { if (Clipboard.ContainsText()) { url.Text = Clipboard.GetText().Trim(); url.Focus(); url.SelectAll(); } }
                catch (ExternalException) { SetStatus("暂时无法读取剪贴板，请按 Ctrl+V 粘贴。", false); }
            }));
            Add(layout, FieldLabel("保存到"));
            folder.AccessibleName = "保存文件夹"; folder.TabIndex = 2;
            Add(layout, FieldRow(folder, browse, "选择…", delegate {
                using (var dialog = new FolderBrowserDialog { Description = "选择下载文件的保存文件夹", ShowNewFolderButton = true, SelectedPath = folder.Text })
                    if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath;
            }));
            Add(layout, FieldLabel("文件名（可选，留空自动识别）"));
            filename.AccessibleName = "文件名，可留空自动识别"; filename.TabIndex = 4; filename.Dock = DockStyle.Top; filename.Margin = new Padding(0, 0, 0, 15);
            Add(layout, filename);
            var progressHeader = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 6, 0, 5) };
            progressHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 80)); progressHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            status.Text = "准备好开始"; status.AutoEllipsis = true; status.Dock = DockStyle.Fill; status.Height = 28; status.AccessibleName = "下载状态";
            percent.Text = "—"; percent.Dock = DockStyle.Fill; percent.TextAlign = ContentAlignment.TopRight;
            progressHeader.Controls.Add(status, 0, 0); progressHeader.Controls.Add(percent, 1, 0); Add(layout, progressHeader);
            meter.Dock = DockStyle.Top; meter.Height = 9; meter.Margin = new Padding(0, 0, 0, 10); Add(layout, meter);
            var detailsRow = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Top, Height = 43, Margin = new Padding(0) };
            detailsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); detailsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            detail.Text = "最多 8 路连接 · 重名文件自动另存"; detail.ForeColor = muted; detail.AutoEllipsis = true; detail.Dock = DockStyle.Fill;
            speed.ForeColor = muted; speed.Dock = DockStyle.Fill; speed.TextAlign = ContentAlignment.TopRight;
            detailsRow.Controls.Add(detail, 0, 0); detailsRow.Controls.Add(speed, 1, 0); Add(layout, detailsRow);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 12) };
            StyleButton(start, "开始下载", 150, true); StyleButton(cancel, "取消", 86, false); StyleButton(open, "打开文件夹", 132, false);
            cancel.Enabled = false; start.TabIndex = 5; cancel.TabIndex = 6; open.TabIndex = 7;
            start.Click += async delegate { await BeginDownloadAsync(); };
            cancel.Click += delegate { if (running != null) { cancel.Enabled = false; SetStatus("正在取消并清理临时文件…", false); running.Cancel(); } };
            open.Click += delegate { OpenFolder(); };
            actions.Controls.Add(start); actions.Controls.Add(cancel); actions.Controls.Add(open); Add(layout, actions);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = new Padding(0, 3, 0, 0) };
            var help = new LinkLabel { Text = "使用说明", LinkColor = green, ActiveLinkColor = ink, AutoSize = true, Margin = new Padding(0, 0, 20, 0), TabIndex = 8 };
            help.LinkClicked += delegate { Process.Start(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "使用说明.html")); };
            footer.Controls.Add(help); footer.Controls.Add(new Label { Text = "支持 HTTP / HTTPS 文件直链", AutoSize = true, ForeColor = muted }); Add(layout, footer);
            layout.RowStyles.Clear();
            for (int i = 0; i < layout.RowCount; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tips.SetToolTip(filename, "例如：安装包.zip。留空时优先使用服务器提供的文件名；不会随意猜测后缀。");
            tips.SetToolTip(url, "GitHub：进入 Releases，右键具体文件，复制链接地址。网页地址不会自动变成文件直链。");
            AcceptButton = start;
            folder.Text = LoadFolder();
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (running != null) { e.Cancel = true; closeAfterCancel = true; running.Cancel(); SetStatus("正在清理，稍后关闭…", false); }
            };
        }

        static void Add(TableLayoutPanel layout, Control control)
        { layout.Controls.Add(control, 0, layout.Controls.Count); }
        Label FieldLabel(string text)
        { return new Label { Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, 6), ForeColor = ink }; }
        Control FieldRow(TextBox input, Button button, string text, EventHandler click)
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 14) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            input.Dock = DockStyle.Fill; input.Margin = new Padding(0, 2, 10, 0);
            StyleButton(button, text, 88, false); button.Height = 34; button.Margin = new Padding(0); button.Click += click;
            row.Controls.Add(input, 0, 0); row.Controls.Add(button, 1, 0); return row;
        }
        void StyleButton(Button button, string text, int width, bool primary)
        {
            button.Text = text; button.Width = width; button.Height = 42; button.FlatStyle = FlatStyle.Flat;
            button.BackColor = primary ? green : BackColor; button.ForeColor = primary ? Color.White : ink;
            button.FlatAppearance.BorderColor = primary ? green : Color.FromArgb(192, 202, 193);
            button.Margin = new Padding(0, 0, 12, 0); button.Cursor = Cursors.Hand;
            button.AccessibleName = text; button.UseVisualStyleBackColor = false;
        }
        void SetStatus(string text, bool error)
        { status.Text = text; status.ForeColor = error ? Color.FromArgb(167, 50, 35) : ink; tips.SetToolTip(status, text); }
        void SetBusy(bool busy)
        { url.Enabled = folder.Enabled = filename.Enabled = paste.Enabled = browse.Enabled = start.Enabled = !busy; cancel.Enabled = busy; }

        async Task BeginDownloadAsync()
        {
            if (running != null) return;
            try
            {
                DownloadCore.ValidateUrl(url.Text);
                if (!String.IsNullOrWhiteSpace(filename.Text)) DownloadCore.ValidateName(filename.Text.Trim());
                if (String.IsNullOrWhiteSpace(folder.Text)) throw new ArgumentException("请选择一个保存文件夹。");
                Path.GetFullPath(folder.Text.Trim());
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "检查一下输入", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            running = new CancellationTokenSource(); SetBusy(true); lastFile = null; meter.Value = 0;
            SetStatus("正在连接，识别文件名…", false); percent.Text = "…"; speed.Text = ""; detail.Text = "正在准备下载引擎";
            try
            {
                var core = new DownloadCore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engine", "aria2c.exe"));
                var progress = new Progress<DownloadProgress>(delegate(DownloadProgress p) {
                    if (running == null || running.IsCancellationRequested) return;
                    SetStatus(p.Name, false);
                    meter.Value = p.Total > 0 ? Math.Min(1.0, (double)p.Completed / p.Total) : 0;
                    percent.Text = p.Total > 0 ? (100 * meter.Value).ToString("0.0", CultureInfo.InvariantCulture) + "%" : "大小未知";
                    detail.Text = FormatBytes(p.Completed) + " / " + (p.Total > 0 ? FormatBytes(p.Total) : "等待服务器提供大小") + "  ·  " + p.Connections + " 路连接";
                    long seconds = p.Speed > 0 && p.Total > p.Completed ? (p.Total - p.Completed) / p.Speed : 0;
                    speed.Text = p.Speed > 0 ? FormatBytes(p.Speed) + "/s" + (seconds > 0 ? " · 剩余 " + FormatTime(seconds) : "") : "正在连接…";
                });
                DownloadResult result = await core.DownloadAsync(url.Text.Trim(), folder.Text.Trim(), filename.Text.Trim(), progress, running.Token);
                lastFile = result.Path; meter.Value = 1; percent.Text = "100%"; speed.Text = "已保存";
                SetStatus("下载完成 · " + Path.GetFileName(lastFile), false);
                detail.Text = String.IsNullOrEmpty(Path.GetExtension(lastFile)) ? "服务器未提供后缀，可重新填写文件名下载。" : FormatBytes(result.Bytes) + " · 点击“打开文件夹”查看";
                SaveFolder();
                System.Media.SystemSounds.Asterisk.Play();
            }
            catch (OperationCanceledException)
            { SetStatus("已取消", false); detail.Text = "本次未完成文件已清理，可重新开始。"; speed.Text = ""; percent.Text = "—"; meter.Value = 0; }
            catch (Exception ex)
            {
                SetStatus("下载未完成", true); detail.Text = "点击“开始下载”可重试。"; speed.Text = "";
                string message = ex is WebException ? "无法连接下载引擎，请关闭程序后重试。" : ex.Message;
                MessageBox.Show(this, message, "下载未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                running.Dispose(); running = null; SetBusy(false);
                if (closeAfterCancel) Close();
            }
        }
        static string FormatBytes(long bytes)
        {
            double value = bytes; string[] units = { "B", "KB", "MB", "GB", "TB" }; int unit = 0;
            while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
            return value.ToString(unit == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[unit];
        }
        static string FormatTime(long seconds)
        { return seconds >= 3600 ? (seconds / 3600) + "小时" + (seconds % 3600 / 60) + "分" : seconds >= 60 ? (seconds / 60) + "分" + (seconds % 60) + "秒" : seconds + "秒"; }
        string LoadFolder()
        {
            try {
                var data = new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, string>>(File.ReadAllText(settingsPath, Encoding.UTF8));
                string saved; if (data.TryGetValue("folder", out saved) && Directory.Exists(saved)) return saved;
            } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (String.IsNullOrEmpty(profile)) profile = Environment.GetEnvironmentVariable("USERPROFILE");
            return String.IsNullOrEmpty(profile) ? AppDomain.CurrentDomain.BaseDirectory : Path.Combine(profile, "Downloads");
        }
        void SaveFolder()
        {
            try { File.WriteAllText(settingsPath, new JavaScriptSerializer().Serialize(new { folder = folder.Text.Trim() }), Encoding.UTF8); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        void OpenFolder()
        {
            try {
                if (lastFile != null && File.Exists(lastFile)) Process.Start("explorer.exe", "/select,\"" + lastFile + "\"");
                else if (Directory.Exists(folder.Text)) Process.Start(folder.Text);
                else MessageBox.Show(this, "这个文件夹尚不存在，请先选择保存位置。", "轻松下载");
            } catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开文件夹"); }
        }
        protected override void Dispose(bool disposing)
        { if (disposing) { tips.Dispose(); if (Icon != null) Icon.Dispose(); } base.Dispose(disposing); }
    }

    sealed class Meter : Control
    {
        double value;
        public double Value { get { return value; } set { this.value = Math.Max(0, Math.Min(1, value)); Invalidate(); } }
        public Meter() { DoubleBuffered = true; AccessibleName = "下载进度"; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.FromArgb(224, 231, 223));
            using (var brush = new SolidBrush(Color.FromArgb(21, 111, 84))) e.Graphics.FillRectangle(brush, 0, 0, (int)(Width * value), Height);
        }
    }
}
