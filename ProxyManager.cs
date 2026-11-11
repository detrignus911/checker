using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Imap;

namespace opentask
{
    public class ProxyManager
    {
        private readonly List<string> validSocks5Proxies = new List<string>();
        private readonly HashSet<string> deadProxies = new HashSet<string>();
        private int pointer = 0;
        private readonly object lockObj = new object();
        private string? selectedManualProxy;
        private List<string> _validAccounts = new List<string>();
        private Dictionary<string, DateTime> _proxyLastChecked = new Dictionary<string, DateTime>();
        public IReadOnlyDictionary<string, DateTime> ProxyCheckTimes => _proxyLastChecked.AsReadOnly();
        
        public void LoadValidAccounts(string accountsFile)
        {
            if (File.Exists(accountsFile))
            {
                _validAccounts = File.ReadAllLines(accountsFile)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList();
            }
        }

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

            int invalidCount = 0;
            var formatValidProxies = new List<string>();
            if (File.Exists(file))
            {
                foreach (var line in File.ReadAllLines(file))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    
                    if (ValidateSocks5Proxy(line, out var validProxy) && validProxy != null)
                    {
                        formatValidProxies.Add(validProxy);
                    }
                    else
                    {
                        invalidCount++;
                    }
                }
            }

            // Теперь ПРОВЕРЯЕМ КАЖДЫЙ ФОРМАЛЬНО ВАЛИДНЫЙ ПРОКСИ НА РАБОТОСПОСОБНОСТЬ!
            Console.WriteLine($"[PROXY_INFO] Найдено {formatValidProxies.Count} прокси с правильным форматом, начинаю проверку их работоспособности...");
            var workingProxies = new List<string>();
            
