using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using PlasmaDownloader;
using ZkData;
using SpringDownloader.Notifications;
using ZeroKLobby.MicroLobby;
using ZeroKLobby.Notifications;

namespace ZeroKLobby
{
    public partial class MainWindow : Form
    {

        string baloonTipPath = null;

        readonly ToolStripMenuItem btnExit;

        bool closeForReal;
        FormWindowState lastState = FormWindowState.Normal;

        readonly NotifyIcon systrayIcon;
        readonly Timer timer1 = new Timer();
        readonly ContextMenuStrip trayStrip;
        public NavigationControl navigationControl { get { return navigationControl1; } }
        public ChatTab ChatTab { get { return navigationControl1.ChatTab; } }
        public static MainWindow Instance { get; private set; }

        public NotifySection NotifySection { get { return notifySection1; } }

        public MainWindow()
        {
            InitializeComponent();
            Instance = this;

            btnExit = new ToolStripMenuItem { Name = "btnExit", Size = new Size(92, 22), Text = "Exit" };
            btnExit.Click += btnExit_Click;


            trayStrip = new ContextMenuStrip();
            trayStrip.Items.AddRange(new ToolStripItem[] { btnExit });
            trayStrip.Name = "trayStrip";
            trayStrip.Size = new Size(93, 26);

            systrayIcon = new NotifyIcon { ContextMenuStrip = trayStrip, Text = "Zero-K Launcher", Visible = true };
            systrayIcon.MouseDown += systrayIcon_MouseDown;
            systrayIcon.BalloonTipClicked += systrayIcon_BalloonTipClicked;

            if (Program.Downloader != null)
            {
                timer1.Interval = 250;
                timer1.Tick += timer1_Tick;

                Program.Downloader.DownloadAdded += TorrentManager_DownloadAdded;
                timer1.Start();
            }
        }



        public void DisplayLog()
        {
            if (!FormLog.Instance.Visible)
            {
                FormLog.Instance.Visible = true;
                FormLog.Instance.Focus();
            }
            else FormLog.Instance.Visible = false;
        }


        public void Exit()
        {
            if (closeForReal) return;
            closeForReal = true;
            Program.CloseOnNext = true;
            InvokeFunc(() => { systrayIcon.Visible = false; });
            InvokeFunc(Close);
        }

        public Control GetHoveredControl()
        {
            Control hovered;
            try
            {
                if (ActiveForm != null && ActiveForm.Visible && !(ActiveForm is ToolTipForm))
                {
                    hovered = ActiveForm.GetHoveredControl();
                    if (hovered != null)
                        return hovered;
                }
                foreach (var lastForm in Application.OpenForms.OfType<Form>().Where(x => !(x is ToolTipForm) && x.Visible))
                {
                    hovered = lastForm.GetHoveredControl();
                    if (hovered != null)
                        return hovered;
                }
            }
            catch (Exception e)
            {
                Trace.TraceError("MainWindow.GetHoveredControl error:", e); //random crash with NULL error on line 140, is weird since already have NULL check (high probability in Linux when we changed focus)
            }
            return null;
        }

        public void InvokeFunc(Action funcToInvoke)
        {
            try
            {
                if (InvokeRequired) Invoke(funcToInvoke);
                else funcToInvoke();
            }
            catch (Exception ex)
            {
                Trace.TraceError("Error invoking: {0}", ex);
            }
        }

