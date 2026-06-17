using System.Text.Json;

namespace BeServer.Services;

public class RetrievalComparisonService
{
    public RetrievalComparisonSummary Compare(
        IReadOnlyList<RagChunkResult> left,
        IReadOnlyList<RagChunkResult> right,
        int leftLatencyMs,
        int rightLatencyMs)
    {
        var leftRanks = left.Select((r, i) => (Key: Key(r), Rank: i + 1)).ToDictionary(x => x.Key, x => x.Rank);
        var rightRanks = right.Select((r, i) => (Key: Key(r), Rank: i + 1)).ToDictionary(x => x.Key, x => x.Rank);
        var shared = leftRanks.Keys.Intersect(rightRanks.Keys).ToList();
        var sourceOverlap = left.Select(r => r.SourceId).Distinct().Intersect(right.Select(r => r.SourceId).Distinct()).Count();
        var rankDeltas = shared
            .Select(key => new RetrievalRankDelta(key.SourceId, key.ChunkIndex, leftRanks[key], rightRanks[key], leftRanks[key] - rightRanks[key]))
            .OrderByDescending(d => Math.Abs(d.Delta))
            .ToList();

        return new RetrievalComparisonSummary(
            shared.Count,
            sourceOverlap,
            rankDeltas,
            right.Count - left.Count,
            rightLatencyMs - leftLatencyMs);
    }

    public string Snapshot(IReadOnlyList<RagChunkResult> results)
    {
        var payload = results.Select((r, i) => new RetrievalResultSnapshot(
            i + 1,
            r.SourceId,
            r.ChunkIndex,
            r.RetrievalVersionId,
            r.Text.Length <= 240 ? r.Text : r.Text[..240]));
        return JsonSerializer.Serialize(payload);
    }

    private static RetrievalChunkKey Key(RagChunkResult r) => new(r.SourceId, r.ChunkIndex);
}

public readonly record struct RetrievalChunkKey(string SourceId, int ChunkIndex);
public record RetrievalRankDelta(string SourceId, int ChunkIndex, int RankA, int RankB, int Delta);
public record RetrievalComparisonSummary(
    int OverlapAtK,
    int SourceOverlap,
    List<RetrievalRankDelta> RankDeltas,
    int ResultCountDelta,
    int LatencyDeltaMs);
public record RetrievalResultSnapshot(
    int Rank,
    string SourceId,
    int ChunkIndex,
    string? RetrievalVersionId,
    string TextPreview);
