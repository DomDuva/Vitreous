namespace Vitreous.UnitTests.Results;

public sealed class ResultTests
{
    private static readonly Error _error = new("ERR", "failed");

    // ── Result.Success singleton ───────────────────────────────────────────────

    [Fact]
    public void Success_IsSuccess_True_IsFailure_False()
    {
        Result.Success.IsSuccess.Should().BeTrue();
        Result.Success.IsFailure.Should().BeFalse();
    }

    [Fact]
    public void Success_AccessingError_Throws()
    {
        var act = () => Result.Success.Error;
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Fail ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Fail_IsFailure_True_IsSuccess_False()
    {
        var result = Result.Fail(_error);
        result.IsFailure.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Fail_Error_ReturnsWrappedError()
    {
        Result.Fail(_error).Error.Should().Be(_error);
    }

    // ── Match ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Match_OnSuccess_CallsSuccessBranch()
    {
        var output = Result.Success.Match(() => "ok", _ => "fail");
        output.Should().Be("ok");
    }

    [Fact]
    public void Match_OnFailure_CallsFailureBranch()
    {
        var output = Result.Fail(_error).Match(() => "ok", e => e.Code);
        output.Should().Be("ERR");
    }

    [Fact]
    public void Match_OnSuccess_FailureBranchNotCalled()
    {
        var called = false;
        Result.Success.Match(() => 0, _ => { called = true; return -1; });
        called.Should().BeFalse();
    }

    [Fact]
    public void Match_OnFailure_SuccessBranchNotCalled()
    {
        var called = false;
        Result.Fail(_error).Match(() => { called = true; return 0; }, _ => -1);
        called.Should().BeFalse();
    }

    // ── MatchAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_OnSuccess_CallsSuccessBranch()
    {
        var output = await Result.Success.MatchAsync(
            () => ValueTask.FromResult("ok"),
            _ => ValueTask.FromResult("fail"));
        output.Should().Be("ok");
    }

    [Fact]
    public async Task MatchAsync_OnFailure_CallsFailureBranch()
    {
        var output = await Result.Fail(_error).MatchAsync(
            () => ValueTask.FromResult("ok"),
            e => ValueTask.FromResult(e.Code));
        output.Should().Be("ERR");
    }

    // ── Implicit conversions ──────────────────────────────────────────────────

    [Fact]
    public void ImplicitFromError_CreatesFailureResult()
    {
        Result result = _error;
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(_error);
    }

    // ── ToResult<T> ───────────────────────────────────────────────────────────

    [Fact]
    public void ToResultT_OnSuccess_MapsToOkWithValue()
    {
        var result = Result.Success.ToResult(42);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void ToResultT_OnFailure_MapsToFailWithSameError()
    {
        var result = Result.Fail(_error).ToResult(42);
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(_error);
    }

    [Fact]
    public void ToResultT_OnFailure_SuccessValueIgnored()
    {
        // The success value supplied should not appear in the result
        var result = Result.Fail(_error).ToResult(999);
        result.IsFailure.Should().BeTrue();
    }

    // ── ToString ──────────────────────────────────────────────────────────────

    [Fact]
    public void ToString_OnSuccess_ReturnsOk()
    {
        Result.Success.ToString().Should().Be("Ok");
    }

    [Fact]
    public void ToString_OnFailure_ReturnsFailFormat()
    {
        Result.Fail(_error).ToString().Should().StartWith("Fail(");
    }
}
