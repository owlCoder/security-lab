using Microsoft.EntityFrameworkCore;
using Team21.Acc.Core.Common;
using Team21.Acc.Infrastructure.Common;
using Team21.Acc.Tests.Infrastructure.Fixture;
using Team21.Acc.Tests.Infrastructure.Fixture.Data;

namespace Team21.Acc.Tests.Infrastructure
{
    /// <summary>
    /// Runs on the EF in memory provider, Postgres specific behaviour isn't covered here.
    /// </summary>
    public class TeamDbContextBaseTests
    {
        private DbContextOptions<TestDbContext> _options = null!;

        [SetUp]
        public void NewDatabase()
        {
            _options = new DbContextOptionsBuilder<TestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        }

        [Test]
        public async Task InsertStampsCreatedAt()
        {
            var before = DateTime.UtcNow;

            var thing = await SeedAsync();

            Assert.That(thing.CreatedAt, Is.GreaterThanOrEqualTo(before));
            Assert.That(thing.CreatedAt.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(thing.UpdatedAt, Is.Null);
        }

        [Test]
        public async Task UpdateStampsUpdatedAt()
        {
            var thing = await SeedAsync();

            await using var db = new TestDbContext(_options);
            var loaded = await db.Things.SingleAsync(t => t.Id == thing.Id);
            loaded.Name = "b";
            await db.SaveChangesAsync();

            Assert.That(loaded.UpdatedAt, Is.Not.Null);
            Assert.That(loaded.UpdatedAt!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        [Test]
        public void SyncSaveChangesStampsToo()
        {
            using var db = new TestDbContext(_options);
            var thing = new Thing { Name = "a", CreatedAt = default };

            db.Things.Add(thing);
            db.SaveChanges();

            Assert.That(thing.CreatedAt, Is.Not.EqualTo(default(DateTime)));
        }

        [Test]
        public async Task UpdateCannotChangeCreatedAtOrCreatedBy()
        {
            var thing = await SeedAsync();

            await using (var db = new TestDbContext(_options))
            {
                var loaded = await db.Things.SingleAsync(t => t.Id == thing.Id);
                loaded.CreatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                loaded.CreatedBy = Guid.NewGuid();
                loaded.Name = "b";
                await db.SaveChangesAsync();
            }

            await using var check = new TestDbContext(_options);
            var stored = await check.Things.SingleAsync(t => t.Id == thing.Id);

            Assert.That(stored.CreatedAt, Is.EqualTo(thing.CreatedAt));
            Assert.That(stored.CreatedBy, Is.EqualTo(thing.CreatedBy));
            Assert.That(stored.Name, Is.EqualTo("b"));
        }

        [Test]
        public async Task SoftDeletedRowsAreHiddenByDefault()
        {
            var kept = await SeedAsync();
            var removed = await SeedAsync();
            await SoftDeleteAsync(removed.Id);

            await using var db = new TestDbContext(_options);
            var visible = await db.Things.Select(t => t.Id).ToListAsync();

            Assert.That(visible, Is.EqualTo(new[] { kept.Id }));
        }

        [Test]
        public async Task IgnoringTheFilterShowsSoftDeletedRows()
        {
            var removed = await SeedAsync();
            await SoftDeleteAsync(removed.Id);

            await using var db = new TestDbContext(_options);
            var stored = await db.Things
                .IgnoreQueryFilters([TeamDbContextBase.SoftDeleteFilter])
                .SingleAsync(t => t.Id == removed.Id);

            Assert.That(stored.IsDeleted, Is.True);
        }

        [Test]
        public void ConfigurationsFromOtherModulesAreNotApplied()
        {
            using var db = new TestDbContext(_options);

            // OtherModuleConfiguration would set a max length of 1 if it leaked into this context
            var name = db.Model.FindEntityType(typeof(Thing))!.FindProperty(nameof(Thing.Name))!;

            Assert.That(name.GetMaxLength(), Is.EqualTo(100));
        }

        private async Task<Thing> SeedAsync()
        {
            await using var db = new TestDbContext(_options);
            var thing = new Thing { Name = "seed", CreatedBy = Guid.NewGuid() };
            db.Things.Add(thing);
            await db.SaveChangesAsync();
            return thing;
        }

        private async Task SoftDeleteAsync(Guid id)
        {
            await using var db = new TestDbContext(_options);
            var thing = await db.Things.SingleAsync(t => t.Id == id);
            thing.IsDeleted = true;
            thing.DeletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }
}

// Fake module laid out like ours, context in .Data and its configuration one level up
namespace Team21.Acc.Tests.Infrastructure.Fixture
{
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class Thing : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    public class ThingConfiguration : IEntityTypeConfiguration<Thing>
    {
        public void Configure(EntityTypeBuilder<Thing> builder) => builder.Property(t => t.Name).HasMaxLength(100);
    }
}

namespace Team21.Acc.Tests.Infrastructure.Fixture.Data
{
    public class TestDbContext(DbContextOptions<TestDbContext> options) : TeamDbContextBase(options)
    {
        protected override string Schema => "test";

        public DbSet<Thing> Things => Set<Thing>();
    }
}

namespace Team21.Acc.Tests.Infrastructure.OtherModule
{
    using Microsoft.EntityFrameworkCore.Metadata.Builders;

    public class OtherModuleConfiguration : IEntityTypeConfiguration<Thing>
    {
        public void Configure(EntityTypeBuilder<Thing> builder) => builder.Property(t => t.Name).HasMaxLength(1);
    }
}
