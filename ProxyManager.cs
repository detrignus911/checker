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

            string hostPortPart;
            if (cleanProxy.Contains('@'))
            {
                var atParts = cleanProxy.Split('@');
                hostPortPart = atParts[atParts.Length - 1];
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
            try
            {
                var cleanProxy = proxy.Replace("socks5://", "");
                var parts = cleanProxy.Split('@').Last().Split(':');
                if (parts.Length < 2) return false;

                var host = parts[0];
                if (!int.TryParse(parts[1], out int port)) return false;

                using var client = new TcpClient();
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                await client.ConnectAsync(host, port, cts.Token);
                return true;
            }
            catch
            {
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