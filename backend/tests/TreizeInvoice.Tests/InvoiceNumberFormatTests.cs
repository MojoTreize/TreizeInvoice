using TreizeInvoice.Services.Invoicing;

namespace TreizeInvoice.Tests;

public class InvoiceNumberFormatTests
{
    [Theory]
    [InlineData("{year}-{counter:0000}", 2026, 1, "2026-0001")]
    [InlineData("{year}-{counter:0000}", 2026, 42, "2026-0042")]
    [InlineData("{year}-{counter:0000}", 2026, 12345, "2026-12345")]
    [InlineData("RE-{year}-{counter:000}", 2027, 7, "RE-2027-007")]
    [InlineData("{counter}", 2026, 9, "9")]
    public void Format_RendLeGabarit(string format, int year, int counter, string expected) =>
        Assert.Equal(expected, InvoiceNumberGenerator.Format(format, year, counter));

    [Fact]
    public void Format_VideRetombeSurLeGabaritParDefaut() =>
        Assert.Equal("2026-0003", InvoiceNumberGenerator.Format("", 2026, 3));
}
