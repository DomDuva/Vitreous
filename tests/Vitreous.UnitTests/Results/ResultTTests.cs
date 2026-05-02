namespace Vitreous.UnitTests.Results;

public sealed class ResultTTests
{
    private static readonly Error _error = new("ERR", "something went wrong");

    // ── Ok ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Ok_IsSuccess_True_IsFailure_False()
    {
        var result = Result<int>.Ok(42);
        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
    }

    [Fact]
    public void Ok_Value_ReturnsWrappedValue()
    {
        Result<string>.Ok("hello").Value.Should().Be("hello");
    }

    [Fact]
    public void Ok_AccessingError_ThrowsInvalidOperationException()
    {
        var result = Result<int>.Ok(1);
        var act = () => result.Error;
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Fail ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Fail_IsFailure_True_IsSuccess_False()
    {
        var result = Result<int>.Fail(_error);
        result.IsFailure.Should().BeTrue();
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Fail_Error_ReturnsWrappedError()
    {
        Result<int>.Fail(_error).Error.Should().Be(_error);
    }

    [Fact]
    public void Fail_AccessingValue_ThrowsInvalidOperationException()
    {
        var result = Result<int>.Fail(_error);
        var act = () => result.Value;
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Match ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Match_OnSuccess_CallsSuccessBranch()
    {
        var output = Result<int>.Ok(7).Match(v => v * 2, _ => -1);
        output.Should().Be(14);
    }

    [Fact]
    public void Match_OnFailure_CallsFailureBranch()
    {
        var output = Result<int>.Fail(_error).Match(_ => "ok", e => e.Code);
        output.Should().Be("ERR");
    }

    [Fact]
    public void Match_OnSuccess_FailureBranchNotCalled()
    {
        var called = false;
        Result<int>.Ok(1).Match(v => v, _ => { called = true; return -1; });
        called.Should().BeFalse();
    }

    [Fact]
    public void Match_OnFailure_SuccessBranchNotCalled()
    {
        var called = false;
        Result<int>.Fail(_error).Match(_ => { called = true; return 0; }, e => -1);
        called.Should().BeFalse();
    }

    // ── MatchAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task MatchAsync_OnSuccess_CallsSuccessBranch()
    {
        var output = await Result<int>.Ok(5).MatchAsync(
            v => ValueTask.FromResult(v + 1),
            _ => ValueTask.FromResult(-1));
        output.Should().Be(6);
    }

    [Fact]
    public async Task MatchAsync_OnFailure_CallsFailureBranch()
    {
        var output = await Result<int>.Fail(_error).MatchAsync(
            _ => ValueTask.FromResult("ok"),
            e => ValueTask.FromResult(e.Code));
        output.Should().Be("ERR");
    }

    // ── Map ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Map_OnSuccess_TransformsValue()
    {
        var result = Result<int>.Ok(3).Map(v => v.ToString());
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("3");
    }

    [Fact]
    public void Map_OnFailure_ReturnsSameErrorWithoutCalling()
    {
        var called = false;
        var result = Result<int>.Fail(_error).Map(v => { called = true; return v.ToString(); });

        called.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(_error);
    }

    // ── MapAsync ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task MapAsync_OnSuccess_TransformsValue()
    {
        var result = await Result<int>.Ok(10).MapAsync(v => ValueTask.FromResult(v * 2));
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(20);
    }

    [Fact]
    public async Task MapAsync_OnFailure_ReturnsSameError()
    {
        var result = await Result<int>.Fail(_error).MapAsync(v => ValueTask.FromResult(v * 2));
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(_error);
    }

    // ── Bind ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Bind_OnSuccess_ChainsToSuccessResult()
    {
        var result = Result<int>.Ok(5).Bind(v => Result<string>.Ok($"v={v}"));
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("v=5");
    }

    [Fact]
    public void Bind_OnSuccess_ChainsToFailureResult()
    {
        var result = Result<int>.Ok(5).Bind(_ => Result<string>.Fail(_error));
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(_error);
    }

    [Fact]
    public void Bind_OnFailure_ShortCircuitsWithoutCallingBinder()
    {
        var called = false;
        var result = Result<int>.Fail(_error).Bind(v =>
        {
            called = true;
            return Result<string>.Ok("x");
        });
        called.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(_error);
    }

    // ── BindAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task BindAsync_OnSuccess_ChainsResult()
    {
        var result = await Result<int>.Ok(3)
            .BindAsync(v => ValueTask.FromResult(Result<int>.Ok(v * v)));
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(9);
    }

    [Fact]
    public async Task BindAsync_OnFailure_ShortCircuits()
    {
        var called = false;
        var result = await Result<int>.Fail(_error).BindAsync(v =>
        {
            called = true;
            return ValueTask.FromResult(Result<int>.Ok(v));
        });
        called.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
    }

    // ── Implicit conversions ──────────────────────────────────────────────────

    [Fact]
    public void ImplicitFromValue_CreatesSuccessResult()
    {
        Result<int> result = 42;
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void ImplicitFromError_CreatesFailureResult()
    {
        Result<int> result = _error;
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(_error);
    }

    // ── ToString ──────────────────────────────────────────────────────────────

    [Fact]
    public void ToString_OnSuccess_ReturnsOkFormat()
    {
        Result<int>.Ok(99).ToString().Should().Be("Ok(99)");
    }

    [Fact]
    public void ToString_OnFailure_ReturnsFailFormat()
    {
        Result<int>.Fail(_error).ToString().Should().StartWith("Fail(");
    }
}
