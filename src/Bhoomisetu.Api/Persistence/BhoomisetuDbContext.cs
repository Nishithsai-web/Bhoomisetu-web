using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Bhoomisetu.Api.Persistence;

/// <summary>
/// EF Core mapping onto the schema created by db/migrations. EF never creates or migrates tables; the SQL
/// files do, so the database stays reviewable and the same for every tool.
/// </summary>
public sealed class BhoomisetuDbContext(DbContextOptions<BhoomisetuDbContext> options) : DbContext(options)
{
    public DbSet<UserRecord> Users => Set<UserRecord>();
    public DbSet<UserCheckRecord> UserChecks => Set<UserCheckRecord>();
    public DbSet<OtpChallengeRecord> OtpChallenges => Set<OtpChallengeRecord>();
    public DbSet<ParcelRecord> Parcels => Set<ParcelRecord>();
    public DbSet<ParcelPartyRecord> ParcelParties => Set<ParcelPartyRecord>();
    public DbSet<OfferRecord> Offers => Set<OfferRecord>();
    public DbSet<ListerStatsRecord> ListerStats => Set<ListerStatsRecord>();
    public DbSet<AuditRecord> AuditLog => Set<AuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserRecord>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<UserCheckRecord>(e =>
        {
            e.ToTable("user_checks");
            e.HasKey(x => new { x.UserId, x.CheckType });
        });

        modelBuilder.Entity<OtpChallengeRecord>(e =>
        {
            e.ToTable("otp_challenges");
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<ParcelRecord>(e =>
        {
            e.ToTable("parcels");
            e.HasKey(x => x.Id);
            e.Property(x => x.RecordedAreaSqm).HasPrecision(14, 4);
            e.Property(x => x.EnteredAreaValue).HasPrecision(14, 4);
            e.Property(x => x.FrontageM).HasPrecision(8, 2);
            e.Property(x => x.DepthM).HasPrecision(8, 2);
            e.Property(x => x.RoadWidthM).HasPrecision(6, 2);
        });

        modelBuilder.Entity<ParcelPartyRecord>(e =>
        {
            e.ToTable("parcel_parties");
            e.HasKey(x => new { x.ParcelId, x.UserId, x.Role });
        });

        modelBuilder.Entity<OfferRecord>(e =>
        {
            e.ToTable("offers");
            e.HasKey(x => x.Id);
            e.Property(x => x.BuyerBrokeragePercent).HasPrecision(5, 2);
        });

        modelBuilder.Entity<ListerStatsRecord>(e =>
        {
            e.ToTable("lister_stats");
            e.HasKey(x => x.UserId);
        });

        modelBuilder.Entity<AuditRecord>(e =>
        {
            e.ToTable("audit_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.Details).HasColumnType("jsonb");
        });

        // Columns are snake_case in SQL; properties are PascalCase in C#.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    internal static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var previousIsLowerOrDigit = i > 0 && (char.IsLower(name[i - 1]) || char.IsDigit(name[i - 1]));
                var nextIsLower = i + 1 < name.Length && char.IsLower(name[i + 1]);
                var previousIsUpper = i > 0 && char.IsUpper(name[i - 1]);
                if (i > 0 && (previousIsLowerOrDigit || (previousIsUpper && nextIsLower)))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
