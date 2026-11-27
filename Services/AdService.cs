using System.DirectoryServices.Protocols;
using System.Net;
using System.Net.NetworkInformation;
using ComputerManager.Models;

namespace ComputerManager.Services;

/// <summary>Кроссплатформенный LDAP-клиент (Windows / Linux / macOS): компьютеры и пользователи AD.</summary>
public class AdService
{
    private static readonly string[] ComputerAttrs =
        ["name", "dNSHostName", "operatingSystem", "description", "distinguishedName", "lastLogonTimestamp"];

    private static readonly string[] UserAttrs =
        ["sAMAccountName", "displayName", "cn", "telephoneNumber", "mobile"];

    public Task<List<ComputerInfo>> GetComputersAsync(string? server, string? user, string? password, bool useSsl)
        => Task.Run(() =>
        {
            using var conn = Connect(server, user, password, useSsl, out var baseDn);

            var result = new List<ComputerInfo>();
            foreach (var e in Search(conn, baseDn,
                         "(&(objectCategory=computer)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))", ComputerAttrs))
            {
                var dn = Str(e, "distinguishedName");
                long ts = long.TryParse(Str(e, "lastLogonTimestamp"), out var t) ? t : 0;

                result.Add(new ComputerInfo
                {
                    Name = Str(e, "name") ?? "?",
                    DnsName = Str(e, "dNSHostName"),
                    OperatingSystem = Str(e, "operatingSystem"),
                    Description = Str(e, "description"),
                    OrgUnit = dn is null ? null : dn[(dn.IndexOf(',') + 1)..],
                    LastLogon = ts > 0 ? DateTime.FromFileTime(ts) : null,
                });
            }

            return result.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        });

    /// <summary>Включённые учётки пользователей: логин, ФИО (displayName), телефон, мобильный.</summary>
    public Task<List<UserInfo>> GetUsersAsync(string? server, string? user, string? password, bool useSsl)
        => Task.Run(() =>
        {
            using var conn = Connect(server, user, password, useSsl, out var baseDn);

            var result = new List<UserInfo>();
            foreach (var e in Search(conn, baseDn,
                         "(&(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))",
                         UserAttrs))
            {
                var login = Str(e, "sAMAccountName");
                if (string.IsNullOrWhiteSpace(login)) continue;

                result.Add(new UserInfo
                {
                    Login = login,
                    FullName = Str(e, "displayName") ?? Str(e, "cn"),
                    Phone = Str(e, "telephoneNumber"),
                    Mobile = Str(e, "mobile"),
                });
            }

            return result.OrderBy(u => u.Login, StringComparer.OrdinalIgnoreCase).ToList();
        });

    private static LdapConnection Connect(string? server, string? user, string? password, bool useSsl, out string baseDn)
    {
        server = string.IsNullOrWhiteSpace(server)
            ? IPGlobalProperties.GetIPGlobalProperties().DomainName
            : server.Trim();

        if (string.IsNullOrWhiteSpace(server))
            throw new InvalidOperationException("Укажите сервер AD (контроллер домена или имя домена).");

        var id = useSsl
            ? new LdapDirectoryIdentifier(server, 636, fullyQualifiedDnsHostName: true, connectionless: false)
            : new LdapDirectoryIdentifier(server);

        var conn = new LdapConnection(id);
        try
        {
            conn.SessionOptions.ProtocolVersion = 3;
            conn.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
            conn.SessionOptions.SecureSocketLayer = useSsl;

            if (!string.IsNullOrWhiteSpace(user))
            {
                conn.Credential = new NetworkCredential(user, password);
                conn.AuthType = useSsl ? AuthType.Basic : AuthType.Negotiate;
            }
            else
            {
                // текущие креды пользователя (Kerberos/NTLM) — основной сценарий на Windows
                conn.AuthType = AuthType.Negotiate;
            }

            conn.Bind();

            var rootResp = (SearchResponse)conn.SendRequest(
                new SearchRequest("", "(objectClass=*)", SearchScope.Base, "defaultNamingContext"));
            baseDn = rootResp.Entries[0].Attributes["defaultNamingContext"][0]?.ToString()
                     ?? throw new InvalidOperationException("Не удалось определить baseDN.");
            return conn;
        }
        catch
        {
            conn.Dispose();
            throw;
        }
    }

    private static IEnumerable<SearchResultEntry> Search(LdapConnection conn, string baseDn, string filter, string[] attrs)
    {
        var req = new SearchRequest(baseDn, filter, SearchScope.Subtree, attrs);
        var page = new PageResultRequestControl(1000);
        req.Controls.Add(page);

        do
        {
            var resp = (SearchResponse)conn.SendRequest(req);
            foreach (SearchResultEntry e in resp.Entries) yield return e;

            var prc = resp.Controls.OfType<PageResultResponseControl>().FirstOrDefault();
            page.Cookie = prc?.Cookie ?? [];
        } while (page.Cookie.Length > 0);
    }

    private static string? Str(SearchResultEntry e, string attr) =>
        e.Attributes.Contains(attr) && e.Attributes[attr].Count > 0 ? e.Attributes[attr][0]?.ToString() : null;
}
