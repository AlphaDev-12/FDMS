using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Services.Implementation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FdmsGatewayApi.Tests;

public class BusinessLogicTests
{
    [Fact]
    public void CurrencySplitService_SingleCurrency_ReturnsOriginalRequest()
    {
        // Arrange
        var service = new CurrencySplitService();
        var request = new ErpInvoiceRequestDto
        {
            InvoiceNo = "INV-001",
            Currency = "USD",
            ReceiptTotal = 100.00m,
            Payments = new List<ErpPaymentDto>
            {
                new() { MoneyTypeCode = "Cash", PaymentAmount = 100.00m, PaymentCurrency = "USD" }
            }
        };

        // Act
        var splitResults = service.SplitIfNeeded(request);

        // Assert
        Assert.Single(splitResults);
        Assert.Equal("INV-001", splitResults[0].InvoiceNo);
        Assert.Equal("USD", splitResults[0].Currency);
    }

    [Fact]
    public void CurrencySplitService_MultiCurrencySplitTender_SplitsIntoSeparateCurrencyInvoices()
    {
        // Arrange
        var service = new CurrencySplitService();
        var request = new ErpInvoiceRequestDto
        {
            InvoiceNo = "INV-MULTI-001",
            Currency = "USD",
            ReceiptTotal = 40.00m,
            Lines = new List<ErpReceiptLineDto>
            {
                new() { ReceiptLineName = "Item 1", ReceiptLineQuantity = 2, ReceiptLineTotal = 40.00m, TaxCode = "15%_VAT", TaxPercent = 15.00m, TaxID = 1 }
            },
            Taxes = new List<ErpReceiptTaxDto>
            {
                new() { TaxCode = "15%_VAT", TaxPercent = 15.00m, TaxID = 1, TaxAmount = 5.22m, SalesAmountWithTax = 40.00m }
            },
            Payments = new List<ErpPaymentDto>
            {
                new() { MoneyTypeCode = "Cash", PaymentAmount = 10.00m, PaymentCurrency = "USD" },
                new() { MoneyTypeCode = "BankTransfer", PaymentAmount = 30.00m, PaymentCurrency = "ZIG" }
            }
        };

        // Act
        var splitResults = service.SplitIfNeeded(request);

        // Assert
        Assert.Equal(2, splitResults.Count);

        var usdInvoice = splitResults.FirstOrDefault(r => r.Currency == "USD");
        var zigInvoice = splitResults.FirstOrDefault(r => r.Currency == "ZIG");

        Assert.NotNull(usdInvoice);
        Assert.NotNull(zigInvoice);

        Assert.Equal("INV-MULTI-001-USD", usdInvoice.InvoiceNo);
        Assert.Equal(10.00m, usdInvoice.ReceiptTotal);

        Assert.Equal("INV-MULTI-001-ZIG", zigInvoice.InvoiceNo);
        Assert.Equal(30.00m, zigInvoice.ReceiptTotal);
    }

    [Fact]
    public void TaxCodeMappingService_MapsStandardTaxCodesCorrectly()
    {
        // Arrange
        var envMock = new Mock<IHostEnvironment>();
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        envMock.Setup(e => e.ContentRootPath).Returns(tempPath);

        var service = new TaxCodeMappingService(NullLogger<TaxCodeMappingService>.Instance, envMock.Object);

        // Act
        var vatResult = service.MapErpTaxCode("15%_VAT");
        var nonVatResult = service.MapErpTaxCode("0%_NON_VAT");

        // Assert
        Assert.Equal("A", vatResult.ZimraTaxCode);
        Assert.Equal(1, vatResult.TaxID);
        Assert.Equal(15.00m, vatResult.TaxPercent);

        Assert.Equal("B", nonVatResult.ZimraTaxCode);
        Assert.Equal(2, nonVatResult.TaxID);
        Assert.Equal(0.00m, nonVatResult.TaxPercent);
    }

    [Fact]
    public void FdmsValidationService_TotalMismatch_ThrowsZimraValidationExceptionWithRCPT019()
    {
        // Arrange
        var service = new FdmsValidationService();
        var deviceConfig = new FdmsGatewayApi.Models.Entities.DeviceConfigEntity
        {
            DeviceId = 1,
            FiscalDayStatus = "FiscalDayOpened"
        };
        var request = new ErpInvoiceRequestDto
        {
            InvoiceNo = "INV-ERR-019",
            Currency = "USD",
            ReceiptTotal = 150.00m, // Mismatched total (expected 100.00m)
            ReceiptLinesTaxInclusive = true,
            Lines = new List<ErpReceiptLineDto>
            {
                new() { ReceiptLineName = "Item A", ReceiptLineQuantity = 1, ReceiptLineTotal = 100.00m, TaxCode = "A", TaxPercent = 15.00m, TaxID = 1 }
            },
            Taxes = new List<ErpReceiptTaxDto>
            {
                new() { TaxCode = "A", TaxPercent = 15.00m, TaxID = 1, TaxAmount = 13.04m, SalesAmountWithTax = 100.00m }
            },
            Payments = new List<ErpPaymentDto>
            {
                new() { MoneyTypeCode = "Cash", PaymentAmount = 150.00m }
            }
        };

        // Act & Assert
        var ex = Assert.Throws<FdmsGatewayApi.Exceptions.ZimraValidationException>(() => service.ValidateInvoiceRequest(request, deviceConfig));
        Assert.Equal("RCPT019", ex.ErrorCode);
    }

    [Fact]
    public async Task FdmsApiClient_GetStockListAsync_ReturnsStockItemsInSimulatedEnv()
    {
        // Arrange
        var httpClientFactoryMock = new Mock<IHttpClientFactory>();
        var healthCheckMock = new Mock<FdmsGatewayApi.Services.Interfaces.IZimraHealthCheck>();
        healthCheckMock.Setup(h => h.TargetEnvironment).Returns("Simulated");

        var configMock = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        var client = new FdmsApiClient(httpClientFactoryMock.Object, healthCheckMock.Object, configMock.Object, NullLogger<FdmsApiClient>.Instance);

        // Act
        var result = await client.GetStockListAsync(new GetStockListRequest { DeviceID = 1 }, "cert", "key");


        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result.Rows);
        Assert.Equal("SIM-ITEM-001", result.Rows[0].HsCode);
    }
}

