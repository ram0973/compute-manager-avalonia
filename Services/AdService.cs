using System.DirectoryServices.Protocols;
using System.Net;
using System.Net.NetworkInformation;
using ComputerManager.Models;

namespace ComputerManager.Services;

/// <summary>Кроссплатформенный LDAP-клиент (Windows / Linux / macOS).</summary>
public class AdService
{
    private static readonly string[] Attrs =
        ["name", "dNSHostName", "operatingSystem", "description", "distinguishedName", "lastLogonTimestamp"];

    public Task<List<ComputerInfo>> GetComputersAsync(string? server, string? user, string? password, bool useSsl)
        => Task.Run(() =>
        {
            server = string.IsNullOrWhiteSpace(server)
                ? IPGlobalProperties.GetIPGlobalProperties().DomainName
                : server.Trim();

            if (string.IsNullOrWhiteSpace(server))
                throw new InvalidOperationException("Укажите сервер AD (контроллер домена или имя домена).");

            var id = useSsl
                ? new LdapDirectoryIdentifier(server, 636, fullyQualifiedDnsHostName: true, connectionless: false)
                : new LdapDirectoryIdentifier(server);

            using var conn = new LdapConnection(id);
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

            // baseDN из RootDSE
            var rootResp = (SearchResponse)conn.SendRequest(
                new SearchRequest("", "(objectClass=*)", SearchScope.Base, "defaultNamingContext"));
            var baseDn = rootResp.Entries[0].Attributes["defaultNamingContext"][0]?.ToString()
                         ?? throw new InvalidOperationException("Не удалось определить baseDN.");

            var req = new SearchRequest(
                baseDn,
                "(&(objectCategory=computer)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))",
                SearchScope.Subtree,
                Attrs);

            var page = new PageResultRequestControl(1000);
            req.Controls.Add(page);

            var result = new List<ComputerInfo>();
            do
            {
                var resp = (SearchResponse)conn.SendRequest(req);
                foreach (SearchResultEntry e in resp.Entries)
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

                var prc = resp.Controls.OfType<PageResultResponseControl>().FirstOrDefault();
                page.Cookie = prc?.Cookie ?? [];
            } while (page.Cookie.Length > 0);

            return result.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        });

    private static string? Str(SearchResultEntry e, string attr) =>
        e.Attributes.Contains(attr) && e.Attributes[attr].Count > 0 ? e.Attributes[attr][0]?.ToString() : null;
}
