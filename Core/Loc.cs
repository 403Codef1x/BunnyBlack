// language: C#, file: Core/Loc.cs
// Локализация RU/EN. Словарь key → {ru, en}.
using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace BunnyBlack.Core
{
    public enum AppLang { RU, EN }

    public static class Loc
    {
        private static AppLang _current = AppLang.RU;

        public static event Action LanguageChanged;

        public static AppLang Current
        {
            get => _current;
            set
            {
                if (_current == value) return;
                _current = value;
                Save();
                LanguageChanged?.Invoke();
            }
        }

        private static readonly Dictionary<string, (string ru, string en)> S =
            new Dictionary<string, (string, string)>
        {
            // ===== Главное окно / навигация =====
            { "app.title",              ("Bunny Black", "Bunny Black") },
            { "app.subtitle",           ("System Recovery", "System Recovery") },
            { "nav.autostart",          ("Автозагрузка", "Autostart") },
            { "nav.scanner",            ("Сканер", "Scanner") },
            { "nav.users",              ("Пользователи", "Users") },
            { "nav.tools",              ("Доп.Возможности", "Tools") },
            { "nav.builtin",            ("Встроенные программы", "Built-in Apps") },
            { "nav.clicker",            ("Кликер", "Clicker") },
            { "nav.settings",           ("Настройки", "Settings") },

            // ===== Баннер =====
            { "banner.winre",           ("WinRE — оффлайн-режим. Изменения применяются к системе на диске {0}\\", "WinRE — offline mode. Changes apply to system on drive {0}\\") },
            { "banner.online",          ("●  Online — работа с текущей системой", "●  Online — working with current system") },

            // ===== Общие =====
            { "status.page",            ("Страница: {0}", "Page: {0}") },
            { "status.loading",         ("Загрузка…", "Loading…") },
            { "btn.ok",                 ("ОК", "OK") },
            { "btn.cancel",             ("Отмена", "Cancel") },
            { "btn.refresh",            ("Обновить", "Refresh") },
            { "btn.delete",             ("Удалить", "Delete") },
            { "btn.edit",               ("Изменить", "Edit") },
            { "btn.create",             ("Создать", "Create") },
            { "btn.save",               ("Сохранить", "Save") },
            { "btn.close",              ("Закрыть", "Close") },
            { "btn.open",               ("Открыть", "Open") },
            { "btn.run",                ("Выполнить", "Run") },
            { "btn.export",             ("Экспорт", "Export") },
            { "btn.import",             ("Импорт", "Import") },
            { "btn.reset",              ("Сбросить", "Reset") },
            { "btn.copy",               ("Копировать", "Copy") },
            { "btn.rename",             ("Переименовать", "Rename") },
            { "btn.move",               ("Переместить", "Move") },
            { "btn.yes",                ("Да", "Yes") },
            { "btn.no",                 ("Нет", "No") },
            { "warn.confirm",           ("Подтверждение", "Confirm") },
            { "warn.irreversible",      ("Действие необратимо.", "Action is irreversible.") },

            // ===== Автозагрузка =====
            { "autostart.title",        ("Автозагрузка", "Autostart") },
            { "autostart.filter",       ("Фильтр...", "Filter...") },
            { "autostart.col.name",     ("Name", "Name") },
            { "autostart.col.value",    ("Value", "Value") },
            { "autostart.col.exists",   ("Файл", "File") },
            { "autostart.col.signed",   ("Подпись", "Signed") },
            { "autostart.col.created",  ("Создан", "Created") },
            { "autostart.col.modified", ("Изменён", "Modified") },
            { "autostart.col.age",      ("Возраст", "Age") },
            { "autostart.col.status",   ("Статус", "Status") },
            { "autostart.menu.edit",    ("Изменить значение", "Edit value") },
            { "autostart.menu.delete",  ("Удалить", "Delete") },
            { "autostart.menu.location",("Открыть расположение файла", "Open file location") },
            { "autostart.menu.regedit", ("Открыть в редакторе реестра", "Open in regedit") },
            { "autostart.menu.copyfull",("Копировать полный путь", "Copy full path") },
            { "autostart.menu.copyval", ("Копировать Value", "Copy value") },
            { "autostart.menu.quar",    ("В карантин (если файл)", "Quarantine (if file)") },

            // ===== Сканер =====
            { "scan.title",             ("Сканирование системы", "System Scan") },
            { "scan.autofix",           ("Автоматически исправлять найденные нарушения", "Auto-fix found violations") },
            { "scan.start",             ("Начать сканирование", "Start Scan") },
            { "scan.fixall",            ("Исправить всё", "Fix All") },
            { "scan.found",             ("Найдено угроз: {0}", "Threats found: {0}") },
            { "scan.done",              ("Сканирование завершено.", "Scan complete.") },

            // ===== Пользователи =====
            { "users.title",            ("Пользователи", "Users") },
            { "users.col.name",         ("Имя", "Name") },
            { "users.col.status",       ("Статус", "Status") },
            { "users.enabled",          ("Включён", "Enabled") },
            { "users.disabled",         ("Отключён", "Disabled") },
            { "users.change",           ("Сменить пароль", "Change Password") },
            { "users.toggle",           ("Вкл/Выкл", "On/Off") },

            // ===== Настройки =====
            { "settings.title",         ("Настройки", "Settings") },
            { "settings.group.window",  ("Окно", "Window") },
            { "settings.tray",          ("Сворачивать в системный трей", "Minimize to tray") },
            { "settings.topmost",       ("Поверх всех окон", "Always on top") },
            { "settings.group.theme",   ("Тема и язык", "Theme and Language") },
            { "settings.theme",         ("Тема", "Theme") },
            { "settings.theme.dark",    ("Тёмная", "Dark") },
            { "settings.theme.light",   ("Светлая", "Light") },
            { "settings.lang",          ("Язык", "Language") },
            { "settings.lang.ru",       ("Русский", "Russian") },
            { "settings.lang.en",       ("English", "English") },
            { "settings.group.startup", ("Автозагрузка программы", "Program Autostart") },
            { "settings.startup.add",   ("Добавить в автозагрузку", "Add to autostart") },
            { "settings.startup.remove",("Убрать из автозагрузки", "Remove from autostart") },
            { "settings.group.updates", ("Обновления", "Updates") },
            { "settings.save",          ("Сохранить настройки", "Save settings") },
            { "settings.saved",         ("Настройки сохранены", "Settings saved") },
            { "settings.updates.check", ("Проверить обновления", "Check for updates") },

            // ===== Встроенные программы =====
            { "builtin.title",          ("Встроенные программы", "Built-in Apps") },
            { "builtin.tasks",          ("Диспетчер задач", "Task Manager") },
            { "builtin.files",          ("Проводник", "File Manager") },
            { "builtin.power",          ("Питание", "Power") },

            // ===== Кликер =====
            { "clicker.title",          ("Кликер", "Clicker") },
            { "clicker.shop",           ("Магазин улучшений", "Upgrade Shop") },
            { "clicker.clicks",         ("Кликов: {0}", "Clicks: {0}") },
            { "clicker.power",          ("Сила клика: {0}", "Click power: {0}") },
            { "clicker.auto",           ("Авто-кликер: {0} / сек", "Auto-clicker: {0} / sec") },

            // ===== Кликер — апгрейды =====
            { "clicker.up.strong",      ("Крепкий палец", "Strong finger") },
            { "clicker.up.fast",        ("Быстрый палец", "Fast finger") },
            { "clicker.up.steel",       ("Стальной палец", "Steel finger") },
            { "clicker.up.auto1",       ("Авто-кликер I", "Auto-clicker I") },
            { "clicker.up.auto2",       ("Авто-кликер II", "Auto-clicker II") },
            { "clicker.up.auto3",       ("Авто-кликер III", "Auto-clicker III") },
            { "clicker.up.golden",      ("Золотой палец", "Golden finger") },
            { "clicker.up.giant",       ("Гигантский палец", "Giant finger") },
            { "clicker.up.turbo",       ("Турбо-кликер", "Turbo-clicker") },
            { "clicker.up.legendary",   ("Легендарный палец", "Legendary finger") },

            // ===== Кликер — описания апгрейдов =====
            { "clicker.up.desc.plus1",  ("+1 к клику", "+1 click") },
            { "clicker.up.desc.plus5",  ("+5 к клику", "+5 click") },
            { "clicker.up.desc.plus15", ("+15 к клику", "+15 click") },
            { "clicker.up.desc.plus50", ("+50 к клику", "+50 click") },
            { "clicker.up.desc.plus150",("+150 к клику", "+150 click") },
            { "clicker.up.desc.plus500",("+500 к клику", "+500 click") },
            { "clicker.up.desc.auto1",  ("+1 клик/сек", "+1/sec") },
            { "clicker.up.desc.auto5",  ("+5 кликов/сек", "+5/sec") },
            { "clicker.up.desc.auto15", ("+15 кликов/сек", "+15/sec") },
            { "clicker.up.desc.auto50", ("+50 кликов/сек", "+50/sec") },
        };

        public static string T(string key)
        {
            if (S.TryGetValue(key, out var pair))
                return _current == AppLang.RU ? pair.ru : pair.en;
            return key;
        }

        public static string T(string key, params object[] args)
        {
            string s = T(key);
            try { return string.Format(s, args); }
            catch { return s; }
        }

        public static void Save()
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\BunnyBlack\Settings"))
                    k?.SetValue("Lang", _current == AppLang.RU ? "ru" : "en");
            }
            catch { }
        }

        public static void Load()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\BunnyBlack\Settings"))
                {
                    if (k == null) return;
                    string s = k.GetValue("Lang")?.ToString() ?? "ru";
                    _current = (s == "en") ? AppLang.EN : AppLang.RU;
                }
            }
            catch { }
        }
    }
}