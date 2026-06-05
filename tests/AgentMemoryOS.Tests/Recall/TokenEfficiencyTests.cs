namespace AgentMemoryOS.Tests.Recall;

using System;
using System.Collections.Generic;
using System.Linq;
using AgentMemoryOS.Abstractions;
using AgentMemoryOS.Recall;

[TestClass]
public sealed class VectorMathTests
{
    [TestMethod]
    public void IdenticalVectorsHaveCosineOne()
    {
        float[] a = [1f, 2f, 3f];
        Assert.AreEqual(1.0, VectorMath.CosineSimilarity(a, a), 1e-6);
    }

    [TestMethod]
    public void OrthogonalVectorsHaveCosineZero()
    {
        float[] a = [1f, 0f];
        float[] b = [0f, 1f];
        Assert.AreEqual(0.0, VectorMath.CosineSimilarity(a, b), 1e-6);
    }

    [TestMethod]
    public void OppositeVectorsHaveCosineMinusOne()
    {
        float[] a = [1f, 1f];
        float[] b = [-1f, -1f];
        Assert.AreEqual(-1.0, VectorMath.CosineSimilarity(a, b), 1e-6);
    }

    [TestMethod]
    public void MismatchedLengthsReturnZero()
    {
        float[] a = [1f, 2f];
        float[] b = [1f];
        Assert.AreEqual(0.0, VectorMath.CosineSimilarity(a, b), 1e-6);
    }
}

[TestClass]
public sealed class MemoryGateTests
{
    [TestMethod]
    public void SocialClosersAreTrivial()
    {
        Assert.IsTrue(MemoryGate.IsTrivial("thanks"));
        Assert.IsTrue(MemoryGate.IsTrivial("Thank you!"));
        Assert.IsTrue(MemoryGate.IsTrivial("ok"));
        Assert.IsTrue(MemoryGate.IsTrivial("   "));
    }

    [TestMethod]
    public void SubstantiveContentIsNotTrivial()
    {
        Assert.IsFalse(MemoryGate.IsTrivial("Repo X gates on a React score below 50."));
    }

    [TestMethod]
    public void GateFiltersBelowThresholdAndOrdersByScore()
    {
        ScoredCandidate[] candidates =
        [
            Candidate("low", 0.40),
            Candidate("high", 0.95),
            Candidate("mid", 0.80),
        ];

        var gated = MemoryGate.Gate(candidates, minScore: 0.5, maxItems: 10);

        string[] expected = ["high", "mid"];
        CollectionAssert.AreEqual(expected, gated.Select(candidate => candidate.Content).ToList());
    }

    [TestMethod]
    public void GateRespectsMaxItems()
    {
        ScoredCandidate[] candidates =
        [
            Candidate("a", 0.9),
            Candidate("b", 0.8),
            Candidate("c", 0.7),
        ];

        var gated = MemoryGate.Gate(candidates, minScore: 0.0, maxItems: 2);

        Assert.AreEqual(2, gated.Count);
        Assert.AreEqual("a", gated[0].Content);
    }

    [TestMethod]
    public void DedupCollapsesExactDuplicatesKeepingHighestScore()
    {
        ScoredCandidate[] candidates =
        [
            Candidate("Repo gates on score 50", 0.7),
            Candidate("repo gates on score 50", 0.9),
        ];

        var result = MemoryGate.Dedup(candidates, alreadySeen: new HashSet<string>(), cosineThreshold: 0.92);

        Assert.AreEqual(1, result.Kept.Count);
        Assert.AreEqual(0.9, result.Kept[0].Score, 1e-9);
    }

    [TestMethod]
    public void DedupRemovesAlreadySeen()
    {
        var seenKey = MemoryGate.NormalizeKey("known fact");
        ScoredCandidate[] candidates = [Candidate("Known fact", 0.9)];

        var result = MemoryGate.Dedup(candidates, alreadySeen: new HashSet<string> { seenKey }, cosineThreshold: 0.92);

        Assert.AreEqual(0, result.Kept.Count);
    }

    [TestMethod]
    public void DedupCollapsesSemanticNearDuplicatesByCosine()
    {
        var a = Candidate("alpha", 0.9, [1f, 0f]);
        var b = Candidate("beta", 0.8, [0.99f, 0.01f]);

        var result = MemoryGate.Dedup([a, b], new HashSet<string>(), cosineThreshold: 0.92);

        Assert.AreEqual(1, result.Kept.Count);
        Assert.AreEqual("alpha", result.Kept[0].Content);
    }

    private static ScoredCandidate Candidate(string content, double score, float[]? embedding = null)
    {
        ReadOnlyMemory<float>? vector = embedding is null ? null : new ReadOnlyMemory<float>(embedding);
        return new ScoredCandidate(content, score, RecallSource.Fact, vector);
    }
}
