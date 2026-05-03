using BasicSubscriptionBot.Api.Data;
using BasicSubscriptionBot.Api.Models;

namespace BasicSubscriptionBot.Api.Services;

public class EchoService
{
    private readonly EchoRepository _repo;
    public EchoService(EchoRepository repo) => _repo = repo;

    public Task<EchoRecord> RecordAsync(string message) => _repo.InsertAsync(message);
    public Task<EchoRecord?> GetAsync(long id) => _repo.GetAsync(id);
}