        /// <summary>
        /// Alerts user
        /// </summary>
        /// <param name="navigationPath">navigation path of event - alert is set on this and disabled if users goes there</param>
        /// <param name="message">bubble message - setting null means no bubble</param>
        /// <param name="useSound">use sound notification</param>
        /// <param name="useFlashing">use flashing</param>
        public void NotifyUser(string navigationPath, string message, bool useSound = false, bool useFlashing = false)
        {
            var showBalloon =
                !((Program.Conf.DisableChannelBubble && navigationPath.Contains("chat/channel/")) ||
                  (Program.Conf.DisablePmBubble && navigationPath.Contains("chat/user/")));

            var isHidden = WindowState == FormWindowState.Minimized || Visible == false || ActiveForm == null;
            var isPathDifferent = navigationControl.Path != navigationPath;

            if (isHidden || isPathDifferent)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    baloonTipPath = navigationPath;
                    if (showBalloon) systrayIcon.ShowBalloonTip(5000, "Zero-K", TextColor.StripCodes(message), ToolTipIcon.Info);
                }
            }
            if (isHidden && useFlashing) FlashWindow();
            if (isPathDifferent) navigationControl.HilitePath(navigationPath, useFlashing ? HiliteLevel.Flash : HiliteLevel.Bold);
            if (useSound)
            {
                try
                {
                    SystemSounds.Exclamation.Play();
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Error exclamation play: {0}", ex); // Is this how it's done?
                }
            }
        }

        public void PopupSelf()
        {
            try
            {
                if (!InvokeRequired)
                {
                    var finalState = lastState;
                    var wasminimized = WindowState == FormWindowState.Minimized;
                    if (wasminimized) WindowState = FormWindowState.Maximized;
                    Show();
                    Activate();
                    Focus();
                    if (wasminimized) WindowState = finalState;
                }
                else InvokeFunc(PopupSelf);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Error popping up self: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Flashes window if its not foreground - until it is foreground
        /// </summary>
        protected void FlashWindow()
        {
            if (!Focused || !Visible || WindowState == FormWindowState.Minimized)
            {
                Visible = true;
                if (Environment.OSVersion.Platform != PlatformID.Unix)
                {
                    // todo implement for linux with #define NET_WM_STATE_DEMANDS_ATTENTION=42
                    var info = new WindowsApi.FLASHWINFO();
                    info.hwnd = Handle;
                    info.dwFlags = 0x0000000C | 0x00000003; // flash all until foreground
                    info.cbSize = Convert.ToUInt32(Marshal.SizeOf(info));
                    WindowsApi.FlashWindowEx(ref info);
                }
            }
        }


        void UpdateDownloads()
        {
            try
            {
                if (Program.Downloader != null && !Program.CloseOnNext)
                {
                    // remove aborted
                    foreach (var pane in
                        new List<INotifyBar>(Program.NotifySection.Bars).OfType<DownloadBar>()
                                                                        .Where(x => x.Download.IsAborted || x.Download.IsComplete == true)) Program.NotifySection.RemoveBar(pane);

                    // update existing
                    foreach (var pane in new List<INotifyBar>(Program.NotifySection.Bars).OfType<DownloadBar>()) pane.UpdateInfo();
                }
            }
            catch (Exception ex)
            {
                Trace.TraceError("Error updating transfers: {0}", ex);
            }
        }



        void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState != FormWindowState.Minimized) lastState = WindowState;
        }

        void MainWindow_Load(object sender, EventArgs e)
        {
            if (Debugger.IsAttached) Text = "==== DEBUGGING ===";
            else Text = "Zero-K lobby";
            Text += " " + Assembly.GetEntryAssembly().GetName().Version;

            Icon = ZklResources.ZkIcon;
            systrayIcon.Icon = ZklResources.ZkIcon;

            Program.SpringScanner.Start();

            if (Program.StartupArgs != null && Program.StartupArgs.Length > 0) navigationControl.Path = Program.StartupArgs[0];

            if (Program.Conf.ConnectOnStartup) Program.ConnectBar.TryToConnectTasClient();
            else NotifySection.AddBar(Program.ConnectBar);
        }

        void TorrentManager_DownloadAdded(object sender, EventArgs<Download> e)
        {
            Invoke(new Action(() => Program.NotifySection.AddBar(new DownloadBar(e.Data))));
        }

        void btnExit_Click(object sender, EventArgs e)
        {
            Exit();
        }



        void systrayIcon_BalloonTipClicked(object sender, EventArgs e)
        {
            navigationControl.Path = baloonTipPath;
            PopupSelf();
        }


        void systrayIcon_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) PopupSelf();
        }


        void timer1_Tick(object sender, EventArgs e)
        {
            UpdateDownloads();
        }

        private void MainWindow_FormClosing(object sender, FormClosingEventArgs e)
        {
            Program.CloseOnNext = true;
            if (Program.TasClient != null) Program.TasClient.RequestDisconnect();
            Program.SaveConfig();
            WindowState = FormWindowState.Minimized;
            systrayIcon.Visible = false;
            Hide();
        }
    }
}