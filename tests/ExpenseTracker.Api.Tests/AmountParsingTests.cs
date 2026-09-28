using System.Globalization;
using ExpenseTracker.Api.Import;

namespace ExpenseTracker.Api.Tests;

public class AmountParsingTests
{
    [Theory]
    [InlineData("-42.35", "-42.35")]
    [InlineData("-42,35", "-42.35")]      // comma decimal
    [InlineData("2500", "2500")]
    [InlineData("1,234.56", "1234.56")]   // US thousands
    [InlineData("1.234,56", "1234.56")]   // European thousands
    [InlineData("1 234,56", "1234.56")]   // space thousands
    [InlineData("1 234,56", "1234.56")] // non-breaking space thousands
    [InlineData("-0.5", "-0.5")]
    public void Parses(string input, string expected)
    {
        Assert.True(CsvTransactionImporter.TryParseAmount(input, out var amount));
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("€12.00")]
    [InlineData("1,234")]     // ambiguous: 1234 or 1.234 -> more than 2 decimals, rejected
    [InlineData("12.345")]
    [InlineData("1e3")]
    public void Rejects(string? input)
    {
        Assert.False(CsvTransactionImporter.TryParseAmount(input, out _));
    }
}
