using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Proxy;
using MimeKit;

namespace opentask
{
    public class ContactsScanner
    {
        private string accountsFile;
        private string outputFile;
        private ProxyManager proxyManager;
        private int maxThreads;
        private int connectTimeout;
        private bool proxyEnabled;
        
        private ConcurrentHashSet<string> allContacts = new ConcurrentHashSet<string>();
        private int totalAccounts = 0;
        private int processedAccounts = 0;
        private DateTime scanStartTime;
        private CancellationTokenSource? cancellationTokenSource;
        private bool isScanning = false;
        private readonly object _fileLock = new object();

        public ContactsScanner(string accountsPath, string outputPath, ProxyManager proxies, int threads, int timeout, bool useProxies)
        {
            accountsFile = accountsPath;
            outputFile = outputPath;
            proxyManager = proxies;
            maxThreads = threads;
            connectTimeout = timeout;
            proxyEnabled = useProxies;
        }

        public async Task StartScan()
        {
            if (!File.Exists(accountsFile))
            {
                UI.WriteLine("Файл accounts.txt не найден!", ConsoleColor.Red);
                UI.WaitKey("Нажмите любую клавишу для возврата...");
                return;
            }

            var accounts = File.ReadAllLines(accountsFile)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            totalAccounts = accounts.Count;
            processedAccounts = 0;
            isScanning = true;
            scanStartTime = DateTime.Now;
            cancellationTokenSource = new CancellationTokenSource();

            var progressTask = ShowScanProgress();
            
            var semaphore = new SemaphoreSlim(maxThreads);
            var tasks = accounts.Select(account => ProcessAccount(account, semaphore, cancellationTokenSource.Token)).ToList();
            
            await Task.WhenAll(tasks);
            
            isScanning = false;
            await progressTask;

            SaveAllContacts();

            Console.WriteLine($"\n✅ Сканер контактов завершил работу! Всего собрано уникальных контактов: {allContacts.Count}");
            UI.WaitKey("Нажмите любую клавишу для возврата в меню...");
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
                if (proxyEnabled)
                {
                    proxy = proxyManager.GetNextProxy();
                }

                var connectionResult = await TryConnectWithRetry(email, password, domain, proxy, ct);
                if (connectionResult.Success && connectionResult.Client != null)
                {
                    var contacts = await ExtractAllContactsFromMailbox(connectionResult.Client, ct);
                    // Исправлено: ConcurrentHashSet не имеет AddRange, добавляем каждый контакт отдельно
                    foreach (var contact in contacts)
                    {
                        allContacts.Add(contact);
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
            // Автоматически определяем IMAP сервер по домену почты через вспомогательный класс
            List<(string Server, int Port, bool UseSsl)> serversToTry = ImapHelper.GetImapServersForDomain(domain);
            if (serversToTry.Count == 0)
            {
                Console.WriteLine($"[FAIL] Не удалось найти IMAP сервер для домена {domain} для аккаунта {email}");
                return (false, null);
            }
            Console.WriteLine($"[DEBUG] Для домена {domain} будем пробовать {serversToTry.Count} сервер(ов)");

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
                    // Продолжаем пробовать другие серверы
                    continue;
                }
            }

            Console.WriteLine($"[FAIL] Не удалось подключиться к аккаунту {email} ни к одному из серверов");
            return (false, null);
        }

