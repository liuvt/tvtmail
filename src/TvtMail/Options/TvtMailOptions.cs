namespace TvtMail.Options;

public sealed class TvtMailOptions
{
    public const string SectionName = "TvtMail";

    public int SyncIntervalSeconds { get; set; } = 60;

    // 0 = đồng bộ toàn bộ thư trong INBOX ở lần đầu.
    public int InitialSyncLimit { get; set; } = 0;

    public int FetchBatchSize { get; set; } = 100;

    public int MaxConcurrentAccountSyncs { get; set; } = 4;
}
