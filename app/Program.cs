using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MaskApp
{
    internal enum MaskTool
    {
        Mask,
        Erase
    }

    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main()
        {
            try
            {
                SetProcessDPIAware();
            }
            catch
            {
                // Older Windows versions may not provide this function.
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ControlWindow());
        }
    }

    internal sealed class ControlWindow : Form
    {
        private readonly CloneOverlay overlay;
        private readonly Button maskButton;
        private readonly Button eraseButton;
        private readonly Button finishButton;
        private readonly Button undoButton;
        private readonly Button clearButton;
        private readonly Button refreshButton;
        private readonly Button paymentButton;
        private readonly Label statusLabel;
        private readonly Label brushSizeLabel;
        private readonly System.Windows.Forms.Timer hoverTimer;
        private InputCaptureWindow drawWindow;
        private bool drawingSuspended;
        private LicenseState licenseState;

        public ControlWindow()
        {
            Text = "学习遮罩";
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            BackColor = Color.FromArgb(30, 32, 36);
            ForeColor = Color.White;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(10, 7, 10, 7);

            FlowLayoutPanel layout = new FlowLayoutPanel();
            layout.AutoSize = true;
            layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            layout.WrapContents = true;
            layout.FlowDirection = FlowDirection.LeftToRight;
            layout.BackColor = BackColor;
            layout.Margin = new Padding(0);
            layout.Padding = new Padding(0);
            layout.MaximumSize = new Size(
                Math.Max(640, Screen.PrimaryScreen.WorkingArea.Width - 28),
                0);

            statusLabel = new Label();
            statusLabel.AutoSize = true;
            statusLabel.MinimumSize = new Size(310, 42);
            statusLabel.MaximumSize = new Size(360, 0);
            statusLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular);
            statusLabel.ForeColor = Color.White;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Margin = new Padding(3, 3, 12, 3);

            brushSizeLabel = new Label();
            brushSizeLabel.Text = "粗细 32";
            brushSizeLabel.AutoSize = true;
            brushSizeLabel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            brushSizeLabel.ForeColor = Color.FromArgb(210, 214, 220);
            brushSizeLabel.TextAlign = ContentAlignment.MiddleCenter;
            brushSizeLabel.Margin = new Padding(5, 14, 2, 3);

            TrackBar brushSizeTrackBar = new TrackBar();
            brushSizeTrackBar.Minimum = 12;
            brushSizeTrackBar.Maximum = 64;
            brushSizeTrackBar.TickFrequency = 4;
            brushSizeTrackBar.SmallChange = 2;
            brushSizeTrackBar.LargeChange = 4;
            brushSizeTrackBar.Value = 32;
            brushSizeTrackBar.TickStyle = TickStyle.None;
            brushSizeTrackBar.BackColor = BackColor;
            brushSizeTrackBar.Width = 130;
            brushSizeTrackBar.Height = 42;
            brushSizeTrackBar.Margin = new Padding(0, 5, 8, 0);
            brushSizeTrackBar.ValueChanged += delegate
            {
                overlay.BrushSize = brushSizeTrackBar.Value;
                brushSizeLabel.Text = "粗细 " + brushSizeTrackBar.Value;
            };

            maskButton = CreateBarButton("遮罩");
            maskButton.Click += delegate { ActivateTool(MaskTool.Mask); };

            eraseButton = CreateBarButton("擦除");
            eraseButton.Click += delegate { ActivateTool(MaskTool.Erase); };

            finishButton = CreateBarButton("完成");
            finishButton.Click += delegate { FinishDrawing(); };

            undoButton = CreateBarButton("撤销");
            undoButton.Click += delegate { UndoLastStroke(); };

            clearButton = CreateBarButton("清空");
            clearButton.Click += delegate { ClearStrokes(); };

            refreshButton = CreateBarButton("读背景");
            refreshButton.Click += delegate { RefreshBackground(); };

            paymentButton = CreateBarButton("购买");
            paymentButton.Click += delegate { OpenPaymentPage(); };

            Button closeButton = CreateBarButton("退出");
            closeButton.Click += delegate { Close(); };

            layout.Controls.Add(statusLabel);
            layout.Controls.Add(maskButton);
            layout.Controls.Add(eraseButton);
            layout.Controls.Add(finishButton);
            layout.Controls.Add(undoButton);
            layout.Controls.Add(clearButton);
            layout.Controls.Add(refreshButton);
            layout.Controls.Add(brushSizeLabel);
            layout.Controls.Add(brushSizeTrackBar);
            layout.Controls.Add(paymentButton);
            layout.Controls.Add(closeButton);
            Controls.Add(layout);

            overlay = new CloneOverlay();
            overlay.BrushSize = brushSizeTrackBar.Value;

            hoverTimer = new System.Windows.Forms.Timer();
            hoverTimer.Interval = 100;
            hoverTimer.Tick += delegate { UpdateHoverState(); };
            hoverTimer.Start();

            Resize += delegate
            {
                ApplyRoundedRegion();
                PositionAtTop();
            };

            UpdateStatus(false);
            InitializeLicense();
        }

        private void InitializeLicense()
        {
            try
            {
                licenseState = LicenseManager.Refresh();

                if (licenseState.Locked)
                {
                    SetStatus(
                        "试用已结束，点击“购买”解锁",
                        Color.FromArgb(255, 180, 90));
                }
                else if (licenseState.Paid)
                {
                    SetStatus(
                        "已解锁全部功能",
                        Color.FromArgb(125, 230, 165));
                }
                else
                {
                    int remaining = Math.Max(
                        1,
                        LicenseManager.TrialDays - licenseState.DaysUsed);
                    SetStatus(
                        "试用中（剩余 " + remaining + " 天）",
                        Color.FromArgb(125, 230, 165));
                }
            }
            catch
            {
                licenseState = null;
                SetStatus(
                    "授权状态获取失败（不影响试用）",
                    Color.FromArgb(190, 195, 202));
            }
        }

        private static Button CreateBarButton(string text)
        {
            Button button = new Button();
            button.Text = text;
            button.AutoSize = true;
            button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            button.MinimumSize = new Size(64, 36);
            button.Padding = new Padding(8, 3, 8, 3);
            button.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(82, 86, 94);
            button.UseVisualStyleBackColor = false;
            button.BackColor = Color.FromArgb(53, 57, 64);
            button.ForeColor = Color.White;
            button.Margin = new Padding(3);
            return button;
        }

        private void ActivateTool(MaskTool tool)
        {
            if (licenseState != null && licenseState.Locked)
            {
                MessageBox.Show(
                    this,
                    "7 天免费试用已结束。\n\n"
                        + "支付 " + licenseState.Price.ToString("0.00") + " 元即可解锁全部功能。\n"
                        + "点击「购买」→ 选择「支付宝」完成付款后自动解锁。",
                    "试用已结束",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (drawWindow == null)
            {
                if (!overlay.HasBackground && !CaptureBackgroundAndUpdate(false))
                {
                    return;
                }

                drawWindow = new InputCaptureWindow();
                drawWindow.StrokeStarted += overlay.BeginStroke;
                drawWindow.StrokeMoved += overlay.UpdateStroke;
                drawWindow.StrokeFinished += overlay.CommitStroke;
                drawWindow.StrokeCanceled += overlay.CancelStroke;
                drawWindow.UndoRequested += UndoLastStroke;
                drawWindow.FormClosed += delegate
                {
                    drawWindow = null;
                    drawingSuspended = false;
                    UpdateStatus(false);
                };

            }

            overlay.CurrentTool = tool;
            drawWindow.Cursor = tool == MaskTool.Erase
                ? Cursors.Hand
                : Cursors.Cross;
            overlay.BringToFront();
            BringToFront();

            if (IsPointerInBar())
            {
                SetDrawingSuspended(true);
            }
            else
            {
                SetDrawingSuspended(false);
            }

            overlay.BringToFront();
            BringToFront();
        }

        private void FinishDrawing()
        {
            if (drawWindow != null)
            {
                drawWindow.Close();
            }
        }

        private void UndoLastStroke()
        {
            if (overlay.UndoLastStroke())
            {
                SetStatus("已撤销上一笔", Color.FromArgb(125, 230, 165));
            }
            else
            {
                SetStatus("没有可以撤销的笔迹", Color.FromArgb(255, 190, 90));
            }
        }

        private void ClearStrokes()
        {
            overlay.ClearStrokes();
            SetStatus("已清除全部遮罩", Color.FromArgb(125, 230, 165));
        }

        private void RefreshBackground()
        {
            if (drawWindow != null)
            {
                return;
            }

            if (CaptureBackgroundAndUpdate(true))
            {
                SetStatus("已重新读取背景并清除旧遮罩", Color.FromArgb(125, 230, 165));
            }
        }

        private void OpenPaymentPage()
        {
            try
            {
                DialogResult choice = MessageBox.Show(
                    this,
                    "请选择支付方式：\n\n"
                        + "是（推荐）— 支付宝，扫码或登录支付\n"
                        + "否 — 银行卡支付（海外通道）\n"
                        + "取消 — 暂不购买",
                    "选择支付方式",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);

                if (choice == DialogResult.Cancel)
                {
                    return;
                }

                PaymentServiceLauncher.OpenCheckout(
                    choice == DialogResult.Yes,
                    LicenseManager.GetMachineId(),
                    LicenseManager.GetFirstUseUnixMs());
                SetStatus(
                    choice == DialogResult.Yes
                        ? "支付宝支付页面已在浏览器打开"
                        : "银行卡支付页面已在浏览器打开",
                    Color.FromArgb(125, 230, 165));
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "无法打开购买页面",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private bool CaptureBackgroundAndUpdate(bool clearExistingStrokes)
        {
            Bitmap snapshot = null;
            bool restoreWindow = Visible;

            try
            {
                overlay.CancelStroke();
                overlay.Hide();
                if (Visible)
                {
                    Hide();
                }

                Application.DoEvents();
                Thread.Sleep(140);
                snapshot = ScreenCapture.CaptureVirtualScreen();
            }
            catch
            {
                if (snapshot != null)
                {
                    snapshot.Dispose();
                    snapshot = null;
                }
            }
            finally
            {
                if (restoreWindow && !Visible)
                {
                    Show();
                }

                if (!overlay.Visible)
                {
                    overlay.Show();
                }

                BringToFront();
                overlay.BringToFront();
                BringToFront();
            }

            if (snapshot == null)
            {
                SetStatus("读取屏幕背景失败，请关闭遮挡内容后重试", Color.FromArgb(255, 130, 110));
                return false;
            }

            overlay.SetBackground(snapshot, clearExistingStrokes);
            return true;
        }

        private void UpdateStatus(bool drawing)
        {
            bool erasing = drawing && overlay.CurrentTool == MaskTool.Erase;

            if (drawing)
            {
                if (drawingSuspended)
                {
                    SetStatus(
                        "已暂停绘制，可点击按钮；鼠标移出顶部条继续",
                        Color.FromArgb(255, 190, 90));
                }
                else if (erasing)
                {
                    SetStatus(
                        "擦除画笔：涂过遮罩即可恢复网页",
                        Color.FromArgb(255, 145, 120));
                }
                else
                {
                    SetStatus(
                        "遮罩画笔：涂过要隐藏的文字",
                        Color.FromArgb(125, 230, 165));
                }

                clearButton.Enabled = true;
                refreshButton.Enabled = false;
            }
            else
            {
                if (overlay.HasBackground)
                {
                    SetStatus(
                        "遮罩已生效，鼠标可以穿透",
                        Color.FromArgb(125, 230, 165));
                }
                else
                {
                    SetStatus(
                        "点击“遮罩”或“擦除”开始",
                        Color.FromArgb(190, 195, 202));
                }

                clearButton.Enabled = true;
                refreshButton.Enabled = true;
            }

            finishButton.Enabled = drawing;
            maskButton.BackColor = drawing && !erasing
                ? Color.FromArgb(58, 130, 82)
                : Color.FromArgb(53, 57, 64);
            eraseButton.BackColor = erasing
                ? Color.FromArgb(170, 72, 52)
                : Color.FromArgb(53, 57, 64);
        }

        private void SetStatus(string text, Color color)
        {
            statusLabel.Text = text;
            statusLabel.ForeColor = color;
        }

        private void UpdateHoverState()
        {
            if (!Visible || drawWindow == null)
            {
                return;
            }

            bool pointerInBar = IsPointerInBar();

            if (pointerInBar && !drawingSuspended)
            {
                SetDrawingSuspended(true);
            }
            else if (!pointerInBar && drawingSuspended)
            {
                SetDrawingSuspended(false);
            }
        }

        private void SetDrawingSuspended(bool suspended)
        {
            if (drawWindow == null)
            {
                return;
            }

            drawingSuspended = suspended;

            if (suspended)
            {
                if (drawWindow.Visible)
                {
                    drawWindow.Hide();
                }
            }
            else
            {
                if (!drawWindow.Visible)
                {
                    drawWindow.Show();
                }

                drawWindow.BringToFront();
                overlay.BringToFront();
                BringToFront();
            }

            UpdateStatus(true);
        }

        private bool IsPointerInBar()
        {
            if (!Visible)
            {
                return false;
            }

            return ClientRectangle.Contains(PointToClient(Cursor.Position));
        }

        private void PositionAtTop()
        {
            Screen screen = Screen.PrimaryScreen;
            if (screen == null)
            {
                return;
            }

            int x = screen.WorkingArea.Left
                + Math.Max(0, (screen.WorkingArea.Width - Width) / 2);
            int y = screen.WorkingArea.Top + 4;
            Location = new Point(x, y);
        }

        private void ApplyRoundedRegion()
        {
            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            int radius = Math.Max(8, Math.Min(20, Height / 2));
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(0, 0, radius, radius, 180, 90);
                path.AddArc(Width - radius, 0, radius, radius, 270, 90);
                path.AddArc(
                    Width - radius,
                    Height - radius,
                    radius,
                    radius,
                    0,
                    90);
                path.AddArc(0, Height - radius, radius, radius, 90, 90);
                path.CloseFigure();

                Region previous = Region;
                Region = new Region(path);
                if (previous != null)
                {
                    previous.Dispose();
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Z))
            {
                UndoLastStroke();
                return true;
            }

            return base.ProcessCmdKey(ref message, keyData);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ApplyRoundedRegion();
            PositionAtTop();
            overlay.Show();
            overlay.BringToFront();
            BringToFront();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            InputCaptureWindow window = drawWindow;
            drawWindow = null;

            if (window != null)
            {
                window.Close();
                window.Dispose();
            }

            hoverTimer.Stop();
            hoverTimer.Dispose();
            overlay.Close();
            overlay.Dispose();
            base.OnFormClosed(e);
        }
    }

    internal static class PaymentServiceLauncher
    {
        private const string ServiceBaseUrl = "http://127.0.0.1:8787";
        private static readonly object StartLock = new object();

        public static void OpenCheckout(
            bool useAlipay,
            string machineId,
            string firstUseUnixMs)
        {
            lock (StartLock)
            {
                if (!IsServiceReady())
                {
                    string serviceDirectory = FindServiceDirectory();
                    if (serviceDirectory == null)
                    {
                        throw new InvalidOperationException(
                            "没有找到 payment-service 目录。请确认它在程序目录或其上一级目录中。");
                    }

                    string nodePath = FindNodeExecutable();
                    if (nodePath == null)
                    {
                        throw new InvalidOperationException(
                            "没有找到 Node.js。请安装 Node.js 20 或更高版本。");
                    }

                    string serverPath = Path.Combine(serviceDirectory, "server.mjs");
                    if (!File.Exists(serverPath))
                    {
                        throw new InvalidOperationException(
                            "payment-service 中缺少 server.mjs。");
                    }

                    StartService(nodePath, serviceDirectory, serverPath);
                    if (!WaitForService(8000))
                    {
                        throw new InvalidOperationException(
                            "支付服务启动失败。请先在 payment-service 目录运行 npm install，"
                            + "并检查 .env 配置。");
                    }
                }

                string path;
                if (useAlipay)
                {
                    path = "/alipay/buy?machineId="
                        + Uri.EscapeDataString(machineId ?? "")
                        + "&firstUse="
                        + Uri.EscapeDataString(firstUseUnixMs ?? "");
                }
                else
                {
                    path = "/buy";
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = ServiceBaseUrl + path,
                    UseShellExecute = true
                });
            }
        }

        private static void StartService(
            string nodePath,
            string serviceDirectory,
            string serverPath)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = nodePath;
            startInfo.Arguments = "\"" + serverPath + "\"";
            startInfo.WorkingDirectory = serviceDirectory;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;

            Process process = Process.Start(startInfo);
            if (process != null)
            {
                process.Dispose();
            }
        }

        private static bool WaitForService(int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);

            while (DateTime.UtcNow < deadline)
            {
                if (IsServiceReady())
                {
                    return true;
                }

                Thread.Sleep(200);
            }

            return IsServiceReady();
        }

        private static bool IsServiceReady()
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(
                    ServiceBaseUrl + "/health");
                request.Method = "GET";
                request.Timeout = 700;
                request.ReadWriteTimeout = 700;
                request.Proxy = null;

                using (HttpWebResponse response =
                    (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode < 200
                        || (int)response.StatusCode >= 300)
                    {
                        return false;
                    }

                    using (Stream stream = response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        string body = reader.ReadToEnd();
                        return body.Contains(
                            "\"service\": \"study-mask-payment-service\"");
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static string FindServiceDirectory()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDirectory, "payment-service"),
                Path.GetFullPath(Path.Combine(
                    baseDirectory,
                    "..",
                    "payment-service")),
                Path.GetFullPath(Path.Combine(
                    baseDirectory,
                    "..",
                    "..",
                    "payment-service"))
            };

            foreach (string candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string FindNodeExecutable()
        {
            string path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path))
            {
                foreach (string directory in path.Split(Path.PathSeparator))
                {
                    if (string.IsNullOrWhiteSpace(directory))
                    {
                        continue;
                    }

                    try
                    {
                        string candidate = Path.Combine(directory.Trim(), "node.exe");
                        if (File.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                    catch
                    {
                        // Ignore malformed PATH entries.
                    }
                }
            }

            string programFiles = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles);
            string defaultPath = Path.Combine(
                programFiles,
                "nodejs",
                "node.exe");
            return File.Exists(defaultPath) ? defaultPath : null;
        }
    }

    internal sealed class LicenseState
    {
        public bool TrialActive;
        public bool Paid;
        public decimal Price;
        public int DaysUsed;
        public int TrialDays;

        public bool Locked
        {
            get { return !Paid && !TrialActive; }
        }
    }

    internal static class LicenseManager
    {
        private const string ServiceBaseUrl = "http://127.0.0.1:8787";
        private const string RegistryPath = @"Software\StudyMask";
        private const int DefaultTrialDays = 7;
        private const decimal TrialPrice = 4.00m;
        private const decimal FullPrice = 9.90m;

        private static string cachedMachineId;
        private static string cachedFirstUseUnixMs;

        public static int TrialDays
        {
            get { return DefaultTrialDays; }
        }

        public static string GetMachineId()
        {
            if (!string.IsNullOrEmpty(cachedMachineId))
            {
                return cachedMachineId;
            }

            string registryValue = ReadRegistry("MachineId");
            string fileValue = ReadAppDataFile("machine.txt");

            if (!string.IsNullOrEmpty(registryValue))
            {
                cachedMachineId = registryValue;
                if (string.IsNullOrEmpty(fileValue))
                {
                    WriteAppDataFile("machine.txt", registryValue);
                }
                return registryValue;
            }

            if (!string.IsNullOrEmpty(fileValue))
            {
                cachedMachineId = fileValue;
                WriteRegistry("MachineId", fileValue);
                return fileValue;
            }

            string generated = Guid.NewGuid().ToString("N");
            cachedMachineId = generated;
            WriteRegistry("MachineId", generated);
            WriteAppDataFile("machine.txt", generated);
            return generated;
        }

        public static string GetFirstUseUnixMs()
        {
            if (!string.IsNullOrEmpty(cachedFirstUseUnixMs))
            {
                return cachedFirstUseUnixMs;
            }

            string registryValue = ReadRegistry("FirstUseUnixMs");
            string fileValue = ReadAppDataFile("first_use.txt");

            if (!string.IsNullOrEmpty(registryValue)
                && !string.IsNullOrEmpty(fileValue))
            {
                cachedFirstUseUnixMs = EarlierUnixMs(registryValue, fileValue);
                return cachedFirstUseUnixMs;
            }

            string existing = !string.IsNullOrEmpty(registryValue)
                ? registryValue
                : fileValue;
            if (!string.IsNullOrEmpty(existing))
            {
                cachedFirstUseUnixMs = existing;
                WriteRegistry("FirstUseUnixMs", existing);
                WriteAppDataFile("first_use.txt", existing);
                return existing;
            }

            string now = UnixMsNow().ToString();
            cachedFirstUseUnixMs = now;
            WriteRegistry("FirstUseUnixMs", now);
            WriteAppDataFile("first_use.txt", now);
            return now;
        }

        /// <summary>
        /// 同步并查询授权状态：先注册本机（首次使用时间以服务端记录为准，重装不会重置），
        /// 再返回服务端权威状态。服务不可用时退回本地计算（不解除锁定）。
        /// </summary>
        public static LicenseState Refresh()
        {
            string machineId = GetMachineId();
            string firstUse = GetFirstUseUnixMs();

            try
            {
                string url = ServiceBaseUrl
                    + "/api/alipay/register?format=text&machineId="
                    + Uri.EscapeDataString(machineId)
                    + "&firstUse="
                    + Uri.EscapeDataString(firstUse);

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.Timeout = 2500;
                request.ReadWriteTimeout = 2500;
                request.Proxy = null;

                using (HttpWebResponse response =
                    (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode < 200
                        || (int)response.StatusCode >= 300)
                    {
                        return ComputeFallbackState(machineId, firstUse);
                    }

                    using (Stream stream = response.GetResponseStream())
                    using (StreamReader reader = new StreamReader(stream))
                    {
                        LicenseState state = ParseState(reader.ReadToEnd());
                        if (state != null)
                        {
                            return state;
                        }
                    }
                }
            }
            catch
            {
                // 支付服务不可用：按本地时间兜底，绝不误判为已解锁。
            }

            return ComputeFallbackState(machineId, firstUse);
        }

        private static LicenseState ComputeFallbackState(
            string machineId,
            string firstUseUnixMs)
        {
            LicenseState state = new LicenseState();
            state.TrialDays = DefaultTrialDays;
            state.Paid = false;

            long firstUse;
            if (long.TryParse(firstUseUnixMs, out firstUse) && firstUse > 0)
            {
                long now = UnixMsNow();
                long days = firstUse <= now
                    ? (now - firstUse) / 86400000L
                    : 0;
                state.DaysUsed = days > int.MaxValue ? int.MaxValue : (int)days;
            }
            else
            {
                state.DaysUsed = 0;
            }

            state.TrialActive = state.DaysUsed < state.TrialDays;
            state.Price = state.TrialActive ? TrialPrice : FullPrice;
            return state;
        }

        private static LicenseState ParseState(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            LicenseState state = new LicenseState();
            state.TrialActive = true;
            state.Paid = false;
            state.Price = TrialPrice;
            state.TrialDays = DefaultTrialDays;
            state.DaysUsed = 0;

            foreach (string rawLine in text.Split('\n'))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim();

                switch (key)
                {
                    case "trialActive":
                        state.TrialActive = value == "true";
                        break;
                    case "paid":
                        state.Paid = value == "true";
                        break;
                    case "price":
                        {
                            decimal parsed;
                            if (decimal.TryParse(value, out parsed))
                            {
                                state.Price = parsed;
                            }
                            break;
                        }
                    case "daysUsed":
                        {
                            int parsed;
                            if (int.TryParse(value, out parsed) && parsed >= 0)
                            {
                                state.DaysUsed = parsed;
                            }
                            break;
                        }
                    case "trialDays":
                        {
                            int parsed;
                            if (int.TryParse(value, out parsed) && parsed > 0)
                            {
                                state.TrialDays = parsed;
                            }
                            break;
                        }
                }
            }

            return state;
        }

        private static string EarlierUnixMs(string first, string second)
        {
            long firstValue;
            long secondValue;
            if (long.TryParse(first, out firstValue)
                && long.TryParse(second, out secondValue))
            {
                return firstValue <= secondValue ? first : second;
            }

            return string.IsNullOrEmpty(first) ? second : first;
        }

        private static long UnixMsNow()
        {
            return (long)(DateTime.UtcNow
                - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
                .TotalMilliseconds;
        }

        private static string ReadRegistry(string name)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                    RegistryPath))
                {
                    if (key == null)
                    {
                        return "";
                    }

                    object value = key.GetValue(name);
                    return value == null ? "" : value.ToString();
                }
            }
            catch
            {
                return "";
            }
        }

        private static void WriteRegistry(string name, string value)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    RegistryPath))
                {
                    if (key != null)
                    {
                        key.SetValue(name, value);
                    }
                }
            }
            catch
            {
                // 注册表写入失败不阻塞：文件副本仍然可用。
            }
        }

        private static string AppDataDirectory()
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "StudyMask");
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch
            {
                // Ignore.
            }
            return directory;
        }

        private static string ReadAppDataFile(string name)
        {
            try
            {
                string path = Path.Combine(AppDataDirectory(), name);
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8).Trim() : "";
            }
            catch
            {
                return "";
            }
        }

        private static void WriteAppDataFile(string name, string value)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(AppDataDirectory(), name),
                    value,
                    Encoding.UTF8);
            }
            catch
            {
                // Ignore.
            }
        }
    }

    internal sealed class InputCaptureWindow : Form
    {
        private bool drawing;
        private Point lastPoint;

        public event Action<Point> StrokeStarted;
        public event Action<Point> StrokeMoved;
        public event Action StrokeFinished;
        public event Action StrokeCanceled;
        public event Action UndoRequested;

        public InputCaptureWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = 0.04;
            Cursor = Cursors.Cross;
            KeyPreview = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Button == MouseButtons.Right)
            {
                if (StrokeCanceled != null)
                {
                    StrokeCanceled();
                }

                Close();
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                drawing = true;
                lastPoint = e.Location;

                if (StrokeStarted != null)
                {
                    StrokeStarted(e.Location);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (!drawing)
            {
                return;
            }

            int distanceX = e.X - lastPoint.X;
            int distanceY = e.Y - lastPoint.Y;

            if ((distanceX * distanceX) + (distanceY * distanceY) < 4)
            {
                return;
            }

            lastPoint = e.Location;

            if (StrokeMoved != null)
            {
                StrokeMoved(e.Location);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (!drawing || e.Button != MouseButtons.Left)
            {
                return;
            }

            drawing = false;

            if (StrokeFinished != null)
            {
                StrokeFinished();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Control && e.KeyCode == Keys.Z)
            {
                if (UndoRequested != null)
                {
                    UndoRequested();
                }

                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                if (StrokeCanceled != null)
                {
                    StrokeCanceled();
                }

                Close();
                e.Handled = true;
            }
        }
    }

    internal sealed class CloneOverlay : Form
    {
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private readonly List<CloneStroke> strokes;
        private Bitmap background;
        private Bitmap maskBitmap;
        private CloneStroke activeStroke;
        private int brushSize = 32;
        private MaskTool currentTool = MaskTool.Mask;

        public CloneOverlay()
        {
            strokes = new List<CloneStroke>();

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen;
            TopMost = true;
            BackColor = Color.Fuchsia;
            TransparencyKey = Color.Fuchsia;
            DoubleBuffered = true;
        }

        public bool HasBackground
        {
            get { return background != null; }
        }

        public int BrushSize
        {
            get { return brushSize; }
            set
            {
                brushSize = Math.Max(12, Math.Min(64, value));
            }
        }

        public MaskTool CurrentTool
        {
            get { return currentTool; }
            set { currentTool = value; }
        }

        public void SetBackground(Bitmap snapshot, bool clearExistingStrokes)
        {
            if (snapshot == null)
            {
                return;
            }

            CancelStroke();

            if (background != null)
            {
                background.Dispose();
            }

            background = snapshot;
            Bounds = SystemInformation.VirtualScreen;

            if (maskBitmap != null)
            {
                maskBitmap.Dispose();
            }

            maskBitmap = new Bitmap(
                background.Width,
                background.Height,
                PixelFormat.Format32bppArgb);

            if (clearExistingStrokes)
            {
                strokes.Clear();
            }

            RenderAllStrokes();
        }

        public void BeginStroke(Point point)
        {
            if (background == null || maskBitmap == null)
            {
                return;
            }

            CancelStroke();

            if (currentTool == MaskTool.Erase)
            {
                activeStroke = new CloneStroke(
                    point,
                    Color.Empty,
                    brushSize,
                    true);
                ErasePoint(point, brushSize);
                Invalidate(GetPointBounds(point, brushSize));
                return;
            }

            Color fillColor = BackgroundSampler.FindCleanColor(
                background,
                point,
                brushSize);

            activeStroke = new CloneStroke(
                point,
                fillColor,
                brushSize,
                false);
            DrawPoint(
                activeStroke.Points[0],
                activeStroke.Colors[0],
                activeStroke.Diameter);
            Invalidate();
        }

        public void UpdateStroke(Point point)
        {
            if (activeStroke == null || maskBitmap == null)
            {
                return;
            }

            Point previousPoint = activeStroke.Points[activeStroke.Points.Count - 1];
            if (previousPoint == point)
            {
                return;
            }

            activeStroke.Points.Add(point);

            if (activeStroke.IsEraser)
            {
                EraseLine(previousPoint, point, activeStroke.Diameter);
            }
            else
            {
                Color fillColor = BackgroundSampler.FindCleanColor(
                    background,
                    point,
                    activeStroke.Diameter);
                activeStroke.Colors.Add(fillColor);

                DrawMaskLineGated(
                    previousPoint,
                    point,
                    activeStroke.Colors[activeStroke.Colors.Count - 2],
                    fillColor,
                    activeStroke.Diameter);
            }

            Invalidate(GetSegmentBounds(previousPoint, point, activeStroke.Diameter));
        }

        public void CommitStroke()
        {
            if (activeStroke != null)
            {
                strokes.Add(activeStroke);
                activeStroke = null;
            }

            Invalidate();
        }

        public bool UndoLastStroke()
        {
            if (activeStroke != null)
            {
                CancelStroke();
                return true;
            }

            if (strokes.Count == 0)
            {
                return false;
            }

            strokes.RemoveAt(strokes.Count - 1);
            RenderAllStrokes();
            return true;
        }

        public void CancelStroke()
        {
            if (activeStroke == null)
            {
                return;
            }

            activeStroke = null;
            RenderAllStrokes();
        }

        public void ClearStrokes()
        {
            activeStroke = null;
            strokes.Clear();
            RenderAllStrokes();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_LAYERED
                    | WS_EX_TRANSPARENT
                    | WS_EX_TOOLWINDOW
                    | WS_EX_NOACTIVATE;
                return parameters;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (maskBitmap == null)
            {
                e.Graphics.Clear(BackColor);
                return;
            }

            e.Graphics.DrawImageUnscaled(maskBitmap, 0, 0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (maskBitmap != null)
                {
                    maskBitmap.Dispose();
                    maskBitmap = null;
                }

                if (background != null)
                {
                    background.Dispose();
                    background = null;
                }
            }

            base.Dispose(disposing);
        }

        private void RenderAllStrokes()
        {
            if (maskBitmap == null || background == null)
            {
                return;
            }

            using (Graphics graphics = Graphics.FromImage(maskBitmap))
            {
                graphics.Clear(BackColor);
                graphics.SmoothingMode = SmoothingMode.None;

                foreach (CloneStroke stroke in strokes)
                {
                    if (stroke.IsEraser)
                    {
                        EraseStroke(graphics, stroke.Points, stroke.Diameter);
                    }
                    else
                    {
                        DrawMaskStrokeGated(
                            stroke.Points,
                            stroke.Colors,
                            stroke.Diameter);
                    }
                }
            }

            Invalidate();
        }

        private void DrawMaskStrokeGated(
            List<Point> points,
            List<Color> colors,
            int diameter)
        {
            if (points.Count == 0)
            {
                return;
            }

            DrawPoint(points[0], colors[0], diameter);

            for (int index = 1; index < points.Count; index++)
            {
                DrawMaskLineGated(
                    points[index - 1],
                    points[index],
                    colors[index - 1],
                    colors[index],
                    diameter);
            }
        }

        private void DrawPoint(Point point, Color fillColor, int diameter)
        {
            if (maskBitmap == null)
            {
                return;
            }

            Rectangle bounds = ClipToMask(GetPointBounds(point, diameter));
            if (bounds.IsEmpty)
            {
                return;
            }

            using (Bitmap layer = new Bitmap(
                bounds.Width,
                bounds.Height,
                PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(layer))
                {
                    graphics.SmoothingMode = SmoothingMode.None;
                    graphics.Clear(BackColor);
                    graphics.TranslateTransform(-bounds.Left, -bounds.Top);
                    float radius = diameter / 2F;

                    using (SolidBrush brush = new SolidBrush(fillColor))
                    {
                        graphics.FillEllipse(
                            brush,
                            point.X - radius,
                            point.Y - radius,
                            diameter,
                            diameter);
                    }
                }

                ApplyMaskLayer(layer, bounds);
            }
        }

        private void DrawMaskLineGated(
            Point first,
            Point second,
            Color firstColor,
            Color secondColor,
            int diameter)
        {
            if (maskBitmap == null)
            {
                return;
            }

            Rectangle bounds = ClipToMask(GetSegmentBounds(first, second, diameter));
            if (bounds.IsEmpty)
            {
                return;
            }

            using (Bitmap layer = new Bitmap(
                bounds.Width,
                bounds.Height,
                PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(layer))
                {
                    graphics.SmoothingMode = SmoothingMode.None;
                    graphics.Clear(BackColor);
                    graphics.TranslateTransform(-bounds.Left, -bounds.Top);

                    using (LinearGradientBrush brush = new LinearGradientBrush(
                        first,
                        second,
                        firstColor,
                        secondColor))
                    using (Pen pen = new Pen(brush, diameter))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        graphics.DrawLine(pen, first, second);
                    }
                }

                ApplyMaskLayer(layer, bounds);
            }
        }

        private void ApplyMaskLayer(Bitmap layer, Rectangle bounds)
        {
            int transparentArgb = BackColor.ToArgb();

            for (int y = 0; y < bounds.Height; y++)
            {
                int targetY = bounds.Top + y;

                for (int x = 0; x < bounds.Width; x++)
                {
                    int targetX = bounds.Left + x;
                    if (maskBitmap.GetPixel(targetX, targetY).ToArgb()
                        != transparentArgb)
                    {
                        continue;
                    }

                    Color source = layer.GetPixel(x, y);
                    if (source.ToArgb() != transparentArgb)
                    {
                        maskBitmap.SetPixel(targetX, targetY, source);
                    }
                }
            }
        }

        private Rectangle ClipToMask(Rectangle bounds)
        {
            if (maskBitmap == null)
            {
                return Rectangle.Empty;
            }

            Rectangle maskBounds = new Rectangle(
                0,
                0,
                maskBitmap.Width,
                maskBitmap.Height);
            bounds.Intersect(maskBounds);
            return bounds;
        }

        private void ErasePoint(Point point, int diameter)
        {
            if (maskBitmap == null)
            {
                return;
            }

            using (Graphics graphics = Graphics.FromImage(maskBitmap))
            {
                graphics.SmoothingMode = SmoothingMode.None;
                graphics.CompositingMode = CompositingMode.SourceCopy;
                float radius = diameter / 2F;

                using (SolidBrush brush = new SolidBrush(BackColor))
                {
                    graphics.FillEllipse(
                        brush,
                        point.X - radius,
                        point.Y - radius,
                        diameter,
                        diameter);
                }
            }
        }

        private void EraseLine(Point first, Point second, int diameter)
        {
            if (maskBitmap == null)
            {
                return;
            }

            using (Graphics graphics = Graphics.FromImage(maskBitmap))
            {
                graphics.SmoothingMode = SmoothingMode.None;
                graphics.CompositingMode = CompositingMode.SourceCopy;

                using (Pen pen = new Pen(BackColor, diameter))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    graphics.DrawLine(pen, first, second);
                }
            }
        }

        private static void EraseStroke(
            Graphics graphics,
            List<Point> points,
            int diameter)
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;

            if (points.Count == 1)
            {
                float radius = diameter / 2F;
                Point point = points[0];

                using (SolidBrush brush = new SolidBrush(Color.Fuchsia))
                {
                    graphics.FillEllipse(
                        brush,
                        point.X - radius,
                        point.Y - radius,
                        diameter,
                        diameter);
                }

                return;
            }

            using (Pen pen = new Pen(Color.Fuchsia, diameter))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                graphics.DrawLines(pen, points.ToArray());
            }
        }

        private static Rectangle GetPointBounds(Point point, int diameter)
        {
            int padding = (diameter / 2) + 3;
            return new Rectangle(
                point.X - padding,
                point.Y - padding,
                (padding * 2) + 1,
                (padding * 2) + 1);
        }

        private static Rectangle GetSegmentBounds(
            Point first,
            Point second,
            int diameter)
        {
            int padding = (diameter / 2) + 3;
            int left = Math.Min(first.X, second.X) - padding;
            int top = Math.Min(first.Y, second.Y) - padding;
            int right = Math.Max(first.X, second.X) + padding;
            int bottom = Math.Max(first.Y, second.Y) + padding;
            return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }

    }

    internal sealed class CloneStroke
    {
        public readonly List<Point> Points;
        public readonly List<Color> Colors;
        public readonly int Diameter;
        public readonly bool IsEraser;

        public CloneStroke(
            Point startPoint,
            Color color,
            int diameter,
            bool isEraser)
        {
            Points = new List<Point>();
            Points.Add(startPoint);
            Colors = new List<Color>();
            Colors.Add(color);
            Diameter = diameter;
            IsEraser = isEraser;
        }
    }

    internal static class BackgroundSampler
    {
        private static readonly Point[] CandidateOffsets =
        {
            new Point(0, -48),
            new Point(0, 48),
            new Point(-48, 0),
            new Point(48, 0),
            new Point(0, -80),
            new Point(0, 80),
            new Point(-80, 0),
            new Point(80, 0)
        };

        public static Color FindCleanColor(
            Bitmap image,
            Point destination,
            int brushDiameter)
        {
            if (image == null || image.Width <= 0 || image.Height <= 0)
            {
                return Color.White;
            }

            int radius = Math.Max(6, Math.Min(12, brushDiameter / 4));
            if (radius * 2 >= image.Width || radius * 2 >= image.Height)
            {
                return image.GetPixel(
                    Clamp(destination.X, 0, image.Width - 1),
                    Clamp(destination.Y, 0, image.Height - 1));
            }

            double bestScore = double.MaxValue;
            int bestX = Clamp(destination.X, radius, image.Width - radius - 1);
            int bestY = Clamp(destination.Y, radius, image.Height - radius - 1);

            foreach (Point candidate in CandidateOffsets)
            {
                int centerX = Clamp(
                    destination.X + candidate.X,
                    radius,
                    image.Width - radius - 1);
                int centerY = Clamp(
                    destination.Y + candidate.Y,
                    radius,
                    image.Height - radius - 1);

                double variance = CalculateVariance(
                    image,
                    centerX,
                    centerY,
                    radius);

                double distance = Math.Sqrt(
                    (centerX - destination.X) * (centerX - destination.X)
                    + (centerY - destination.Y) * (centerY - destination.Y));
                double score = variance + (distance * 1.5);

                if (score < bestScore)
                {
                    bestScore = score;
                    bestX = centerX;
                    bestY = centerY;
                }
            }

            return CalculateAverageColor(image, bestX, bestY, radius);
        }

        private static double CalculateVariance(
            Bitmap image,
            int centerX,
            int centerY,
            int radius)
        {
            long count = 0;
            double sum = 0;
            double sumSquares = 0;

            for (int y = centerY - radius; y <= centerY + radius; y += 2)
            {
                for (int x = centerX - radius; x <= centerX + radius; x += 2)
                {
                    Color color = image.GetPixel(x, y);
                    double luminance =
                        ((color.R * 299) + (color.G * 587) + (color.B * 114)) / 1000.0;
                    sum += luminance;
                    sumSquares += luminance * luminance;
                    count++;
                }
            }

            if (count == 0)
            {
                return double.MaxValue;
            }

            double mean = sum / count;
            return (sumSquares / count) - (mean * mean);
        }

        private static Color CalculateAverageColor(
            Bitmap image,
            int centerX,
            int centerY,
            int radius)
        {
            long count = 0;
            long red = 0;
            long green = 0;
            long blue = 0;

            for (int y = centerY - radius; y <= centerY + radius; y += 2)
            {
                for (int x = centerX - radius; x <= centerX + radius; x += 2)
                {
                    Color color = image.GetPixel(x, y);
                    red += color.R;
                    green += color.G;
                    blue += color.B;
                    count++;
                }
            }

            if (count == 0)
            {
                return Color.White;
            }

            return Color.FromArgb(
                (int)(red / count),
                (int)(green / count),
                (int)(blue / count));
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }

            if (value > maximum)
            {
                return maximum;
            }

            return value;
        }
    }

    internal static class ScreenCapture
    {
        public static Bitmap CaptureVirtualScreen()
        {
            Rectangle bounds = SystemInformation.VirtualScreen;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                throw new InvalidOperationException("Invalid virtual screen size.");
            }

            Bitmap bitmap = new Bitmap(
                bounds.Width,
                bounds.Height,
                PixelFormat.Format32bppArgb);

            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(
                    bounds.Left,
                    bounds.Top,
                    0,
                    0,
                    bounds.Size,
                    CopyPixelOperation.SourceCopy);
            }

            return bitmap;
        }
    }
}
