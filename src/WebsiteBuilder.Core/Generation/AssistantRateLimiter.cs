using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace WebsiteBuilder.Core.Generation;

public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    /// <summary>Free-tier ceiling on assistant requests per tenant per rolling day. Plans arrive with WB-7.</summary>
    public int RequestsPerDay { get; set; } = 30;
}

/// <summary>Gate for per-tenant assistant usage. Returns false when the tenant is over its allowance.</summary>
public interface IAssistantRateLimiter
{
    bool TryAcquire(Guid tenantId);
}

/// <summary>
/// In-memory rolling-day limiter. Adequate for a single instance / MVP; a durable per-plan limiter
/// replaces it with billing (WB-7). Resets on restart, which only ever grants extra allowance.
/// </summary>
public sealed class InMemoryAssistantRateLimiter(IOptions<AssistantOptions> options, TimeProvider? timeProvider = null)
    : IAssistantRateLimiter
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> _hits = new();

    public bool TryAcquire(Guid tenantId)
    {
        var limit = options.Value.RequestsPerDay;
        var now = _time.GetUtcNow();
        var cutoff = now - TimeSpan.FromDays(1);
        var queue = _hits.GetOrAdd(tenantId, _ => new Queue<DateTimeOffset>());

        lock (queue)
        {
            while (queue.Count > 0 && queue.Peek() < cutoff)
            {
                queue.Dequeue();
            }

            if (queue.Count >= limit)
            {
                return false;
            }

            queue.Enqueue(now);
            return true;
        }
    }
}
