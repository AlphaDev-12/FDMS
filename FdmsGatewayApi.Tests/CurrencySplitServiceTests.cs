using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Services.Implementation;
using Xunit;

namespace FdmsGatewayApi.Tests;

public class CurrencySplitServiceTests
{
    private readonly CurrencySplitService _service;

    public CurrencySplitServiceTests()
    {
        _service = new CurrencySplitService();
    }

    [Fact]
    public void SplitIfNeeded_SingleCurrencyPayments_ReturnsOriginalRequest()
    {
        var request = new ErpInvoiceRequestDto
        {
            InvoiceNo = "INV-001",
            Currency = "USD",
            ReceiptTotal = 100.00m,
            Payments = new List<ErpPaymentDto>
            {
                new() { PaymentAmount = 100.00m, PaymentCurrency = "USD", MoneyTypeCode = "CASH" }
            },
            Lines = new List<ErpReceiptLineDto>
            {
                new() { ReceiptLineNo = 1, ReceiptLineName = "Item 1", ReceiptLineQuantity = 1, ReceiptLinePrice = 100.00m, ReceiptLineTotal = 100.00m }
            },
            Taxes = new List<ErpReceiptTaxDto>
            {
                new() { TaxCode = "A", TaxPercent = 15.00m, TaxAmount = 13.04m, SalesAmountWithTax = 100.00m }
            }
        };

        var result = _service.SplitIfNeeded(request);

        Assert.Single(result);
        Assert.Equal("INV-001", result[0].InvoiceNo);
        Assert.Equal("USD", result[0].Currency);
    }

    [Fact]
    public void SplitIfNeeded_MultiCurrencyThreeCurrencies_SplitsProportionally()
    {
        var request = new ErpInvoiceRequestDto
        {
            InvoiceNo = "INV-MULTI",
            Currency = "USD",
            ReceiptTotal = 200.00m,
            Payments = new List<ErpPaymentDto>
            {
                new() { PaymentAmount = 100.00m, PaymentCurrency = "USD", MoneyTypeCode = "CASH" },
                new() { PaymentAmount = 50.00m, PaymentCurrency = "ZIG", MoneyTypeCode = "ECOCASH" },
                new() { PaymentAmount = 50.00m, PaymentCurrency = "EUR", MoneyTypeCode = "CARD" }
            },
            Lines = new List<ErpReceiptLineDto>
            {
                new() { ReceiptLineNo = 1, ReceiptLineName = "Product A", ReceiptLineQuantity = 2, ReceiptLinePrice = 100.00m, ReceiptLineTotal = 200.00m }
            },
            Taxes = new List<ErpReceiptTaxDto>
            {
                new() { TaxCode = "A", TaxPercent = 15.00m, TaxAmount = 26.09m, SalesAmountWithTax = 200.00m }
            }
        };

        var result = _service.SplitIfNeeded(request);

        Assert.Equal(3, result.Count);

        var usdSplit = result.FirstOrDefault(r => r.Currency == "USD");
        var zigSplit = result.FirstOrDefault(r => r.Currency == "ZIG");
        var eurSplit = result.FirstOrDefault(r => r.Currency == "EUR");

        Assert.NotNull(usdSplit);
        Assert.NotNull(zigSplit);
        Assert.NotNull(eurSplit);

        Assert.Equal("INV-MULTI-USD", usdSplit.InvoiceNo);
        Assert.Equal(100.00m, usdSplit.ReceiptTotal);
        Assert.Equal(100.00m, usdSplit.Lines[0].ReceiptLineTotal);

        Assert.Equal("INV-MULTI-ZIG", zigSplit.InvoiceNo);
        Assert.Equal(50.00m, zigSplit.ReceiptTotal);
        Assert.Equal(50.00m, zigSplit.Lines[0].ReceiptLineTotal);

        Assert.Equal("INV-MULTI-EUR", eurSplit.InvoiceNo);
        Assert.Equal(50.00m, eurSplit.ReceiptTotal);
        Assert.Equal(50.00m, eurSplit.Lines[0].ReceiptLineTotal);
    }

    [Fact]
    public void SplitIfNeeded_EmptyPayments_ReturnsOriginalRequest()
    {
        var request = new ErpInvoiceRequestDto
        {
            InvoiceNo = "INV-NO-PAYMENT",
            Currency = "USD",
            ReceiptTotal = 50.00m,
            Payments = new List<ErpPaymentDto>()
        };

        var result = _service.SplitIfNeeded(request);

        Assert.Single(result);
        Assert.Equal("INV-NO-PAYMENT", result[0].InvoiceNo);
    }
}
