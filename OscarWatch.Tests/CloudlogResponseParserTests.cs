using OscarWatch.Core.Cloudlog;

namespace OscarWatch.Tests;

/// <summary>
/// **Validates: Requirements 10.1, 10.2, 10.4, 10.5**
///
/// Example and edge-case tests verifying that <see cref="CloudlogResponseParser"/>
/// correctly parses success status, reason fields, and handles null/malformed input.
/// </summary>
public sealed class CloudlogResponseParserTests
{
    /// <summary>
    /// Valid JSON with status "success" sets the success output to true and TryParse returns true.
    /// </summary>
    [Fact]
    public void Valid_json_with_status_success_sets_success_to_true()
    {
        const string json = """{"status":"success"}""";

        var result = CloudlogResponseParser.TryParse(json, out var success, out _);

        Assert.True(result);
        Assert.True(success);
    }

    [Fact]
    public void Valid_json_with_status_created_sets_success_to_true()
    {
        const string json = """{"status":"created","imported_count":1}""";

        var result = CloudlogResponseParser.TryParse(json, out var success, out _);

        Assert.True(result);
        Assert.True(success);
    }

    [Fact]
    public void Created_with_nothing_imported_is_a_failure_and_keeps_the_message()
    {
        const string json = """{"status":"created","imported_count":0,"messages":["Date/Time: 2026-10-16 16:41 Callsign: DL7NDR Band: 2m Duplicate "]}""";

        var result = CloudlogResponseParser.TryParse(json, out var success, out var reason);

        Assert.True(result);
        Assert.False(success);
        Assert.Equal("Date/Time: 2026-10-16 16:41 Callsign: DL7NDR Band: 2m Duplicate", reason);
    }

    /// <summary>
    /// Response containing a "reason" field populates the reason output with a trimmed value.
    /// </summary>
    [Fact]
    public void Response_with_reason_field_populates_reason_output()
    {
        const string json = """{"status":"failed","reason":"  some error  "}""";

        var result = CloudlogResponseParser.TryParse(json, out var success, out var reason);

        Assert.True(result);
        Assert.False(success);
        Assert.Equal("some error", reason);
    }

    /// <summary>
    /// Null and whitespace inputs cause TryParse to return false.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Null_or_whitespace_input_returns_false(string? input)
    {
        var result = CloudlogResponseParser.TryParse(input, out var success, out var reason);

        Assert.False(result);
        Assert.False(success);
        Assert.Null(reason);
    }

    /// <summary>
    /// Malformed JSON returns false without throwing an exception.
    /// </summary>
    [Fact]
    public void Malformed_json_returns_false_without_exception()
    {
        const string malformed = "not json at all{{{";

        var exception = Record.Exception(() =>
            CloudlogResponseParser.TryParse(malformed, out _, out _));

        Assert.Null(exception);

        var result = CloudlogResponseParser.TryParse(malformed, out var success, out var reason);

        Assert.False(result);
        Assert.False(success);
        Assert.Null(reason);
    }
}
