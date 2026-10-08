// language: C#, file: Forms/ClickerForm.cs
// Апгрейды пересоздаются при смене языка.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Resources;
using System.Text;
using System.Windows.Forms;
using BunnyBlack.Core;
using Microsoft.Win32;

namespace BunnyBlack.Forms
{
    public partial class ClickerForm : UserControl
    {
        private Label scoreLabel;
        private Label clickPowerLabel;
        private Label autoClickerLabel;
        private PictureBox clickerPicture;
        private FlowLayoutPanel shopPanel;
        private Label titleLabel;
        private Label shopTitleLabel;
        private Button exportBtn;
        private Button importBtn;
        private Button resetBtn;

        private long score = 0;
        private long clickPower = 1;
        private long autoClickerLevel = 0;
        private bool isWinRE;

        private Timer autoClickerTimer;
        private Timer autoSaveTimer;

        private class Upgrade
        {
            public string NameKey { get; set; }
            public string DescKey { get; set; }
            public long Cost { get; set; }
            public long BaseCost { get; set; }
            public int Level { get; set; } = 0;
            public Action OnBuy { get; set; }
        }

        private List<Upgrade> upgrades = new List<Upgrade>();

        public ClickerForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Foreground;
            InitializeComponent();
            LoadProgress();
            StartAutoClicker();
            StartAutoSave();

            ThemeManager.ThemeChanged += ApplyTheme;
            Loc.LanguageChanged += ApplyLanguage;
        }

        public void ApplyTheme()
        {
            if (InvokeRequired) { Invoke(new Action(ApplyTheme)); return; }
            this.BackColor = ThemeManager.Background;
            this.ForeColor = ThemeManager.Foreground;
            ThemeHelper.Apply(this);
            RebuildShop();
            Invalidate(true);
        }

        public void ApplyLanguage()
        {
            if (InvokeRequired) { Invoke(new Action(ApplyLanguage)); return; }
            if (titleLabel != null) titleLabel.Text = Loc.T("clicker.title");
            if (shopTitleLabel != null) shopTitleLabel.Text = Loc.T("clicker.shop");
            if (exportBtn != null) exportBtn.Text = Loc.T("btn.export");
            if (importBtn != null) importBtn.Text = Loc.T("btn.import");
            if (resetBtn != null) resetBtn.Text = Loc.T("btn.reset");

            RebuildShop();
            UpdateUI();
            Invalidate(true);
        }

        private string ProgressFilePath
        {
            get
            {
                try
                {
                    string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
                    if (string.IsNullOrEmpty(exeDir)) exeDir = ".";
                    return Path.Combine(exeDir, "Clicker.dat");
                }
                catch { return "Clicker.dat"; }
            }
        }

        private void InitializeComponent()
        {
            this.Dock = DockStyle.Fill;

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(28, 24, 28, 20),
                BackColor = ThemeManager.Background
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            titleLabel = new Label
            {
                Text = Loc.T("clicker.title"),
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = ThemeManager.Foreground,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            mainLayout.Controls.Add(titleLabel, 0, 0);

            var shopHeader = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                WrapContents = false
            };

            shopTitleLabel = new Label
            {
                Text = Loc.T("clicker.shop"),
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = ThemeManager.Foreground,
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 12, 20, 0)
            };
            shopHeader.Controls.Add(shopTitleLabel);

            exportBtn = MakeSmallButton(Loc.T("btn.export"));
            exportBtn.Click += (s, e) =>
            {
                SaveProgress();
                NedoMessageBox.Show($"Saved:\n{ProgressFilePath}");
            };
            shopHeader.Controls.Add(exportBtn);

            importBtn = MakeSmallButton(Loc.T("btn.import"));
            importBtn.Click += (s, e) =>
            {
                LoadProgress();
                NedoMessageBox.Show("Loaded");
            };
            shopHeader.Controls.Add(importBtn);

