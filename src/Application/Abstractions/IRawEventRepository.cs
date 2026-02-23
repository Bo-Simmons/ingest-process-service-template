using Domain;

namespace Application.Abstractions;

public interface IRawEventRepository
{
    void AddRange(IEnumerable<RawEvent> events);
}
