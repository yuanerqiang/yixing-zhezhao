using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MaskInstaller
{
    internal static class Program
    {
        private const string ProductName = "学习遮罩";
        private const string Version = "1.0.0";
        private const string UninstallKey =
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\StudyMask";

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (IsUninstallMode(args))
            {
                Application.Run(new UninstallForm());
                return;
            }

            Application.Run(new SetupForm());
        }

        public static string InstallDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "StudyMask");
            }
        }

        public static string InstalledAppPath
        {
            get { return Path.Combine(InstallDirectory, ProductName + ".exe"); }
        }

        public static string InstalledUninstallerPath
        {
            get { return Path.Combine(InstallDirectory, "卸载" + ProductName + ".exe"); }
        }

        public static string DesktopShortcutPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.DesktopDirectory),
                    ProductName + ".lnk");
            }
        }

        public static string StartMenuShortcutPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.Programs),
                    ProductName + ".lnk");
            }
        }

        public static bool IsInstalled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallKey))
            {
                return key != null && File.Exists(InstalledAppPath);
            }
        }

        public static void InstallApplication()
        {
            if (IsApplicationRunning())
            {
                throw new InvalidOperationException(
                    "请先关闭正在运行的学习遮罩，再重新安装。");
            }

            Directory.CreateDirectory(InstallDirectory);
            ExtractApplication(InstalledAppPath);
            File.Copy(
                Application.ExecutablePath,
                InstalledUninstallerPath,
                true);

            CreateShortcut(
                DesktopShortcutPath,
                InstalledAppPath,
                InstallDirectory,
                ProductName);
            CreateShortcut(
                StartMenuShortcutPath,
                InstalledAppPath,
                InstallDirectory,
                ProductName);
            WriteUninstallRegistry();
        }

        public static void OpenApplication()
        {
            if (!File.Exists(InstalledAppPath))
            {
                throw new InvalidOperationException("学习遮罩还没有安装。");
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = InstalledAppPath,
                WorkingDirectory = InstallDirectory,
                UseShellExecute = true
            });
        }

        public static void BeginUninstall()
        {
            string directory = InstallDirectory;

            RemoveShortcut(DesktopShortcutPath);
            RemoveShortcut(StartMenuShortcutPath);

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                UninstallKey,
                true))
            {
                if (key != null)
                {
                    key.Close();
                }
            }

            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            ScheduleDirectoryDeletion(directory);
            Application.Exit();
        }

        public static Icon LoadIcon()
        {
            using (Stream stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("AppIcon.ico"))
            {
                if (stream == null)
                {
                    return SystemIcons.Application;
                }

                using (Icon icon = new Icon(stream))
                {
                    return (Icon)icon.Clone();
                }
            }
        }

        public static Image LoadCoverImage()
        {
            using (Stream stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("AppCover.png"))
            {
                if (stream == null)
                {
                    return null;
                }

                using (Image image = Image.FromStream(stream))
                {
                    return new Bitmap(image);
                }
            }
        }

        private static bool IsUninstallMode(string[] args)
        {
            string executableName = Path.GetFileNameWithoutExtension(
                Application.ExecutablePath);

            if (executableName.StartsWith("卸载", StringComparison.Ordinal))
            {
                return true;
            }

            foreach (string argument in args)
            {
                if (string.Equals(
                    argument,
                    "--uninstall",
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsApplicationRunning()
        {
            Process[] processes = Process.GetProcessesByName(ProductName);
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (Process process in processes)
                {
                    process.Dispose();
                }
            }
        }

        private static void ExtractApplication(string targetPath)
        {
            using (Stream resource = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("StudyMaskApp.exe"))
            {
                if (resource == null)
                {
                    throw new InvalidOperationException(
                        "安装包内没有找到主程序。");
                }

                string temporaryPath = targetPath + ".tmp";
                using (FileStream output = File.Create(temporaryPath))
                {
                    resource.CopyTo(output);
                }

                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                File.Move(temporaryPath, targetPath);
            }
        }

        private static void WriteUninstallRegistry()
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("无法写入卸载信息。");
                }

                key.SetValue("DisplayName", ProductName);
                key.SetValue("DisplayVersion", Version);
                key.SetValue("Publisher", "本地应用");
                key.SetValue("InstallLocation", InstallDirectory);
                key.SetValue("DisplayIcon", InstalledAppPath + ",0");
                key.SetValue(
                    "UninstallString",
                    "\"" + InstalledUninstallerPath + "\" --uninstall");
                key.SetValue(
                    "QuietUninstallString",
                    "\"" + InstalledUninstallerPath + "\" --uninstall");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue(
                    "EstimatedSize",
                    GetDirectorySizeInKilobytes(InstallDirectory),
                    RegistryValueKind.DWord);
            }
        }

        private static int GetDirectorySizeInKilobytes(string directory)
        {
            long bytes = 0;

            if (Directory.Exists(directory))
            {
                foreach (string file in Directory.GetFiles(
                    directory,
                    "*",
                    SearchOption.AllDirectories))
                {
                    bytes += new FileInfo(file).Length;
                }
            }

            return (int)Math.Max(1, bytes / 1024);
        }

        private static void CreateShortcut(
            string shortcutPath,
            string targetPath,
            string workingDirectory,
            string description)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath));

            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                throw new InvalidOperationException("系统不支持创建快捷方式。");
            }

            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                null,
                shell,
                new object[] { shortcutPath });
            Type shortcutType = shortcut.GetType();

            shortcutType.InvokeMember(
                "TargetPath",
                BindingFlags.SetProperty,
                null,
                shortcut,
                new object[] { targetPath });
            shortcutType.InvokeMember(
                "WorkingDirectory",
                BindingFlags.SetProperty,
                null,
                shortcut,
                new object[] { workingDirectory });
            shortcutType.InvokeMember(
                "IconLocation",
                BindingFlags.SetProperty,
                null,
                shortcut,
                new object[] { targetPath + ",0" });
            shortcutType.InvokeMember(
                "Description",
                BindingFlags.SetProperty,
                null,
                shortcut,
                new object[] { description });
            shortcutType.InvokeMember(
                "Save",
                BindingFlags.InvokeMethod,
                null,
                shortcut,
                null);
        }

        private static void RemoveShortcut(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Shortcut cleanup is best effort.
            }
        }

        private static void ScheduleDirectoryDeletion(string directory)
        {
            string batchPath = Path.Combine(
                Path.GetTempPath(),
                "StudyMaskUninstall_" + Guid.NewGuid().ToString("N") + ".bat");

            string script =
                "@echo off\r\n"
                + "timeout /t 2 /nobreak >nul\r\n"
                + "for /L %%i in (1,1,20) do (\r\n"
                + "  rmdir /s /q \"" + directory + "\" 2>nul\r\n"
                + "  if not exist \"" + directory + "\" goto done\r\n"
                + "  timeout /t 1 /nobreak >nul\r\n"
                + ")\r\n"
                + ":done\r\n"
                + "del \"%~f0\" >nul 2>nul\r\n";

            File.WriteAllText(batchPath, script, Encoding.Default);

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c \"" + batchPath + "\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly Label statusLabel;
        private readonly Label locationLabel;
        private readonly Button installButton;

        public SetupForm()
        {
            Text = "学习遮罩安装程序";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(720, 470);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular);
            Icon = Program.LoadIcon();

            Panel cover = new Panel();
            cover.SetBounds(0, 0, 720, 164);
            cover.BackColor = Color.FromArgb(30, 35, 48);

            PictureBox coverImage = new PictureBox();
            coverImage.Image = Program.LoadCoverImage();
            coverImage.SizeMode = PictureBoxSizeMode.Zoom;
            coverImage.SetBounds(34, 24, 116, 116);
            coverImage.BackColor = Color.Transparent;

            Label title = new Label();
            title.Text = "学习遮罩";
            title.Font = new Font("Microsoft YaHei UI", 25F, FontStyle.Bold);
            title.ForeColor = Color.White;
            title.SetBounds(178, 34, 480, 52);

            Label subtitle = new Label();
            subtitle.Text = "隐藏网页文字，保持鼠标操作流畅";
            subtitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Regular);
            subtitle.ForeColor = Color.FromArgb(190, 199, 212);
            subtitle.SetBounds(182, 91, 470, 32);

            cover.Controls.Add(coverImage);
            cover.Controls.Add(title);
            cover.Controls.Add(subtitle);

            Label intro = new Label();
            intro.Text = "安装后会在桌面和开始菜单创建快捷方式。";
            intro.ForeColor = Color.FromArgb(65, 65, 65);
            intro.SetBounds(45, 194, 630, 30);

            locationLabel = new Label();
            locationLabel.ForeColor = Color.FromArgb(105, 105, 105);
            locationLabel.SetBounds(45, 230, 630, 48);

            statusLabel = new Label();
            statusLabel.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            statusLabel.SetBounds(45, 286, 630, 34);

            installButton = CreateButton("安装", 45, 350, 132, 44);
            installButton.Click += delegate { Install(); };

            Button openButton = CreateButton("打开软件", 190, 350, 132, 44);
            openButton.Click += delegate
            {
                try
                {
                    Program.OpenApplication();
                }
                catch (Exception exception)
                {
                    MessageBox.Show(
                        this,
                        exception.Message,
                        "无法打开",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            };

            Button uninstallButton = CreateButton("卸载", 335, 350, 132, 44);
            uninstallButton.Click += delegate
            {
                DialogResult result = MessageBox.Show(
                    this,
                    "确定要卸载学习遮罩吗？",
                    "卸载确认",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    Program.BeginUninstall();
                }
            };

            Button closeButton = CreateButton("关闭", 480, 350, 132, 44);
            closeButton.Click += delegate { Close(); };

            Controls.Add(cover);
            Controls.Add(intro);
            Controls.Add(locationLabel);
            Controls.Add(statusLabel);
            Controls.Add(installButton);
            Controls.Add(openButton);
            Controls.Add(uninstallButton);
            Controls.Add(closeButton);

            UpdateInstalledState();
        }

        private Button CreateButton(
            string text,
            int x,
            int y,
            int width,
            int height)
        {
            Button button = new Button();
            button.Text = text;
            button.SetBounds(x, y, width, height);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(180, 188, 198);
            button.BackColor = Color.White;
            button.ForeColor = Color.FromArgb(40, 45, 55);
            return button;
        }

        private void Install()
        {
            try
            {
                installButton.Enabled = false;
                Cursor = Cursors.WaitCursor;
                Program.InstallApplication();
                UpdateInstalledState();
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    "安装失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
                installButton.Enabled = true;
            }
        }

        private void UpdateInstalledState()
        {
            locationLabel.Text = "安装位置：" + Program.InstallDirectory;

            if (Program.IsInstalled())
            {
                statusLabel.Text = "已安装，可以正常使用。";
                statusLabel.ForeColor = Color.FromArgb(34, 140, 86);
                installButton.Text = "重新安装";
            }
            else
            {
                statusLabel.Text = "尚未安装。";
                statusLabel.ForeColor = Color.FromArgb(150, 100, 35);
                installButton.Text = "安装";
            }
        }
    }

    internal sealed class UninstallForm : Form
    {
        public UninstallForm()
        {
            Text = "卸载学习遮罩";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 250);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular);
            Icon = Program.LoadIcon();

            Label title = new Label();
            title.Text = "卸载学习遮罩";
            title.Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold);
            title.SetBounds(35, 30, 430, 44);

            Label message = new Label();
            message.Text = "将删除程序文件、桌面快捷方式和开始菜单快捷方式。";
            message.ForeColor = Color.FromArgb(80, 80, 80);
            message.SetBounds(38, 88, 425, 34);

            Button confirmButton = new Button();
            confirmButton.Text = "确认卸载";
            confirmButton.SetBounds(70, 165, 160, 45);
            confirmButton.Click += delegate
            {
                DialogResult result = MessageBox.Show(
                    this,
                    "确定要卸载学习遮罩吗？",
                    "卸载确认",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result == DialogResult.Yes)
                {
                    Program.BeginUninstall();
                }
            };

            Button cancelButton = new Button();
            cancelButton.Text = "取消";
            cancelButton.SetBounds(270, 165, 160, 45);
            cancelButton.Click += delegate { Close(); };

            Controls.Add(title);
            Controls.Add(message);
            Controls.Add(confirmButton);
            Controls.Add(cancelButton);
        }
    }
}
