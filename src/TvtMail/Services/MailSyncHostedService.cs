using Microsoft.Extensions.Options;
using TvtMail.Options;

namespace TvtMail.Services;

public sealed class MailSyncHostedService(
    ImapMailService mailService,
    IOptions<TvtMailOptions> options,
    ILogger<MailSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var seconds = Math.Max(30, options.Value.SyncIntervalSeconds);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));

        do
        {
            try
            {
                await mailService.SyncAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Lỗi background sync TVT Mail");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
