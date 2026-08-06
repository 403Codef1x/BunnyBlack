using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BunnyBlack.Forms
{
    public partial class ToolsForm : UserControl
    {
        private bool isWinRE;

        public ToolsForm(bool winRE)
        {
            isWinRE = winRE;
            this.BackColor = Color.FromArgb(13, 13, 13);
            this.ForeColor = Color.FromArgb(216, 216, 216);
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Dock = DockStyle.Fill;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(28, 24, 28, 20),
                BackColor = Color.FromArgb(13, 13, 13)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var title = new Label
            {
                Text = "Дополнительные возможности" + (isWinRE ? " (WinRE)" : ""),
                Font = new Font("Segoe UI", 18, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            layout.Controls.Add(title, 0, 0);

            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Color.Transparent
            };

            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 0, 0, 10)
            };

            var tools = new (string name, string desc, Action action)[]
            {
                ("Выйти из пользователя", "Завершает сеанс текущего пользователя", LogOff),
                ("Войти в WinRE", "Перезагружает компьютер в среду восстановления", EnterWinRE),
                ("Выполнить", "Открывает окно 'Выполнить'", RunDialog),
                ("Вернуть русский язык", "Восстанавливает русскую раскладку клавиатуры", RestoreRussianKeyboard),
                ("Починить шрифты", "Восстанавливает системные шрифты", RestoreFonts),
                ("Вернуть стандартную тему", "Восстанавливает стандартную тему Windows", RestoreDefaultTheme),
                ("Включить UAC", "Включает контроль учётных записей", EnableUAC),
                ("sfc /scannow", "Проверяет целостность системных файлов", RunSfcScannow),
                ("Заменить sethc и utilman", "Заменяет sethc.exe и utilman.exe на BunnyBlack (Только WinRE)", ReplaceSethcUtilman),
            };

            foreach (var tool in tools)
            {
                var panel = CreateToolPanel(tool.name, tool.desc, tool.action);
                flow.Controls.Add(panel);
            }

            scroll.Controls.Add(flow);
            layout.Controls.Add(scroll, 0, 1);
            this.Controls.Add(layout);
        }

        private Panel CreateToolPanel(string name, string desc, Action action)
        {
            var panel = new Panel
            {
                Height = 65,
                Width = 850,
                BackColor = Color.FromArgb(22, 22, 22),
                Margin = new Padding(0, 0, 0, 8)
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(16, 8, 16, 8)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

            var info = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1,
                BackColor = Color.Transparent
            };
            info.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            info.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));

            var nameLabel = new Label
            {
                Text = name,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            info.Controls.Add(nameLabel, 0, 0);

            var descLabel = new Label
            {
                Text = desc,
                Font = new Font("Segoe UI", 10),
                ForeColor = Color.FromArgb(102, 102, 102),
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };
            info.Controls.Add(descLabel, 0, 1);

            layout.Controls.Add(info, 0, 0);

            var btnContainer = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 130,
                Height = 38,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 0, 0, 0),
                Margin = new Padding(0, 0, 0, 0)
            };

            var btn = new Button
            {
                Text = "Выполнить",
                Height = 38,
                Width = 130,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.FromArgb(240, 240, 240),
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 1, BorderColor = Color.FromArgb(60, 60, 60) },
                Cursor = Cursors.Hand,
                Font = new Font("Segoe UI", 11),
                UseVisualStyleBackColor = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = new Padding(0, 0, 0, 0),
                Margin = new Padding(0, 0, 0, 0)
            };
            btn.Click += (s, e) => ExecuteAction(action, name);

            btnContainer.Controls.Add(btn);
            layout.Controls.Add(btnContainer, 1, 0);

            panel.Controls.Add(layout);
            return panel;
        }

        private void ExecuteAction(Action action, string toolName)
        {
            try
            {
                action();
                MessageBox.Show($"✅ {toolName} выполнено!", "Успешно",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Ошибка: {ex.Message}", "Ошибка",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RunCmd(string command)
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c " + command,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        process.WaitForExit();
                        if (process.ExitCode != 0)
                        {
                            string error = process.StandardError.ReadToEnd();
                            if (!string.IsNullOrEmpty(error))
                                throw new Exception(error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка выполнения: {ex.Message}");
            }
        }

        private void RunCmdVisible(string command)
        {
            try
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/k " + command,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    Verb = "runas"
                };

                System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка: {ex.Message}");
            }
        }

        // ============================================================
        // 1. ВЫЙТИ ИЗ ПОЛЬЗОВАТЕЛЯ
        // ============================================================
        private void LogOff()
        {
            if (MessageBox.Show("Завершить сеанс текущего пользователя?", "Подтверждение",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                RunCmd("shutdown /l");
            }
        }

        // ============================================================
        // 2. ВОЙТИ В WINRE
        // ============================================================
        private void EnterWinRE()
        {
            if (MessageBox.Show("Перезагрузить компьютер в среду восстановления?", "Подтверждение",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                RunCmd("shutdown /r /o /t 0");
            }
        }

        // ============================================================
        // 3. ВЫПОЛНИТЬ
        // ============================================================
        private void RunDialog()
        {
            RunCmd("explorer.exe shell:::{2559a1f3-21d7-11d4-bdaf-00c04f60b9f0}");
        }

        // ============================================================
        // 4. ВЕРНУТЬ РУССКИЙ ЯЗЫК
        // ============================================================
        private void RestoreRussianKeyboard()
        {
            RunCmd("reg add \"HKCU\\Control Panel\\International\\User Profile\" /v \"KeyboardLayout\" /t REG_SZ /d \"00000409,00000419\" /f");
            MessageBox.Show("Русская раскладка добавлена. Выйдите из системы и зайдите обратно.",
                "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ============================================================
        // 5. ПОЧИНИТЬ ШРИФТЫ
        // ============================================================
        private void RestoreFonts()
        {
            RunCmd("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Fonts\" /v \"Segoe UI (TrueType)\" /t REG_SZ /d \"segoeui.ttf\" /f");
            RunCmd("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Fonts\" /v \"Segoe UI Bold (TrueType)\" /t REG_SZ /d \"segoeuib.ttf\" /f");
            RunCmd("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Fonts\" /v \"Segoe UI Italic (TrueType)\" /t REG_SZ /d \"segoeuii.ttf\" /f");
            RunCmd("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Fonts\" /v \"Segoe UI Bold Italic (TrueType)\" /t REG_SZ /d \"segoeuiz.ttf\" /f");
            MessageBox.Show("Шрифты восстановлены. Требуется перезагрузка.",
                "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ============================================================
        // 6. ВЕРНУТЬ СТАНДАРТНУЮ ТЕМУ
        // ============================================================
        private void RestoreDefaultTheme()
        {
            RunCmd("reg add \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\" /v \"CurrentTheme\" /t REG_SZ /d \"C:\\Windows\\resources\\Themes\\aero.theme\" /f");
            RunCmd("reg add \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\" /v \"ThemeChangesDesktopIcons\" /t REG_DWORD /d 1 /f");
            RunCmd("reg add \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\" /v \"ThemeChangesMousePointers\" /t REG_DWORD /d 1 /f");
            RunCmd("reg add \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\" /v \"ThemeChangesSounds\" /t REG_DWORD /d 1 /f");
            MessageBox.Show("Стандартная тема восстановлена.",
                "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ============================================================
        // 7. ВКЛЮЧИТЬ UAC
        // ============================================================
        private void EnableUAC()
        {
            RunCmd("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v \"EnableLUA\" /t REG_DWORD /d 1 /f");
            RunCmd("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v \"ConsentPromptBehaviorAdmin\" /t REG_DWORD /d 5 /f");
            RunCmd("reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v \"PromptOnSecureDesktop\" /t REG_DWORD /d 1 /f");
            MessageBox.Show("UAC включен. Требуется перезагрузка.",
                "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ============================================================
        // 8. SFC /SCANNOW
        // ============================================================
        private void RunSfcScannow()
        {
            string command;
            if (isWinRE)
                command = "sfc /scannow /offbootdir=C:\\ /offwindir=C:\\Windows";
            else
                command = "sfc /scannow";

            RunCmdVisible(command);
        }

        // ============================================================
        // 9. ЗАМЕНИТЬ SETHC И UTILMAN (ИСПРАВЛЕННЫЙ ВАРИАНТ)
        // ============================================================
        private void ReplaceSethcUtilman()
        {
            try
            {
                // === ГЛАВНАЯ ПРОВЕРКА: РАБОТАЕТ ТОЛЬКО В WinRE ===
                if (!isWinRE)
                {
                    throw new Exception("Эта функция может работать ТОЛЬКО в среде восстановления WinRE!\n" +
                        "Пожалуйста, перезагрузите компьютер и загрузитесь с флешки (или нажмите 'Войти в WinRE').");
                }

                if (!IsAdministrator())
                {
                    throw new Exception("Для замены файлов требуются права администратора!\nПерезапустите программу от имени администратора.");
                }

                // === АВТОМАТИЧЕСКОЕ ОПРЕДЕЛЕНИЕ ДИСКА С СИСТЕМОЙ В WinRE ===
                // В WinRE путь к папке Windows чаще всего D:\Windows, E:\Windows и т.д.
                // Мы найдем его, перебрав все доступные буквы.
                string windowsPath = FindWindowsDirectoryInWinRE();

                if (string.IsNullOrEmpty(windowsPath))
                {
                    throw new Exception("Не удалось автоматически найти папку Windows на вашем диске. Попробуйте указать букву диска вручную.");
                }

                string system32 = Path.Combine(windowsPath, "System32");

                string exePath = Application.ExecutablePath;

                string sethcPath = Path.Combine(system32, "sethc.exe");
                string utilmanPath = Path.Combine(system32, "utilman.exe");
                string sethcBak = Path.Combine(system32, "sethc.exe.bak");
                string utilmanBak = Path.Combine(system32, "utilman.exe.bak");
                string tempSethc = Path.Combine(Path.GetTempPath(), "sethc_temp.exe");
                string tempUtilman = Path.Combine(Path.GetTempPath(), "utilman_temp.exe");

                // Копируем нашу программу во временные файлы
                File.Copy(exePath, tempSethc, true);
                File.Copy(exePath, tempUtilman, true);

                // СОЗДАЁМ BAT-ФАЙЛ (В нём уже нет жестких путей к C:\, используются переменные)
                string batPath = Path.Combine(Path.GetTempPath(), "replace_sethc.bat");
                File.WriteAllText(batPath, $@"
@echo off
title Замена sethc.exe и utilman.exe
echo ========================================
echo   Замена sethc.exe и utilman.exe
echo ========================================
echo.

echo [1] Остановка защиты файлов...
takeown /f ""{sethcPath}"" >nul 2>&1
icacls ""{sethcPath}"" /grant Administrators:F >nul 2>&1
takeown /f ""{utilmanPath}"" >nul 2>&1
icacls ""{utilmanPath}"" /grant Administrators:F >nul 2>&1

echo [2] Снятие атрибутов...
attrib -r -s -h ""{sethcPath}"" >nul 2>&1
attrib -r -s -h ""{utilmanPath}"" >nul 2>&1

echo [3] Сохранение оригиналов...
if not exist ""{sethcBak}"" copy ""{sethcPath}"" ""{sethcBak}""
if not exist ""{utilmanBak}"" copy ""{utilmanPath}"" ""{utilmanBak}""

echo [4] Замена файлов...
copy ""{tempSethc}"" ""{sethcPath}"" /y
copy ""{tempUtilman}"" ""{utilmanPath}"" /y

echo.
echo ========================================
echo   ✅ Замена выполнена успешно!
echo   Теперь при 5 Shift или Win+U запускается Bunny Black
echo ========================================
echo.
pause
");

                // ЗАПУСКАЕМ BAT-ФАЙЛ
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = batPath,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    Verb = "runas"
                };

                var process = System.Diagnostics.Process.Start(startInfo);
                if (process != null)
                {
                    process.WaitForExit();
                }

                try { File.Delete(batPath); } catch { }
                try { File.Delete(tempSethc); } catch { }
                try { File.Delete(tempUtilman); } catch { }

                MessageBox.Show("✅ Замена выполнена успешно!\n\n" +
                    "Теперь при нажатии 5 раз Shift (sethc) или Win+U (utilman)\n" +
                    "будет запускаться Bunny Black.\n\n" +
                    "Оригиналы сохранены как:\n" +
                    $"• {sethcBak}\n" +
                    $"• {utilmanBak}\n\n" +
                    "⚠️ ВНИМАНИЕ! Чтобы восстановить оригиналы:\n" +
                    "1. Откройте командную строку от имени администратора\n" +
                    "2. Перейдите в папку System32 и выполните:\n" +
                    "   copy sethc.exe.bak sethc.exe\n" +
                    "   copy utilman.exe.bak utilman.exe",
                    "Успешно", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (Exception ex)
            {
                throw new Exception($"Не удалось заменить файлы: {ex.Message}");
            }
        }

        // ============================================================
        // ВСПОМОГАТЕЛЬНАЯ ФУНКЦИЯ: Поиск диска с Windows в среде WinRE
        // ============================================================
        private string FindWindowsDirectoryInWinRE()
        {
            // В WinRE проверяем все логические диски
            string[] drives = Directory.GetLogicalDrives();
            foreach (string drive in drives)
            {
                try
                {
                    string testPath = Path.Combine(drive, "Windows", "System32");
                    if (Directory.Exists(testPath))
                    {
                        return Path.Combine(drive, "Windows");
                    }
                }
                catch { /* Игнорируем ошибки доступа к дискам, пробуем следующий */ }
            }
            return null; // Не найдено
        }

        private bool IsAdministrator()
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }
}