            // Используем асинхронные задачи вместо Parallel.ForEach для корректного ожидания всех проверок
            var checkTasks = new List<Task>();
            int maxParallelism = 10;
            var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);
            
            foreach (var proxy in formatValidProxies)
            {
                checkTasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var isWorking = await TestProxyAsync(proxy, 5);
                        if (isWorking)
                        {
                            lock (workingProxies) 
                            {
                                workingProxies.Add(proxy);
                            }
                            Console.WriteLine($"[PROXY_OK] ✅ Прокси работает: {proxy.Substring(0, 50)}...");
                        }
                        else
                        {
                            Interlocked.Increment(ref invalidCount);
                            Console.WriteLine($"[PROXY_FAIL] ❌ Прокси не работает (неверный пароль/хост/порт), отброшен: {proxy.Substring(0, 50)}...");
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }
            
            // Дожидаемся завершения всех задач проверки
            Task.WaitAll(checkTasks.ToArray());
            semaphore.Dispose();

            // Добавляем только РАБОЧИЕ прокси в основной список
            foreach (var wp in workingProxies.Distinct())
            {
                validSocks5Proxies.Add(wp);
            }

            Console.WriteLine($"[PROXY_INFO] Загружено {validSocks5Proxies.Count} РАБОЧИХ SOCKS5 прокси, отброшено {invalidCount} невалидных/неработающих");
        }

        private bool ValidateSocks5Proxy(string proxyLine, out string? validProxy)
        {
            validProxy = null;
            string rawProxy = proxyLine.Trim();
            
            Console.WriteLine($"[PROXY_DEBUG] Проверяю прокси: {rawProxy}");

            // 1. Удаляем префикс socks5:// если он есть
            if (rawProxy.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase))
            {
                rawProxy = rawProxy.Substring(9);
                Console.WriteLine($"[PROXY_DEBUG] Удален префикс socks5://, осталось: {rawProxy}");
            }
            // Если есть другой протокол — отклоняем
            else if (rawProxy.Contains("://"))
            {
                Console.WriteLine($"[PROXY_ERROR] Отклонен не-SOCKS5 прокси: {proxyLine} (только SOCKS5 поддерживаются)");
                return false;
            }

            // 2. Разделяем на часть с авторизацией и хост:порт (ищем ПОСЛЕДНИЙ символ @, потому что в логине могут быть @?)
            int lastAtIndex = rawProxy.LastIndexOf('@');
            if (lastAtIndex != -1)
            {
                string userPassPart = rawProxy.Substring(0, lastAtIndex);
                string hostPortPart = rawProxy.Substring(lastAtIndex + 1);
                
                Console.WriteLine($"[PROXY_DEBUG] Разделил на userPass={userPassPart}, hostPort={hostPortPart}");

                // Проверяем что в userPassPart есть разделитель : между логином и паролем
                if (!userPassPart.Contains(':'))
                {
                    Console.WriteLine($"[PROXY_ERROR] Отклонен невалидный прокси: {proxyLine} (отсутствует : между логином и паролем)");
                    return false;
                }

                // Разделяем логин и пароль (берем ПЕРВЫЙ :? НЕТ — берем ТОЛЬКО ПЕРВОЕ ВХОЖДЕНИЕ :, чтобы в пароле могли быть :
                var userPass = userPassPart.Split(new[] {':'}, 2);
                if (userPass.Length != 2 || string.IsNullOrWhiteSpace(userPass[0]) || string.IsNullOrWhiteSpace(userPass[1]))
                {
                    Console.WriteLine($"[PROXY_ERROR] Отклонен невалидный прокси: {proxyLine} (логин или пароль пустые)");
                    return false;
                }

                // Теперь парсим hostPortPart — ищем ПОСЛЕДНИЙ : чтобы отделить порт (поддерживает IPv6 с : в адресе)
                int lastColonInHostPort = hostPortPart.LastIndexOf(':');
                if (lastColonInHostPort == -1)
                {
                    Console.WriteLine($"[PROXY_ERROR] Отклонен невалидный прокси: {proxyLine} (не найден порт в hostPort части {hostPortPart})");
                    return false;
                }

                string host = hostPortPart.Substring(0, lastColonInHostPort);
                string portStr = hostPortPart.Substring(lastColonInHostPort + 1);
                
                if (!int.TryParse(portStr, out int port) || port < 1 || port > 65535)
                {
                    Console.WriteLine($"[PROXY_ERROR] Отклонен невалидный прокси: {proxyLine} (порт {portStr} невалидный)");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(host))
                {
                    Console.WriteLine($"[PROXY_ERROR] Отклонен невалидный прокси: {proxyLine} (хост пустой)");
                    return false;
                }

                // Если все проверки пройдены — возвращаем корректный прокси
                validProxy = $"socks5://{userPassPart}@{host}:{port}";
                Console.WriteLine($"[PROXY_DEBUG] Прокси валидный, итоговая строка: {validProxy}");
                return true;
            }
            // Если нет @ — прокси без авторизации, парсим просто host:port
            else
            {
                int lastColon = rawProxy.LastIndexOf(':');
                if (lastColon == -1)
                {
                    Console.WriteLine($"[PROXY_ERROR] Отклонен невалидный прокси: {proxyLine} (не найден порт, нет @ и нет : для разделения хоста и порта)");
                    return false;
                }

                string host = rawProxy.Substring(0, lastColon);
                string portStr = rawProxy.Substring(lastColon + 1);
                
                if (!int.TryParse(portStr, out int port) || port < 1 || port > 65535)
                {
                    Console.WriteLine($"[PROXY_ERROR] Отклонен невалидный прокси: {proxyLine} (порт {portStr} невалидный)");
                    return false;
                }

                validProxy = $"socks5://{host}:{port}";
                return true;
            }
        }

        public string? GetNextProxy()
        {
            if (selectedManualProxy != null)
            {
                return selectedManualProxy;
            }

            if (validSocks5Proxies.Count == 0) return null;

            lock (lockObj)
            {
                var proxy = validSocks5Proxies[pointer];
                pointer = (pointer + 1) % validSocks5Proxies.Count;
                return proxy;
            }
        }

        public void MarkProxyAsDead(string proxy)
        {
            lock (lockObj)
            {
                deadProxies.Add(proxy);
                validSocks5Proxies.Remove(proxy);
                if (pointer >= validSocks5Proxies.Count) pointer = 0;
            }
        }

        public int GetActiveProxyCount()
        {
            return validSocks5Proxies.Count;
        }

        public void SelectManualProxy(string proxy)
        {
            selectedManualProxy = proxy;
        }

        public void ClearManualProxy()
        {
            selectedManualProxy = null;
        }

        public List<string> GetAllProxies()
        {
            return new List<string>(validSocks5Proxies);
        }

        public async Task<bool> TestProxyAsync(string proxy, int timeoutSeconds = 5)
        {
            string proxyShort = (proxy.Length > 50) ? proxy.Substring(0, 50) + "..." : proxy;
            try
            {
                Console.WriteLine($"[PROXY_TEST_DEBUG] Начинаю проверку прокси: {proxyShort}");
                
                // Парсим прокси полностью как в ValidateSocks5Proxy (используем последнее вхождение @ и :)
                var cleanProxy = proxy.Replace("socks5://", "");
                int lastAtIndex = cleanProxy.LastIndexOf('@');
                string userPass = "";
                string hostPort = cleanProxy;
                string username = "";
                string password = "";
                bool hasAuth = false;
                
                if (lastAtIndex != -1)
                {
                    userPass = cleanProxy.Substring(0, lastAtIndex);
                    hostPort = cleanProxy.Substring(lastAtIndex + 1);
                    hasAuth = true;
                    Console.WriteLine($"[PROXY_TEST_DEBUG] Прокси {proxyShort} имеет авторизацию, часть с креденшелами: {(userPass.Length > 20 ? userPass.Substring(0, 20) + "..." : userPass)}, hostPort: {hostPort}");
                    
                    // Разделяем логин и пароль (как в валидации - берем первое вхождение :)
                    var userPassParts = userPass.Split(new[] {':'}, 2);
                    if (userPassParts.Length == 2)
                    {
                        username = userPassParts[0];
                        password = userPassParts[1];
                        Console.WriteLine($"[PROXY_TEST_DEBUG] Прокси {proxyShort} распарсил логин: {username}, длина пароля: {password.Length}");
                    }
                }
                else
                {
                    Console.WriteLine($"[PROXY_TEST_DEBUG] Прокси {proxyShort} без авторизации, hostPort: {hostPort}");
                }

                // Парсим хост и порт (используем последнее вхождение :)
                int lastColon = hostPort.LastIndexOf(':');
                if (lastColon == -1) 
                {
                    Console.WriteLine($"[PROXY_TEST_FAIL] Прокси {proxyShort} не удалось распарсить порт: не найден разделитель : в hostPort {hostPort}");
                    return false;
                }
                string host = hostPort.Substring(0, lastColon);
                if (!int.TryParse(hostPort.Substring(lastColon + 1), out int port)) 
                {
                    Console.WriteLine($"[PROXY_TEST_FAIL] Прокси {proxyShort} неверный порт: {hostPort.Substring(lastColon + 1)}");
                    return false;
                }
                Console.WriteLine($"[PROXY_TEST_DEBUG] Прокси {proxyShort} распарсен хост: {host}, порт: {port}");

                // Подключаемся по TCP к прокси
                using var client = new TcpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                Console.WriteLine($"[PROXY_TEST_DEBUG] Прокси {proxyShort} подключаюсь к {host}:{port} с таймаутом {timeoutSeconds}с");
                await client.ConnectAsync(host, port, cts.Token);
                Console.WriteLine($"[PROXY_TEST_DEBUG] Прокси {proxyShort} TCP-подключение успешно установлено");
                
                NetworkStream stream = client.GetStream();
                stream.ReadTimeout = timeoutSeconds * 1000;
                stream.WriteTimeout = timeoutSeconds * 1000;

                // 1. Отправляем SOCKS5 приветствие (версия 5, поддерживаем 2 метода: без авторизации и с логином/паролем)
                // 0x05=SOCKS5 версия, количество методов=2, 0x00=без авторизации, 0x02=авторизация по логин/пароль
                byte[] helloMsg;
                if (hasAuth)
                {
                    // Если прокси с авторизацией - отправляем только метод 0x02
                    helloMsg = new byte[] { 0x05, 0x01, 0x02 };
                }
                else
                {
                    // Если без авторизации - отправляем оба метода
                    helloMsg = new byte[] { 0x05, 0x02, 0x00, 0x02 };
                }
                
                await stream.WriteAsync(helloMsg, 0, helloMsg.Length, cts.Token);
                await stream.FlushAsync(cts.Token);
                Console.WriteLine($"[PROXY_TEST_DEBUG] Прокси {proxyShort} отправлен SOCKS5 hello, длина: {helloMsg.Length} байт");

                // Читаем ответ от прокси на приветствие (всегда 2 байта: версия и выбранный метод)
                byte[] helloResp = new byte[2];
                int read = await stream.ReadAsync(helloResp, 0, helloResp.Length, cts.Token);
                if (read != 2) 
                {
                    Console.WriteLine($"[PROXY_TEST_FAIL] Прокси {proxyShort} получил некорректный размер ответа на hello: {read} байт (ожидалось 2)");
                    return false;
                }
                if (helloResp[0] != 0x05) 
                {
                    Console.WriteLine($"[PROXY_TEST_FAIL] Прокси {proxyShort} не является SOCKS5: версия ответа {helloResp[0]:X2} (ожидалось 0x05)");
                    return false;
                }
                Console.WriteLine($"[PROXY_TEST_DEBUG] Прокси {proxyShort} получил ответ на hello: версия={helloResp[0]:X2}, выбранный метод={helloResp[1]:X2}");

                // Проверяем что прокси выбрал корректный метод авторизации
                if (hasAuth)
                {
                    if (helloResp[1] != 0x02) 
                    {
                        Console.WriteLine($"[PROXY_TEST_FAIL] Прокси {proxyShort} не поддерживает авторизацию по логину/паролю, выбрал метод {helloResp[1]:X2} (ожидалось 0x02)");
                        return false;
                    }
                    Console.WriteLine($"[PROXY_TEST_DEBUG] Прокси {proxyShort} поддерживает авторизацию по логину/паролю как и ожидалось");
                }
                else
                {
                    if (helloResp[1] != 0x00 && helloResp[1] != 0x02) 
                    {
                        Console.WriteLine($"[PROXY_TEST_FAIL] Прокси {proxyShort} выбрал неподдерживаемый метод авторизации {helloResp[1]:X2}");
                        return false;
                    }
                    // Если прокси без авторизации и сервер выбрал метод 0x00 - проверка завершена
                    if (helloResp[1] == 0x00)
                    {
                        Console.WriteLine($"[PROXY_TEST_SUCCESS] Прокси {proxyShort} проверен успешно (без авторизации)");
                        client.Close();
                        return true;
                    }
                }

                // 2. Если есть логин и пароль и мы должны отправить авторизацию
                if (hasAuth && !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    Console.WriteLine($"[PROXY_TEST_DEBUG] Прокси {proxyShort} отправляю пакет авторизации");
                    
                    // Формируем пакет авторизации (версия 0x01 для RFC 1929)
                    List<byte> authMsg = new List<byte> { 0x01 }; // версия аутентификации (для username/password всегда 0x01)
                    authMsg.Add((byte)username.Length);
                    authMsg.AddRange(System.Text.Encoding.ASCII.GetBytes(username));
                    authMsg.Add((byte)password.Length);
                    authMsg.AddRange(System.Text.Encoding.ASCII.GetBytes(password));

                    await stream.WriteAsync(authMsg.ToArray(), 0, authMsg.Count, cts.Token);
                    await stream.FlushAsync(cts.Token);
                    Console.WriteLine($"[PROXY_TEST_DEBUG] Прокси {proxyShort} пакет авторизации отправлен, размер: {authMsg.Count} байт");

                    // Читаем ответ авторизации (всегда 2 байта: версия и статус)
                    byte[] authResp = new byte[2];
                    read = await stream.ReadAsync(authResp, 0, authResp.Length, cts.Token);
                    if (read != 2) 
                    {
                        Console.WriteLine($"[PROXY_TEST_FAIL] Прокси {proxyShort} получил некорректный размер ответа на авторизацию: {read} байт (ожидалось 2)");
                        return false;
                    }
                    if (authResp[1] != 0x00) 
                    {
                        // 0x00 = успех, любое другое значение = ошибка авторизации
                        Console.WriteLine($"[PROXY_TEST_FAIL] Прокси {proxyShort} авторизация не удалась, статус ответа {authResp[1]:X2} (неверный логин или пароль)");
                        return false;
                    }
                    Console.WriteLine($"[PROXY_TEST_DEBUG] Прокси {proxyShort} авторизация прошла успешно, статус 0x00");
                }

                // Если все проверки прошли - прокси рабочий
                Console.WriteLine($"[PROXY_TEST_SUCCESS] Прокси {proxyShort} все проверки пройдены успешно");
                client.Close();
                return true;
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[PROXY_TEST_FAIL] Прокси {proxyShort} проверка завершилась по таймауту ({timeoutSeconds}с)");
                return false;
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"[PROXY_TEST_FAIL] Прокси {proxyShort} сетевая ошибка: {ex.Message} (SocketErrorCode: {ex.SocketErrorCode})");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PROXY_TEST_FAIL] Прокси {proxyShort} неожиданная ошибка при проверке: {ex.GetType().Name} - {ex.Message}");
                return false;
            }
        }

        public async Task<bool> TestProxy(string proxy, CancellationToken ct)
        {
            try
            {
                // Берем случайный валидный аккаунт из списка (нужно предварительно загрузить список аккаунтов)
                var testAccount = _validAccounts.FirstOrDefault();
                if (testAccount == null) return false;

                var parts = testAccount.Split(':');
                if (parts.Length < 2) return false;
                var email = parts[0].Trim();
                var password = parts[1].Trim();
                var domain = email.Split('@').Last();

                // Используем существующую логику подключения для проверки
                var scanner = new ContactsScanner("", "", this, 1, 30, true);
                // Получаем приватный метод TryConnectWithRetry через рефлексию
                var methodInfo = typeof(ContactsScanner).GetMethod("TryConnectWithRetry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (methodInfo == null) return false;
                
                var result = await (Task<(bool Success, ImapClient? Client)>)methodInfo.Invoke(scanner, new object[] { email, password, domain, proxy, ct });
                _proxyLastChecked[proxy] = DateTime.Now;
                return result.Success;
            }
            catch
            {
                return false;
            }
        }
    }
}