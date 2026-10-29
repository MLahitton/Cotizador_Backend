using Application.Projects.GetProjectIdeaPhoto;
using Domain.PublicExperience;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Repositories;

public sealed class ProjectIdeaPhotoRepository(ApplicationDbContext context) : IProjectIdeaPhotoRepository
{
    public async Task<PublicExperienceIdeaPhoto?> FindAsync(Guid projectId, Guid clientId, CancellationToken ct)
    {
        var rows = await context.Set<PublicExperienceSubmission>().AsNoTracking()
            .Where(s => s.ProjectId == projectId).Include(s => s.IdeaPhoto).Take(2).ToListAsync(ct);
        if (rows.Count == 0) return null;
        if (rows.Count != 1 || rows[0].ClientId != clientId
            || (rows[0].IdeaPhoto is { } photo && photo.SubmissionId != rows[0].Id))
            throw new InvalidDataException("Inconsistent project photo relationship.");
        return rows[0].IdeaPhoto;
    }
}
