using System;
using System.Collections.Generic;

namespace opentask
{
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

        public static List<(string Server, int Port, bool UseSsl)> GetImapServersForDomain(string domain)
        {
            var servers = new List<(string Server, int Port, bool UseSsl)>();
            var imapDict = GetImapDictionary();
            
            // Сначала проверяем если домен есть в словаре
            if (imapDict.TryGetValue(domain, out var config))
            {
                servers.Add((config.server, config.port, config.ssl));
            }
            
            // Добавляем общие серверы как запасной вариант
            servers.AddRange(new List<(string Server, int Port, bool UseSsl)>
            {
                ("imap.gmail.com", 993, true),
                ("outlook.office365.com", 993, true),
                ("imap-mail.outlook.com", 993, true),
                ("imap.yandex.ru", 993, true),
                ("imap.mail.ru", 993, true),
                ("imap.rambler.ru", 993, true)
            });
            
            return servers;
        }
    }
}