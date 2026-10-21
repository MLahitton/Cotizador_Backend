using System.Data;
using System.Data.Common;
using Infrastructure.Persistence;
using Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace CotizadorBackend.Tests.Infrastructure.Persistence;

public sealed class ProjectDiagnosisRepositoryTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Query_OnlyProjectsSourceColumns_IsFilteredLimitedAndUntracked(int count)
    {
        var reader = new SourceReader(count);
        await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .AddInterceptors(new NoConnection(), reader).Options);
        var projectId = Guid.NewGuid();
        var result = await new PublicExperienceSubmissionRepository(context)
            .FindByProjectIdAsync(projectId, TestContext.Current.CancellationToken);
        Assert.Equal(count, result.Count);
        Assert.All(result, x => Assert.Equal("{}", x.AnswersJson));
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Single(reader.Sql);
        var sql = reader.Sql[0];
        Assert.Contains("project_id", sql);
        Assert.Contains("SNG_WEB", sql);
        Assert.Contains("LIMIT", sql);
        Assert.Contains(projectId, reader.Parameters);
        Assert.Contains(2, reader.Parameters);
        Assert.DoesNotContain("JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("files", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client_", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pre_quote", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("requirement_id", sql, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("SELECT", sql);
    }

    private sealed class NoConnection : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class SourceReader(int count) : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public List<object> Parameters { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText);
            Parameters.AddRange(command.Parameters.Cast<DbParameter>().Select(x => x.Value!));
            var table = new DataTable();
            foreach (var column in new[] { "submission_id", "source", "schema_version", "answers" })
                table.Columns.Add(column, typeof(string));
            for (var i = 0; i < count; i++) table.Rows.Add("submission-" + i, "SNG_WEB", "sng-public-experience-v1", "{}");
            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
        }
    }
}
