// language: C#, file: Forms/ClickerForm.cs
// Полная замена.
// п.15 — прогресс хранится в Clicker.dat рядом с exe (едет с флешкой) + дублирование в реестр.
// п.18 — автосохранение раз в 15 сек и при кликах.
// Сохранены все апгрейды, картинки, авто-кликер, анимация.
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

        private long score = 0;
        private long clickPower = 1;
        private long autoClickerLevel = 0;
        private bool isWinRE;

        private Timer autoClickerTimer;
        private Timer autoSaveTimer;

        private class Upgrade
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public long Cost { get; set; }
            public long BaseCost { get; set; }
            public int Level { get; set; } = 0;
            public Action OnBuy { get; set; }
        }

        private List<Upgrade> upgrades = new List<Upgrade>();

        public ClickerForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);
            InitializeComponent();
            LoadProgress();
            StartAutoClicker();
            StartAutoSave();
        }

        // ============================================================
        // п.15 — путь к файлу прогресса рядом с exe
        // ============================================================
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
                BackColor = Color.FromArgb(13, 13, 13)
            };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var title = new Label
            {
                Text = "Кликер",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            mainLayout.Controls.Add(title, 0, 0);

            // п.15 — заголовок магазина + кнопки экспорт/импорт
            var shopHeader = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Color.Transparent,
                WrapContents = false
            };

            var shopTitle = new Label
            {
                Text = "Магазин улучшений",
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 12, 20, 0)
            };
            shopHeader.Controls.Add(shopTitle);

            var exportBtn = new Button
            {
                Text = "Экспорт",
                Height = 28,
                Width = 90,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(200, 200, 200),
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) },
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 12, 6, 0),
                Font = new Font("Segoe UI", 9)
            };
            exportBtn.Click += (s, e) =>
            {
                SaveProgress();
                NedoMessageBox.Show($"Прогресс сохранён:\n{ProgressFilePath}");
            };
            shopHeader.Controls.Add(exportBtn);

            var importBtn = new Button
            {
                Text = "Импорт",
                Height = 28,
                Width = 90,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(200, 200, 200),
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) },
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 12, 0, 0),
                Font = new Font("Segoe UI", 9)
            };
            importBtn.Click += (s, e) =>
            {
                LoadProgress();
                NedoMessageBox.Show("Прогресс загружен");
            };
            shopHeader.Controls.Add(importBtn);

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
                Text = "Кликов: 0",
                Font = new Font("Segoe UI", 22, FontStyle.Bold),
                ForeColor = Color.FromArgb(136, 221, 170),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleCenter
            };
            clickerLayout.Controls.Add(scoreLabel, 0, 0);

            clickPowerLabel = new Label
            {
                Text = "Сила клика: 1",
                Font = new Font("Segoe UI", 12),
                ForeColor = Color.FromArgb(170, 170, 170),
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
            catch
            {
                clickerPicture.Image = CreatePlaceholderCircle(320);
            }

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
                Text = "Авто-кликер: 0 / сек",
                Font = new Font("Segoe UI", 12),
                ForeColor = Color.FromArgb(136, 221, 170),
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

            // ============================================================
            // АПГРЕЙДЫ — без изменений
            // ============================================================
            upgrades.Clear();

            upgrades.Add(new Upgrade { Name = "Крепкий палец", Description = "+1 к силе клика", Cost = 10, BaseCost = 10, OnBuy = () => { clickPower += 1; } });
            upgrades.Add(new Upgrade { Name = "Быстрый палец", Description = "+5 к силе клика", Cost = 60, BaseCost = 60, OnBuy = () => { clickPower += 5; } });
            upgrades.Add(new Upgrade { Name = "Стальной палец", Description = "+15 к силе клика", Cost = 200, BaseCost = 200, OnBuy = () => { clickPower += 15; } });
            upgrades.Add(new Upgrade { Name = "Авто-кликер I", Description = "+1 клик/сек", Cost = 50, BaseCost = 50, OnBuy = () => { autoClickerLevel += 1; } });
            upgrades.Add(new Upgrade { Name = "Авто-кликер II", Description = "+5 кликов/сек", Cost = 250, BaseCost = 250, OnBuy = () => { autoClickerLevel += 5; } });
            upgrades.Add(new Upgrade { Name = "Авто-кликер III", Description = "+15 кликов/сек", Cost = 800, BaseCost = 800, OnBuy = () => { autoClickerLevel += 15; } });
            upgrades.Add(new Upgrade { Name = "Золотой палец", Description = "+50 к силе клика", Cost = 1000, BaseCost = 1000, OnBuy = () => { clickPower += 50; } });
            upgrades.Add(new Upgrade { Name = "Гигантский палец", Description = "+150 к силе клика", Cost = 4000, BaseCost = 4000, OnBuy = () => { clickPower += 150; } });
            upgrades.Add(new Upgrade { Name = "Турбо-кликер", Description = "+50 кликов/сек", Cost = 3000, BaseCost = 3000, OnBuy = () => { autoClickerLevel += 50; } });
            upgrades.Add(new Upgrade { Name = "Легендарный палец", Description = "+500 к силе клика", Cost = 10000, BaseCost = 10000, OnBuy = () => { clickPower += 500; } });

            foreach (var upgrade in upgrades)
            {
                Panel card = CreateShopCard(upgrade);
                shopPanel.Controls.Add(card);
            }

            mainLayout.Controls.Add(shopPanel, 1, 1);
            this.Controls.Add(mainLayout);
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
            Panel card = new Panel
            {
                Height = 70,
                Width = 340,
                BackColor = Color.FromArgb(22, 22, 22),
                Margin = new Padding(0, 0, 0, 8)
            };

            card.Paint += (s, e) =>
            {
                ControlPaint.DrawBorder(e.Graphics, card.ClientRectangle,
                    Color.FromArgb(45, 45, 45), 1, ButtonBorderStyle.Solid,
                    Color.FromArgb(45, 45, 45), 1, ButtonBorderStyle.Solid,
                    Color.FromArgb(45, 45, 45), 1, ButtonBorderStyle.Solid,
                    Color.FromArgb(45, 45, 45), 1, ButtonBorderStyle.Solid);
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

            var nameLabel = new Label
            {
                Text = upgrade.Name,
                Font = new Font("Segoe UI", 11, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            layout.Controls.Add(nameLabel, 0, 0);

            var descLabel = new Label
            {
                Text = upgrade.Description,
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(150, 150, 150),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
            layout.Controls.Add(descLabel, 0, 1);

            var buyBtn = new Button
            {
                Text = $"{upgrade.Cost}",
                Height = 40,
                Width = 100,
                BackColor = Color.FromArgb(30, 55, 40),
                ForeColor = Color.FromArgb(136, 221, 170),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(51, 102, 68) },
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
                else
                {
                    NedoMessageBox.Show("Недостаточно кликов!", isError: true);
                }
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

            scoreLabel.Text = $"Кликов: {score}";
            clickPowerLabel.Text = $"Сила клика: {clickPower}";
            autoClickerLabel.Text = $"Авто-кликер: {autoClickerLevel} / сек";
        }

        private void AnimateClick()
        {
            var originalLocation = clickerPicture.Location;
            clickerPicture.Location = new Point(originalLocation.X + 3, originalLocation.Y + 3);
            var t = new Timer { Interval = 50 };
            t.Tick += (ts, te) =>
            {
                clickerPicture.Location = originalLocation;
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

        // ============================================================
        // п.15 — SAVE: файл рядом с exe + дублирование в реестр
        // ============================================================
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

                try { File.WriteAllText(ProgressFilePath, sb.ToString()); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SaveProgress/file] {ex.Message}"); }

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
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[SaveProgress/reg] {ex.Message}"); }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveProgress] {ex.Message}");
            }
        }

        // ============================================================
        // п.15 — LOAD: сначала файл, если нет — реестр
        // ============================================================
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
                        return;
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[LoadProgress/file] {ex.Message}"); }
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
                        {
                            upgrades[i].Level = lvl;
                            upgrades[i].Cost = upgrades[i].BaseCost;
                            for (int j = 0; j < lvl; j++)
                                upgrades[i].Cost = (long)(upgrades[i].Cost * 1.5);
                        }
                    }
                    UpdateUI();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadProgress] {ex.Message}");
            }
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
                        upgrades[idx].Cost = upgrades[idx].BaseCost;
                        for (int j = 0; j < lvl; j++)
                            upgrades[idx].Cost = (long)(upgrades[idx].Cost * 1.5);
                    }
                }
            }
        }

        private Image MakeCircularImage(Image sourceImage, int size)
        {
            Bitmap squareBitmap = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(squareBitmap))
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
            return squareBitmap;
        }

        private Image CreatePlaceholderCircle(int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(22, 22, 22)))
                    g.FillEllipse(bgBrush, 0, 0, size - 1, size - 1);

                using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (Font font = new Font("Segoe UI", 12, FontStyle.Bold))
                using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(136, 221, 170)))
                    g.DrawString("Нет картинки", font, textBrush, new RectangleF(0, 0, size, size), sf);
            }
            return bmp;
        }
    }
}