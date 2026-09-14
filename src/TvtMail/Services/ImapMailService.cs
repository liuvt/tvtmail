using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MimeKit;
using TvtMail.Data;
using TvtMail.Models;
using TvtMail.Options;

namespace TvtMail.Services;

public sealed class ImapMailService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly CredentialProtector _credentials;
    private readonly TvtMailOptions _options;
    private readonly ILogger<ImapMailService> _logger;
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _accountLocks = new();

    public ImapMailService(
        IDbContextFactory<AppDbContext> dbFactory,
        CredentialProtector credentials,
        IOptions<TvtMailOptions> options,
        ILogger<ImapMailService> logger)
    {
        _dbFactory = dbFactory;
        _credentials = credentials;
        _options = options.Value;
        _logger = logger;
    }

    public async Task TestConnectionAsync(MailConnectionSettings settings, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        await ConnectAndAuthenticateAsync(client, settings, cancellationToken);
        await client.Inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    public async Task SyncAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var accountIds = await db.MailAccounts
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(_options.MaxConcurrentAccountSyncs, 1, 10),
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(accountIds, parallelOptions, async (accountId, token) =>
        {
            try
            {
                await SyncAccountAsync(accountId, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể đồng bộ mailbox {AccountId}", accountId);
            }
        });
    }

    public async Task SyncAccountAsync(int accountId, CancellationToken cancellationToken = default)
    {
        var gate = _accountLocks.GetOrAdd(accountId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var account = await db.MailAccounts
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == accountId, cancellationToken);

            if (account is null || !account.IsActive)
                return;

            var settings = ToSettings(account);

            using var client = CreateClient();
            await ConnectAndAuthenticateAsync(client, settings, cancellationToken);

            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var currentUidValidity = (long)inbox.UidValidity;

            if (account.InboxUidValidity is not null && account.InboxUidValidity.Value != currentUidValidity)
            {
                // RFC IMAP: khi UIDVALIDITY đổi, cache UID cũ không còn hợp lệ.
                await db.MailMessages
                    .Where(x => x.MailAccountId == accountId && x.Folder == "INBOX")
                    .ExecuteDeleteAsync(cancellationToken);
            }

            var maxCachedUid = await db.MailMessages
                .AsNoTracking()
                .Where(x => x.MailAccountId == accountId && x.Folder == "INBOX")
                .MaxAsync(x => (long?)x.Uid, cancellationToken) ?? 0;

            List<UniqueId> newUids;

            if (maxCachedUid == 0)
            {
                newUids = (await inbox.SearchAsync(SearchQuery.All, cancellationToken)).ToList();

                if (_options.InitialSyncLimit > 0 && newUids.Count > _options.InitialSyncLimit)
                {
                    newUids = newUids
                        .Skip(newUids.Count - _options.InitialSyncLimit)
                        .ToList();
                }
            }
            else if (maxCachedUid < uint.MaxValue && inbox.UidNext is { } uidNext && uidNext.Id > maxCachedUid + 1)
            {
                var first = new UniqueId((uint)(maxCachedUid + 1));
                var last = new UniqueId(uidNext.Id - 1);
                var range = new UniqueIdRange(first, last);
                newUids = (await inbox.SearchAsync(SearchQuery.Uids(range), cancellationToken)).ToList();
            }
            else if (inbox.UidNext is null)
            {
                // Một số server không trả UIDNEXT: fallback an toàn bằng SEARCH ALL rồi lọc UID mới.
                newUids = (await inbox.SearchAsync(SearchQuery.All, cancellationToken))
                    .Where(x => x.Id > maxCachedUid)
                    .ToList();
            }
            else
            {
                newUids = new List<UniqueId>();
            }

            var batchSize = Math.Clamp(_options.FetchBatchSize, 20, 500);
            for (var offset = 0; offset < newUids.Count; offset += batchSize)
            {
                var batch = newUids.Skip(offset).Take(batchSize).ToList();
                var summaries = await inbox.FetchAsync(
                    batch,
                    MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags | MessageSummaryItems.InternalDate,
                    cancellationToken);

                foreach (var summary in summaries)
                {
                    var envelope = summary.Envelope;
                    var from = envelope?.From?.Mailboxes.FirstOrDefault();
                    var sentAt = envelope?.Date?.UtcDateTime
                                 ?? summary.InternalDate?.UtcDateTime
                                 ?? DateTime.UtcNow;
                    var receivedAt = summary.InternalDate?.UtcDateTime ?? sentAt;

                    db.MailMessages.Add(new MailMessage
                    {
                        MailAccountId = accountId,
                        Folder = "INBOX",
                        Uid = summary.UniqueId.Id,
                        InternetMessageId = Trim(envelope?.MessageId, 998),
                        Subject = Trim(envelope?.Subject, 1200) ?? "(Không có tiêu đề)",
                        FromName = Trim(from?.Name, 500) ?? string.Empty,
                        FromAddress = Trim(from?.Address, 500) ?? string.Empty,
                        ToText = Trim(envelope?.To?.ToString(), 2000) ?? string.Empty,
                        SentAtUtc = DateTime.SpecifyKind(sentAt, DateTimeKind.Utc),
                        ReceivedAtUtc = DateTime.SpecifyKind(receivedAt, DateTimeKind.Utc),
                        IsSeen = summary.Flags?.HasFlag(MessageFlags.Seen) == true,
                        Preview = string.Empty
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
            }

            var tracked = await db.MailAccounts.FirstAsync(x => x.Id == accountId, cancellationToken);
            tracked.LastSyncedAtUtc = DateTime.UtcNow;
            tracked.InboxUidValidity = currentUidValidity;
            tracked.LastError = null;
            await db.SaveChangesAsync(cancellationToken);

            await client.DisconnectAsync(true, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await SaveAccountErrorAsync(accountId, ex.Message, cancellationToken);
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task LoadBodyAsync(long mailId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var mail = await db.MailMessages
            .Include(x => x.MailAccount)
            .FirstOrDefaultAsync(x => x.Id == mailId, cancellationToken);

        if (mail is null || mail.BodyLoadedAtUtc is not null)
            return;

        var settings = ToSettings(mail.MailAccount);

        using var client = CreateClient();
        await ConnectAndAuthenticateAsync(client, settings, cancellationToken);
        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

        var message = await inbox.GetMessageAsync(new UniqueId((uint)mail.Uid), cancellationToken);
        var text = message.TextBody;

        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(message.HtmlBody))
            text = HtmlToText(message.HtmlBody);

        var from = message.From.Mailboxes.FirstOrDefault();

        mail.Subject = Trim(message.Subject, 1200) ?? mail.Subject;
        mail.FromName = Trim(from?.Name, 500) ?? mail.FromName;
        mail.FromAddress = Trim(from?.Address, 500) ?? mail.FromAddress;
        mail.ToText = Trim(message.To.ToString(), 2000) ?? mail.ToText;
        mail.BodyText = text ?? string.Empty;
        mail.BodyHtml = message.HtmlBody;
        mail.BodyLoadedAtUtc = DateTime.UtcNow;
        mail.IsSeen = true;
        mail.HasAttachments = message.Attachments.Any();
        mail.Preview = Trim(NormalizeWhitespace(text), 600) ?? string.Empty;

        await db.SaveChangesAsync(cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    private MailConnectionSettings ToSettings(MailAccount account)
        => new(
            account.EmailAddress,
            account.Username,
            _credentials.Unprotect(account.EncryptedPassword),
            account.ImapHost,
            account.ImapPort,
            account.SecurityMode);

    private static ImapClient CreateClient()
    {
        return new ImapClient
        {
            Timeout = 30_000
        };
    }

    private static async Task ConnectAndAuthenticateAsync(
        ImapClient client,
        MailConnectionSettings settings,
        CancellationToken cancellationToken)
    {
        var socketOptions = settings.SecurityMode switch
        {
            ImapSecurityMode.SslOnConnect => SecureSocketOptions.SslOnConnect,
            ImapSecurityMode.StartTls => SecureSocketOptions.StartTls,
            ImapSecurityMode.None => SecureSocketOptions.None,
            _ => SecureSocketOptions.Auto
        };

        await client.ConnectAsync(settings.ImapHost, settings.ImapPort, socketOptions, cancellationToken);
        await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
    }

    private async Task SaveAccountErrorAsync(int accountId, string message, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var account = await db.MailAccounts.FirstOrDefaultAsync(x => x.Id == accountId, cancellationToken);
            if (account is null)
                return;

            account.LastError = Trim(message, 1200);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Không thể lưu lỗi đồng bộ cho mailbox {AccountId}", accountId);
        }
    }

    private static string HtmlToText(string html)
    {
        var withoutScripts = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var withBreaks = Regex.Replace(withoutScripts, "<(br|/p|/div|/li|/tr|/h[1-6])\\s*/?>", "\n", RegexOptions.IgnoreCase);
        var noTags = Regex.Replace(withBreaks, "<[^>]+>", " ");
        return NormalizeWhitespace(WebUtility.HtmlDecode(noTags));
    }

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = value.Replace("\r\n", "\n").Replace('\r', '\n');
        value = Regex.Replace(value, "[ \\t]+", " ");
        value = Regex.Replace(value, "\\n{3,}", "\n\n");
        return value.Trim();
    }

    private static string? Trim(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
