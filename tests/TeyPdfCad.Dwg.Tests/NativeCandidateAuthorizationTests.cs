using TeyPdfCad.Core.Recognition;
using TeyPdfCad.Dwg;
using Xunit;

namespace TeyPdfCad.Dwg.Tests;

public sealed class NativeCandidateAuthorizationTests
{
    [Fact]
    public void Null_authorization_is_probe_mode_and_allows_candidate_emission()
    {
        var result = NativeCandidateAuthorization.GetAuthorizedCandidateIds(
            TwoSourceCandidatePlan(),
            pageNumber: 1,
            authorizedSuppressedSources: null);

        Assert.Null(result);
        Assert.True(NativeCandidateAuthorization.ShouldEmit(result, "candidate-1"));
    }

    [Fact]
    public void Empty_final_authorization_emits_no_native_candidates()
    {
        var result = NativeCandidateAuthorization.GetAuthorizedCandidateIds(
            TwoSourceCandidatePlan(),
            pageNumber: 1,
            authorizedSuppressedSources: new HashSet<PageSourceRef>());

        Assert.NotNull(result);
        Assert.Empty(result);
        Assert.False(NativeCandidateAuthorization.ShouldEmit(result, "candidate-1"));
    }

    [Fact]
    public void Full_candidate_source_authorization_allows_exact_candidate()
    {
        var result = NativeCandidateAuthorization.GetAuthorizedCandidateIds(
            TwoSourceCandidatePlan(),
            pageNumber: 1,
            authorizedSuppressedSources: new HashSet<PageSourceRef>
            {
                new(1, "source-a"),
                new(1, "source-b")
            });

        Assert.Equal(["candidate-1"], result!.OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public void Partial_candidate_source_authorization_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            NativeCandidateAuthorization.GetAuthorizedCandidateIds(
                TwoSourceCandidatePlan(),
                pageNumber: 1,
                authorizedSuppressedSources: new HashSet<PageSourceRef>
                {
                    new(1, "source-a")
                }));
    }

    [Fact]
    public void Authorization_is_page_scoped_when_raw_source_ids_repeat()
    {
        var result = NativeCandidateAuthorization.GetAuthorizedCandidateIds(
            TwoSourceCandidatePlan(),
            pageNumber: 1,
            authorizedSuppressedSources: new HashSet<PageSourceRef>
            {
                new(2, "source-a"),
                new(2, "source-b")
            });

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void Unknown_authorized_source_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            NativeCandidateAuthorization.GetAuthorizedCandidateIds(
                TwoSourceCandidatePlan(),
                pageNumber: 1,
                authorizedSuppressedSources: new HashSet<PageSourceRef>
                {
                    new(1, "not-eligible")
                }));
    }

    private static SourceReplacementPlan TwoSourceCandidatePlan()
        => new(
            EligibleSourceIds: ["source-a", "source-b"],
            PreservedSourceIds: [],
            DeferredCandidateKeys: [],
            SourceCoverageMap: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["source-a"] = ["candidate-1"],
                ["source-b"] = ["candidate-1"]
            },
            Conflicts: [],
            Residuals: []);
}
