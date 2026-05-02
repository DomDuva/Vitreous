namespace Vitreous.UnitTests.Results;

public sealed class ErrorTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var ext = new Dictionary<string, object?> { ["key"] = 1 };
        var error = new Error("CODE", "some message", ext);

        error.Code.Should().Be("CODE");
        error.Message.Should().Be("some message");
        error.Extensions.Should().BeSameAs(ext);
    }

    [Fact]
    public void Constructor_ExtensionsDefaultsToNull()
    {
        var error = new Error("CODE", "msg");
        error.Extensions.Should().BeNull();
    }

    // ── NotFound ──────────────────────────────────────────────────────────────

    [Fact]
    public void NotFound_UsesDefaultCode()
    {
        var error = Error.NotFound("not here");
        error.Code.Should().Be("NOT_FOUND");
        error.Message.Should().Be("not here");
        error.Extensions.Should().BeNull();
    }

    [Fact]
    public void NotFound_WithCustomCode_UsesCustomCode()
    {
        var error = Error.NotFound("not here", "MISSING");
        error.Code.Should().Be("MISSING");
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public void Validation_NoFailures_HasNoExtensions()
    {
        var error = Error.Validation("bad input");
        error.Code.Should().Be("VALIDATION");
        error.Message.Should().Be("bad input");
        error.Extensions.Should().BeNull();
    }

    [Fact]
    public void Validation_EmptyFailures_HasNoExtensions()
    {
        var error = Error.Validation("bad input", new Dictionary<string, string[]>());
        error.Extensions.Should().BeNull();
    }

    [Fact]
    public void Validation_WithFailures_PopulatesExtensions()
    {
        var failures = new Dictionary<string, string[]>
        {
            ["Name"] = ["Required"],
            ["Email"] = ["Invalid format"]
        };
        var error = Error.Validation("bad input", failures);

        error.Extensions.Should().ContainKey("failures");
        error.Extensions!["failures"].Should().BeSameAs(failures);
    }

    [Fact]
    public void Validation_WithCustomCode_UsesCustomCode()
    {
        var error = Error.Validation("bad input", code: "MY_VALIDATION");
        error.Code.Should().Be("MY_VALIDATION");
    }

    // ── Unauthorized ──────────────────────────────────────────────────────────

    [Fact]
    public void Unauthorized_UsesDefaultCode()
    {
        var error = Error.Unauthorized("access denied");
        error.Code.Should().Be("UNAUTHORIZED");
        error.Message.Should().Be("access denied");
    }

    [Fact]
    public void Unauthorized_WithCustomCode_UsesCustomCode()
    {
        var error = Error.Unauthorized("denied", "AUTH_REQUIRED");
        error.Code.Should().Be("AUTH_REQUIRED");
    }

    // ── Conflict ──────────────────────────────────────────────────────────────

    [Fact]
    public void Conflict_UsesDefaultCode()
    {
        var error = Error.Conflict("duplicate");
        error.Code.Should().Be("CONFLICT");
        error.Message.Should().Be("duplicate");
    }

    [Fact]
    public void Conflict_WithCustomCode_UsesCustomCode()
    {
        var error = Error.Conflict("duplicate", "DUPE");
        error.Code.Should().Be("DUPE");
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    [Fact]
    public void Internal_NoException_HasNoExtensions()
    {
        var error = Error.Internal("boom");
        error.Code.Should().Be("INTERNAL");
        error.Message.Should().Be("boom");
        error.Extensions.Should().BeNull();
    }

    [Fact]
    public void Internal_WithException_PopulatesExtensions()
    {
        var ex = new InvalidOperationException("inner message");
        var error = Error.Internal("boom", ex);

        error.Extensions.Should().ContainKey("exceptionType");
        error.Extensions.Should().ContainKey("exceptionMessage");
        error.Extensions!["exceptionType"].Should().Be(nameof(InvalidOperationException));
        error.Extensions!["exceptionMessage"].Should().Be("inner message");
    }

    [Fact]
    public void Internal_WithCustomCode_UsesCustomCode()
    {
        var error = Error.Internal("boom", code: "FATAL");
        error.Code.Should().Be("FATAL");
    }

    // ── Custom ────────────────────────────────────────────────────────────────

    [Fact]
    public void Custom_SetsAllFields()
    {
        var ext = new Dictionary<string, object?> { ["x"] = 42 };
        var error = Error.Custom("MY_CODE", "my message", ext);

        error.Code.Should().Be("MY_CODE");
        error.Message.Should().Be("my message");
        error.Extensions.Should().ContainKey("x");
    }

    [Fact]
    public void Custom_NoExtensions_IsNull()
    {
        var error = Error.Custom("X", "y");
        error.Extensions.Should().BeNull();
    }

    // ── ToString ──────────────────────────────────────────────────────────────

    [Fact]
    public void ToString_ReturnsExpectedFormat()
    {
        var error = new Error("CODE", "some message");
        error.ToString().Should().Be("[CODE] some message");
    }

    // ── Record equality ───────────────────────────────────────────────────────

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        var a = new Error("X", "y");
        var b = new Error("X", "y");
        a.Should().Be(b);
    }

    [Fact]
    public void RecordEquality_DifferentCode_AreNotEqual()
    {
        new Error("A", "msg").Should().NotBe(new Error("B", "msg"));
    }

    [Fact]
    public void RecordEquality_DifferentMessage_AreNotEqual()
    {
        new Error("X", "a").Should().NotBe(new Error("X", "b"));
    }
}
