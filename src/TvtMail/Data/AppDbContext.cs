using Microsoft.EntityFrameworkCore;
using TvtMail.Models;

namespace TvtMail.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<MailAccount> MailAccounts => Set<MailAccount>();
    public DbSet<MailMessage> MailMessages => Set<MailMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MailAccount>()
            .HasIndex(x => x.EmailAddress);

        modelBuilder.Entity<MailMessage>()
            .HasIndex(x => new { x.MailAccountId, x.Folder, x.Uid })
            .IsUnique();

        modelBuilder.Entity<MailMessage>()
            .HasIndex(x => x.ReceivedAtUtc);

        modelBuilder.Entity<MailMessage>()
            .HasOne(x => x.MailAccount)
            .WithMany(x => x.Messages)
            .HasForeignKey(x => x.MailAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