        private async Task<HashSet<string>> ExtractAllContactsFromMailbox(ImapClient client, CancellationToken ct)
        {
            var contacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            try
            {
                var allFolders = new List<IMailFolder>();
                // Используем расширенный список всех возможных папок как основной способ, чтобы гарантировать сканирование всех папок
                // Это решает проблемы с несовместимостью API разных версий MailKit
                allFolders.Add(client.Inbox);
                // Расширенный список всех возможных папок на разных языках
                var allPossibleFolders = new List<string> { 
                    "Sent", "Отправленные", "Sent Messages", "Отправленные сообщения",
                    "Archive", "Архив", "Archived", "Архивные",
                    "Drafts", "Черновики", "Draft", "Черновик",
                    "All Mail", "Вся почта", "Почта", "Все сообщения",
                    "Spam", "Спам", "Junk", "Нежелательная почта", "Спам сообщения",
                    "Trash", "Корзина", "Удаленные", "Deleted",
                    "Important", "Важные", "Starred", "С избранным",
                    "Snoozed", "Отложенные", "Outbox", "Исходящие",
                    "Work", "Работа", "Personal", "Личные", "Archive 2023", "Archive 2024" // пользовательские архивные папки
                };
                foreach (var folderName in allPossibleFolders)
                {
                    try {
                        var folder = client.GetFolder(folderName);
                        if (folder != null && folder.Exists && folder.Count > 0 && !allFolders.Contains(folder))
                        {
                            allFolders.Add(folder);
                            Console.WriteLine($"[EXTRACT] Добавлена папка для сканирования: {folderName} (сообщений: {folder.Count})");
                        }
                    } catch { continue; }
                }

                // Убрали дублирующий код запасного варианта, так как теперь основной код сканирует все папки

                Console.WriteLine($"[EXTRACT] Всего будет сканировано {allFolders.Count} папок в этом аккаунте");
                // Вместо цикла по importantFolders используем allFolders:
                foreach (var folder in allFolders)
                {
                    if (ct.IsCancellationRequested) break;
                    
                    try
                    {
                        if (!folder.IsOpen)
                            await folder.OpenAsync(FolderAccess.ReadOnly, ct);
                        
                        Console.WriteLine($"[EXTRACT] Сканируем папку '{folder.Name}', в ней {folder.Count} сообщений");
                        
                        if (folder.Count == 0) continue;

                        // Сканируем АБСОЛЮТНО ВСЕ сообщения в папке
                        var messageSummaries = await folder.FetchAsync(0, -1, MessageSummaryItems.Full | MessageSummaryItems.Body, ct);
                        
                        var extractedInFolder = 0;
                        var messageIndex = 0;
                        foreach (var msg in messageSummaries)
                        {
                            if (ct.IsCancellationRequested) 
                            {
                                Console.WriteLine("\n[EXTRACT] Сканер остановлен пользователем (ESC)");
                                break;
                            }
                            
                            try
                                  {
                                      // Сначала извлекаем контакты из Envelope который у нас уже есть в MessageSummary (это РАБОТАЕТ)
                                      if (msg.Envelope != null)
                                      {
                                          extractedInFolder += ExtractContactsFromEnvelope(msg.Envelope, contacts);
                                      }
                                      
                                      // Теперь загружаем ПОЛНОЕ сообщение ТОЛЬКО чтобы извлечь контакты из ТЕЛА (тело не в Envelope)
                                       int addedFromBody = 0;
                                       if (msg?.UniqueId != null)
                                       {
                                           // Для черновиков часто возникают ошибки с UID, поэтому оборачиваем в отдельный try/catch
                                           try
                                           {
                                               var fullMessage = await folder.GetMessageAsync(msg.UniqueId, ct);
                                               if (fullMessage != null)
                                               {
                                                   // Извлекаем контакты из ТЕЛА сообщения
                                                   var messageText = GetTextFromMimeMessage(fullMessage);
                                                   addedFromBody = ExtractEmailsFromText(messageText, contacts);
                                                   extractedInFolder += addedFromBody;
                                               }
                                           }
                                           catch (Exception ex) when (ex.Message.Contains("uid is invalid") || ex.Message.Contains("Invalid uid"))
                                           {
                                               // Тихо пропускаем черновики с невалидным UID - это нормальная ситуация для папки Drafts
                                               if (folder.Name is "Drafts" or "Черновики" && messageIndex < 20)
                                               {
                                                   // Логируем только первые несколько ошибок чтобы не засорять логи
                                                   Console.WriteLine($"[LOG] Пропущен черновик #{messageIndex} в папке {folder.Name} (невалидный UID)");
                                               }
                                           }
                                           catch (Exception ex)
                                           {
                                               // Для остальных папок логируем ошибку как раньше
                                               Console.WriteLine($"[LOG] ОШИБКА загрузки сообщения #{messageIndex} в папке {folder.Name}: {ex.Message}");
                                           }
                                       }
                                  
                                  if (messageIndex % 100 == 0 && addedFromBody > 0)
                                    Console.WriteLine($"[EXTRACT] Из сообщения #{messageIndex} в {folder.Name} извлечено {addedFromBody} контактов из тела");
                                    
                                messageIndex++;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[LOG] ОШИБКА обработки сообщения #{messageIndex} в папке {folder.Name}: {ex.Message}");
                                if (ex.InnerException != null)
                                    Console.WriteLine($"[LOG] Внутренняя ошибка: {ex.InnerException.Message}");
                                messageIndex++;
                                continue;
                            }
                        }
                        
                        Console.WriteLine($"[EXTRACT] Из папки '{folder.Name}' извлечено ВСЕГО {extractedInFolder} контактов");
                        await folder.CloseAsync(false, ct);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ERROR] Ошибка при сканировании папки {folder.Name}: {ex.Message}");
                        continue;
                    }
                }
                
                Console.WriteLine($"[EXTRACT] Всего извлечено уникальных контактов: {contacts.Count}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Общая ошибка извлечения контактов: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"[ERROR] Внутренняя ошибка: {ex.InnerException.Message}");
                }
            }

            return contacts;
        }

