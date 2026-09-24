namespace PhoneBridge.Discovery;

public enum DiscoveryChangeKind { Added, Updated, Removed, Rejected, Status }
public sealed record DiscoveryChange(DiscoveryChangeKind Kind, string Id, DeviceCandidate? Candidate, string Reason);

// Called by the single discovery event loop. No UI, networking or credentials here.
public sealed class CandidateRegistry
{
    private readonly Dictionary<string, DeviceCandidate> candidates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> missedScans = new(StringComparer.Ordinal);
    public IReadOnlyList<DeviceCandidate> Snapshot => candidates.Values.ToArray();

    public DiscoveryChange? Apply(ServiceAdvertisement advertisement)
    {
        var key = CandidateParser.Key(CandidateSource.Mdns, advertisement.ServiceKey);
        if (CandidateParser.TryParse(advertisement, out var candidate, out var reason)) return Upsert(candidate!);
        // Invalid replacement cannot leave an older HTTPS endpoint selectable.
        return Remove(key, reason) ?? new(DiscoveryChangeKind.Rejected, key, null, reason);
    }

    public DiscoveryChange? AddManual(string address, int port) =>
        CandidateParser.TryManual(address, port, out var candidate) ? Upsert(candidate!) :
            new(DiscoveryChangeKind.Rejected, "manual", null, "invalid-endpoint");

    public DiscoveryChange? RemoveService(string serviceKey, string reason = "service-removed") =>
        Remove(CandidateParser.Key(CandidateSource.Mdns, serviceKey), reason);

    public IReadOnlyList<DiscoveryChange> CompleteScan(IReadOnlySet<string> observed)
    {
        var changes = new List<DiscoveryChange>();
        foreach (var candidate in candidates.Values.Where(c => c.Source == CandidateSource.Mdns).ToArray())
        {
            if (observed.Contains(candidate.Id)) { missedScans.Remove(candidate.Id); continue; }
            var misses = missedScans.GetValueOrDefault(candidate.Id) + 1;
            missedScans[candidate.Id] = misses;
            if (misses >= 2) changes.Add(Remove(candidate.Id, "not-rediscovered")!);
        }
        return changes;
    }

    public IReadOnlyList<DiscoveryChange> Clear(string reason, CandidateSource? source = null)
    {
        var keys = candidates.Values.Where(c => source is null || c.Source == source).Select(c => c.Id).ToArray();
        return keys.Select(key => Remove(key, reason)!).ToArray();
    }

    private DiscoveryChange? Upsert(DeviceCandidate candidate)
    {
        if (candidates.TryGetValue(candidate.Id, out var previous))
        {
            if (previous.DisplayName == candidate.DisplayName && previous.Endpoints.SequenceEqual(candidate.Endpoints) &&
                previous.Protocol == candidate.Protocol && previous.DeviceIdHint == candidate.DeviceIdHint && previous.Pairing == candidate.Pairing) return null;
            candidates[candidate.Id] = candidate;
            return new(DiscoveryChangeKind.Updated, candidate.Id, candidate, "advertisement-updated");
        }
        if (candidates.Count >= CandidateParser.MaxCandidates)
            return new(DiscoveryChangeKind.Rejected, candidate.Id, null, "candidate-limit");
        candidates.Add(candidate.Id, candidate);
        return new(DiscoveryChangeKind.Added, candidate.Id, candidate, "unverified-candidate");
    }

    private DiscoveryChange? Remove(string key, string reason)
    {
        missedScans.Remove(key);
        return candidates.Remove(key) ? new(DiscoveryChangeKind.Removed, key, null, reason) : null;
    }
}