            resetBtn = new Button
            {
                Text = Loc.T("btn.reset"),
                Height = 28,
                Width = 90,
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeManager.DangerBack,
                ForeColor = ThemeManager.Danger,
                FlatAppearance = { BorderSize = 1, BorderColor = ThemeManager.Danger },
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 12, 0, 0),
                Font = new Font("Segoe UI", 9)
            };
            resetBtn.Click += (s, e) => ResetProgress();
            shopHeader.Controls.Add(resetBtn);

            mainLayout.Controls.Add(shopHeader, 1, 0);

            var clickerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent
            };
            clickerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            clickerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            clickerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            clickerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

            scoreLabel = new Label
            {
                Text = Loc.T("clicker.clicks", 0),
                Font = new Font("Segoe UI", 22, FontStyle.Bold),
                ForeColor = ThemeManager.Success,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            clickerLayout.Controls.Add(scoreLabel, 0, 0);

            clickPowerLabel = new Label
            {
                Text = Loc.T("clicker.power", 1),
                Font = new Font("Segoe UI", 12),
                ForeColor = ThemeManager.Muted,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            clickerLayout.Controls.Add(clickPowerLabel, 0, 1);

            var pictureContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };

            clickerPicture = new PictureBox
            {
                Size = new Size(320, 320),
                SizeMode = PictureBoxSizeMode.Zoom,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };

            try
            {
                var rm = new ResourceManager("BunnyBlack.Properties.Resources", typeof(ClickerForm).Assembly);
                object img = rm.GetObject("ClickerImage");
                if (img is Image originalImage)
                    clickerPicture.Image = MakeCircularImage(originalImage, 320);
                else
                    clickerPicture.Image = CreatePlaceholderCircle(320);
            }
            catch { clickerPicture.Image = CreatePlaceholderCircle(320); }

            clickerPicture.Click += (s, e) =>
            {
                score += clickPower;
                UpdateUI();
                AnimateClick();
                SaveProgress();
            };

            pictureContainer.Controls.Add(clickerPicture);
            pictureContainer.Resize += (s, e) => CenterPicture(pictureContainer);
            pictureContainer.HandleCreated += (s, e) => CenterPicture(pictureContainer);
            clickerPicture.SizeChanged += (s, e) => CenterPicture(pictureContainer);

            clickerLayout.Controls.Add(pictureContainer, 0, 2);

            autoClickerLabel = new Label
            {
                Text = Loc.T("clicker.auto", 0),
                Font = new Font("Segoe UI", 12),
                ForeColor = ThemeManager.Success,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            clickerLayout.Controls.Add(autoClickerLabel, 0, 3);

            mainLayout.Controls.Add(clickerLayout, 0, 1);

            shopPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.Transparent,
                Padding = new Padding(10, 0, 0, 0)
            };

            DefineUpgrades();
            RebuildShop();

            mainLayout.Controls.Add(shopPanel, 1, 1);
            this.Controls.Add(mainLayout);
        }

        private Button MakeSmallButton(string text)
        {
            return new Button
            {
                Text = text,
                Height = 28,
                Width = 90,
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeManager.PanelAlt,
                ForeColor = ThemeManager.Foreground,
                FlatAppearance = { BorderSize = 1, BorderColor = ThemeManager.Border },
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 12, 6, 0),
                Font = new Font("Segoe UI", 9)
            };
        }

        private void DefineUpgrades()
        {
            upgrades.Clear();
            upgrades.Add(new Upgrade { NameKey = "clicker.up.strong", DescKey = "clicker.up.desc.plus1", Cost = 10, BaseCost = 10, OnBuy = () => { clickPower += 1; } });
            upgrades.Add(new Upgrade { NameKey = "clicker.up.fast", DescKey = "clicker.up.desc.plus5", Cost = 60, BaseCost = 60, OnBuy = () => { clickPower += 5; } });
            upgrades.Add(new Upgrade { NameKey = "clicker.up.steel", DescKey = "clicker.up.desc.plus15", Cost = 200, BaseCost = 200, OnBuy = () => { clickPower += 15; } });
            upgrades.Add(new Upgrade { NameKey = "clicker.up.auto1", DescKey = "clicker.up.desc.auto1", Cost = 50, BaseCost = 50, OnBuy = () => { autoClickerLevel += 1; } });
            upgrades.Add(new Upgrade { NameKey = "clicker.up.auto2", DescKey = "clicker.up.desc.auto5", Cost = 250, BaseCost = 250, OnBuy = () => { autoClickerLevel += 5; } });
            upgrades.Add(new Upgrade { NameKey = "clicker.up.auto3", DescKey = "clicker.up.desc.auto15", Cost = 800, BaseCost = 800, OnBuy = () => { autoClickerLevel += 15; } });
            upgrades.Add(new Upgrade { NameKey = "clicker.up.golden", DescKey = "clicker.up.desc.plus50", Cost = 1000, BaseCost = 1000, OnBuy = () => { clickPower += 50; } });
            upgrades.Add(new Upgrade { NameKey = "clicker.up.giant", DescKey = "clicker.up.desc.plus150", Cost = 4000, BaseCost = 4000, OnBuy = () => { clickPower += 150; } });
            upgrades.Add(new Upgrade { NameKey = "clicker.up.turbo", DescKey = "clicker.up.desc.auto50", Cost = 3000, BaseCost = 3000, OnBuy = () => { autoClickerLevel += 50; } });
            upgrades.Add(new Upgrade { NameKey = "clicker.up.legendary", DescKey = "clicker.up.desc.plus500", Cost = 10000, BaseCost = 10000, OnBuy = () => { clickPower += 500; } });
        }

        private void RebuildShop()
        {
            if (shopPanel == null) return;

            shopPanel.Controls.Clear();

            // Восстанавливаем цену с учётом уровня
            foreach (var u in upgrades)
            {
                u.Cost = u.BaseCost;
                for (int j = 0; j < u.Level; j++)
                    u.Cost = (long)(u.Cost * 1.5);
            }

            foreach (var u in upgrades)
                shopPanel.Controls.Add(CreateShopCard(u));
        }

        private void ResetProgress()
        {
            if (MessageBoxHelper.Show(
                "Reset progress?", Loc.T("warn.confirm"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            try
            {
                score = 0;
                clickPower = 1;
                autoClickerLevel = 0;

                for (int i = 0; i < upgrades.Count; i++)
                {
                    upgrades[i].Level = 0;
                    upgrades[i].Cost = upgrades[i].BaseCost;
                }

                try { if (File.Exists(ProgressFilePath)) File.Delete(ProgressFilePath); }
                catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree(@"SOFTWARE\BunnyBlack\Clicker", false); }
                catch { }

                RebuildShop();
                UpdateUI();

                NedoMessageBox.Show("Reset done");
            }
            catch (Exception ex)
            {
                NedoMessageBox.Show("Error: " + ex.Message, isError: true);
            }
        }

        private void CenterPicture(Panel container)
        {
            if (container == null || clickerPicture == null) return;
            int x = (container.Width - clickerPicture.Width) / 2;
            int y = (container.Height - clickerPicture.Height) / 2;
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            clickerPicture.Location = new Point(x, y);
        }

        private Panel CreateShopCard(Upgrade upgrade)
        {
            var card = new Panel
            {
                Height = 70,
                Width = 340,
                BackColor = ThemeManager.PanelAlt,
                Margin = new Padding(0, 0, 0, 8)
            };
            card.Paint += (s, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle,
                    ThemeManager.Border, 1, ButtonBorderStyle.Solid,
                    ThemeManager.Border, 1, ButtonBorderStyle.Solid,
                    ThemeManager.Border, 1, ButtonBorderStyle.Solid,
                    ThemeManager.Border, 1, ButtonBorderStyle.Solid);
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(12, 8, 12, 8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));

            layout.Controls.Add(new Label
            {
                Text = Loc.T(upgrade.NameKey),
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = ThemeManager.Foreground,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);

            layout.Controls.Add(new Label
            {
                Text = Loc.T(upgrade.DescKey),
                Font = new Font("Segoe UI", 9),
                ForeColor = ThemeManager.Muted,
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 1);

            var buyBtn = new Button
            {
                Text = $"{upgrade.Cost}",
                Height = 40,
                Width = 100,
                BackColor = ThemeManager.SuccessBack,
                ForeColor = ThemeManager.Success,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = ThemeManager.Success },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 0, 4, 0)
            };

            buyBtn.Click += (s, e) =>
            {
                if (score >= upgrade.Cost)
                {
                    score -= upgrade.Cost;
                    upgrade.OnBuy?.Invoke();
                    upgrade.Level++;
                    upgrade.Cost = (long)(upgrade.Cost * 1.5);
                    buyBtn.Text = $"{upgrade.Cost}";
                    UpdateUI();
                    SaveProgress();
                }
                else NedoMessageBox.Show("Not enough", isError: true);
            };

            layout.Controls.Add(buyBtn, 1, 0);
            layout.SetRowSpan(buyBtn, 2);
            card.Controls.Add(layout);
            return card;
        }

        private void UpdateUI()
        {
            if (scoreLabel.InvokeRequired)
            {
                scoreLabel.Invoke(new Action(UpdateUI));
                return;
            }
            scoreLabel.Text = Loc.T("clicker.clicks", score);
            clickPowerLabel.Text = Loc.T("clicker.power", clickPower);
            autoClickerLabel.Text = Loc.T("clicker.auto", autoClickerLevel);
        }

        private void AnimateClick()
        {
            var orig = clickerPicture.Location;
            clickerPicture.Location = new Point(orig.X + 3, orig.Y + 3);
            var t = new Timer { Interval = 50 };
            t.Tick += (ts, te) =>
            {
                clickerPicture.Location = orig;
                t.Stop();
                t.Dispose();
            };
            t.Start();
        }

        private void StartAutoClicker()
        {
            autoClickerTimer = new Timer { Interval = 1000 };
            autoClickerTimer.Tick += (s, e) =>
            {
                if (autoClickerLevel > 0)
                {
                    score += autoClickerLevel;
                    UpdateUI();
                }
            };
            autoClickerTimer.Start();
        }

        private void StartAutoSave()
        {
            autoSaveTimer = new Timer { Interval = 15000 };
            autoSaveTimer.Tick += (s, e) => SaveProgress();
            autoSaveTimer.Start();
        }

        private void SaveProgress()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Score={score}");
                sb.AppendLine($"ClickPower={clickPower}");
                sb.AppendLine($"AutoLevel={autoClickerLevel}");
                for (int i = 0; i < upgrades.Count; i++)
                    sb.AppendLine($"Upgrade{i}_Level={upgrades[i].Level}");

                try { File.WriteAllText(ProgressFilePath, sb.ToString()); } catch { }

                try
                {
                    using (var k = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\BunnyBlack\Clicker"))
                    {
                        if (k != null)
                        {
                            k.SetValue("Score", score.ToString());
                            k.SetValue("ClickPower", clickPower.ToString());
                            k.SetValue("AutoLevel", autoClickerLevel.ToString());
                            for (int i = 0; i < upgrades.Count; i++)
                                k.SetValue($"Upgrade{i}_Level", upgrades[i].Level);
                        }
                    }
                }
                catch { }
            }
            catch { }
        }

        private void LoadProgress()
        {
            try
            {
                if (File.Exists(ProgressFilePath))
                {
                    try
                    {
                        ParseProgressLines(File.ReadAllLines(ProgressFilePath));
                        UpdateUI();
                        RebuildShop();
                        return;
                    }
                    catch { }
                }

                using (var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\BunnyBlack\Clicker"))
                {
                    if (k == null) return;

                    if (long.TryParse(k.GetValue("Score")?.ToString(), out long s)) score = s;
                    if (long.TryParse(k.GetValue("ClickPower")?.ToString(), out long c)) clickPower = c;
                    if (long.TryParse(k.GetValue("AutoLevel")?.ToString(), out long a)) autoClickerLevel = a;

                    for (int i = 0; i < upgrades.Count; i++)
                    {
                        if (int.TryParse(k.GetValue($"Upgrade{i}_Level")?.ToString(), out int lvl) && lvl > 0)
                            upgrades[i].Level = lvl;
                    }
                    UpdateUI();
                    RebuildShop();
                }
            }
            catch { }
        }

        private void ParseProgressLines(string[] lines)
        {
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq);
                string val = line.Substring(eq + 1);

                if (key == "Score" && long.TryParse(val, out long s)) score = s;
                else if (key == "ClickPower" && long.TryParse(val, out long c)) clickPower = c;
                else if (key == "AutoLevel" && long.TryParse(val, out long a)) autoClickerLevel = a;
                else if (key.StartsWith("Upgrade") && key.EndsWith("_Level"))
                {
                    string mid = key.Substring("Upgrade".Length);
                    mid = mid.Substring(0, mid.Length - "_Level".Length);
                    if (int.TryParse(mid, out int idx) && idx >= 0 && idx < upgrades.Count
                        && int.TryParse(val, out int lvl) && lvl > 0)
                    {
                        upgrades[idx].Level = lvl;
                    }
                }
            }
        }

        private Image MakeCircularImage(Image sourceImage, int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                using (GraphicsPath path = new GraphicsPath())
                {
                    path.AddEllipse(0, 0, size, size);
                    g.SetClip(path);
                    g.DrawImage(sourceImage, new Rectangle(0, 0, size, size));
                }
            }
            return bmp;
        }

        private Image CreatePlaceholderCircle(int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (SolidBrush b = new SolidBrush(ThemeManager.PanelAlt))
                    g.FillEllipse(b, 0, 0, size - 1, size - 1);
                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (Font f = new Font("Segoe UI", 12, FontStyle.Bold))
                using (SolidBrush t = new SolidBrush(ThemeManager.Success))
                    g.DrawString("No image", f, t, new RectangleF(0, 0, size, size), sf);
            }
            return bmp;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ThemeManager.ThemeChanged -= ApplyTheme;
                Loc.LanguageChanged -= ApplyLanguage;
                try { autoClickerTimer?.Stop(); autoClickerTimer?.Dispose(); } catch { }
                try { autoSaveTimer?.Stop(); autoSaveTimer?.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }
}