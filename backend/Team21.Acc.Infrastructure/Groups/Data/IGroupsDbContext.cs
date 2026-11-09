using Team21.Acc.Infrastructure.Common;

namespace Team21.Acc.Infrastructure.Groups.Data;

/// <summary>What ACC-06 services get injected instead of GroupsDbContext.</summary>
public interface IGroupsDbContext : ITeamDbContext
{
    // DbSet<Group> Groups { get; set; }
}
