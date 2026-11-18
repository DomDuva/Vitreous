using System.Text.Json;
using Vitreous.DevUI;
using Vitreous.DevUI.Tracing;

namespace Vitreous.UnitTests.DevUI;

public sealed class DevUISerializerTests
{
    public sealed record Credentials(string User, string Password, string ApiKey, string AccessToken);

    public sealed record WithSensitiveParameter(string Name, [Sensitive] decimal Salary);

    public sealed class WithSensitiveProperty
    {
        public string Name { get; init; } = "";

        [Sensitive]
        public Guid InternalId { get; init; }
    }

    public sealed record Nested(string Label, Credentials Inner);

    public sealed record Custom(string Iban, string Note);

    private static JsonElement Serialize<T>(T value, Action<VitreousDevUIOptions>? configure = null)
    {
        var options = new VitreousDevUIOptions();
        configure?.Invoke(options);
        return JsonDocument.Parse(new DevUISerializer(options).SerializePayload(value)!).RootElement;
    }

    [Fact]
    public void DefaultNames_AreMasked_BySubstring_CaseInsensitively()
    {
        var json = Serialize(new Credentials("ada", "p@ss", "key-123", "tok-456"));

        json.GetProperty("user").GetString().Should().Be("ada");
        json.GetProperty("password").GetString().Should().Be("***");
        json.GetProperty("apiKey").GetString().Should().Be("***");
        json.GetProperty("accessToken").GetString().Should().Be("***", "'token' matches by substring");
    }

    [Fact]
    public void SensitiveOnAPositionalParameter_IsMasked_WhateverTheType()
    {
        var json = Serialize(new WithSensitiveParameter("ada", 123_456m));

        json.GetProperty("name").GetString().Should().Be("ada");
        json.GetProperty("salary").GetString().Should().Be("***");
    }

    [Fact]
    public void SensitiveOnAProperty_IsMasked()
    {
        var json = Serialize(new WithSensitiveProperty { Name = "ada", InternalId = Guid.NewGuid() });

        json.GetProperty("internalId").GetString().Should().Be("***");
    }

    [Fact]
    public void NestedObjects_AreMaskedToo()
    {
        var json = Serialize(new Nested("outer", new Credentials("ada", "p@ss", "k", "t")));

        json.GetProperty("label").GetString().Should().Be("outer");
        json.GetProperty("inner").GetProperty("password").GetString().Should().Be("***");
    }

    [Fact]
    public void ConfiguredNames_CanBeAddedAndDefaultsRemoved()
    {
        var json = Serialize(new Custom("DE89 3704", "hello"), o => o.RedactedPropertyNames.Add("iban"));
        json.GetProperty("iban").GetString().Should().Be("***");

        var unmasked = Serialize(new Credentials("ada", "p@ss", "k", "t"), o => o.RedactedPropertyNames.Clear());
        unmasked.GetProperty("password").GetString().Should().Be("p@ss");
    }

    [Fact]
    public void Cycles_DoNotThrow()
    {
        var node = new Node { Name = "a" };
        node.Next = node;

        new DevUISerializer(new VitreousDevUIOptions()).SerializePayload(node).Should().Contain("\"name\":\"a\"");
    }

    public sealed class Node
    {
        public string Name { get; set; } = "";
        public Node? Next { get; set; }
    }
}
