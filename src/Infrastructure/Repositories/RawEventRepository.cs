using Application.Abstractions;
using Domain;

namespace Infrastructure.Repositories;

public sealed class RawEventRepository(IngestionDbContext db) : IRawEventRepository
{
    public void AddRange(IEnumerable<RawEvent> events) => db.RawEvents.AddRange(events);
}