        private int ExtractContactsFromEnvelope(Envelope? envelope, HashSet<string> contacts)
        {
            var newContacts = 0;
            if (envelope == null) return newContacts;

            newContacts += AddAddressesIfValid(envelope.From, contacts);
            newContacts += AddAddressesIfValid(envelope.To, contacts);
            newContacts += AddAddressesIfValid(envelope.Cc, contacts);
            newContacts += AddAddressesIfValid(envelope.Bcc, contacts);
            
            return newContacts;
        }

        private int AddAddressesIfValid(InternetAddressList? addresses, HashSet<string> contacts)
        {
            var added = 0;
            if (addresses == null) return added;

            foreach (var addr in addresses)
            {
                if (addr is MailboxAddress mailboxAddr && 
                    !string.IsNullOrWhiteSpace(mailboxAddr.Address) && 
                    IsValidEmail(mailboxAddr.Address))
                {
                    if (contacts.Add(mailboxAddr.Address.ToLowerInvariant()))
                        added++;
                }
            }
            return added;
        }

        // Метод для парсинга email-адресов напрямую из текста (тела сообщения)
        private int ExtractEmailsFromText(string? text, HashSet<string> contacts)
        {
            var added = 0;
            if (string.IsNullOrWhiteSpace(text))
                return added;
            
            // Улучшенное регулярное выражение для поиска любых валидных email-адресов
            var emailRegex = new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled);
            var matches = emailRegex.Matches(text);
            
            foreach (Match match in matches)
            {
                string email = match.Value.ToLowerInvariant();
                if (IsValidEmail(email) && contacts.Add(email))
                {
                    added++;
                }
            }
            return added;
        }

        // Рекурсивный метод для получения всего текстового содержимого из MIME-структуры сообщения
        private string GetTextFromMimeMessage(MimeMessage message)
        {
            if (message == null)
                return string.Empty;
            
            var text = new StringBuilder();
            
            // Используем встроенный в MimeKit метод для получения текста из любого MIME-сообщения
            // Это гарантирует что мы корректно обработаем все типы контента включая HTML
            var textBody = message.TextBody;
            if (textBody != null)
            {
                text.AppendLine(textBody);
            }
            
            var htmlBody = message.HtmlBody;
            if (htmlBody != null)
            {
                // Просто добавляем сырой HTML текст (regex все равно найдет email в любом случае)
                text.AppendLine(htmlBody);
            }
            
            return text.ToString();
        }

        private bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            try
            {
                return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LOG] Ошибка валидации email: {ex.Message}");
                return false;
            }
        }

        private void SaveAllContacts()
        {
            lock (_fileLock)
            {
                try
                {
                    var contactsToSave = allContacts.Distinct().OrderBy(c => c).ToList();
                    Console.WriteLine($"[SAVE] Начинаю сохранение {contactsToSave.Count} контактов в файл {outputFile}...");
                    File.WriteAllLines(outputFile, contactsToSave);
                    Console.WriteLine($"[SAVE] ✅ Контакты успешно сохранены в {Path.GetFullPath(outputFile)}");
                    // Для проверки выведем первые 5 контактов в консоль
                    if (contactsToSave.Any())
                    {
                        Console.WriteLine("[SAVE] Первые 5 сохраненных контактов:");
                        foreach (var c in contactsToSave.Take(5))
                            Console.WriteLine($"  - {c}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] ❌ Ошибка при сохранении контактов в файл {outputFile}: {ex.Message}");
                    // Запасной вариант записи в текущую директорию с другим именем, если основной файл недоступен
                    try
                    {
                        var backupFile = $"backup_{outputFile}";
                        File.WriteAllLines(backupFile, allContacts.Distinct().OrderBy(c => c));
                        Console.WriteLine($"[SAVE] ✅ Контакты сохранены в резервный файл {Path.GetFullPath(backupFile)}");
                    }
                    catch { }
                }
            }
        }

        private async Task ShowScanProgress()
        {
            while (isScanning)
            {
                Console.Clear();
                UI.DrawHeader("📧 CONTACTS SCANNER", "Сбор всех контактов из аккаунтов", 80);
                
                var progress = totalAccounts > 0 ? (double)processedAccounts / totalAccounts * 100 : 0;
                var elapsed = DateTime.Now - scanStartTime;
                var overallSpeed = processedAccounts > 0 ? processedAccounts / elapsed.TotalSeconds : 0;
                
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"┌{new string('─', 78)}┐");
                Console.WriteLine($"│ Прогресс: {progress,5:F1}% │ Обработано: {processedAccounts,6:N0} из {totalAccounts,6:N0} │");
                Console.ResetColor();
                
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"│ Собрано контактов: {allContacts.Count,6:N0} │ Скорость: {overallSpeed,5:F1}/сек │");
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