using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Models.Entities;
using FdmsGatewayApi.Services.Implementation;
using Xunit;

namespace FdmsGatewayApi.Tests;

public class ValidationServiceTests
{
    private readonly FdmsValidationService _validationService;
    private readonly DeviceConfigEntity _openDevice;

    public ValidationServiceTests()
    {
        _validationService = new FdmsValidationService();
        _openDevice = new DeviceConfigEntity
        {
            DeviceId = 1,
            FiscalDayStatus = "FiscalDayOpened",
            CurrentFiscalDayNo = 10
        };
    }

    [Fact]
    public void ValidateInvoiceRequest_ValidInvoice_DoesNotThrow()
    {
        var request = CreateValidInvoice();
        _validationService.ValidateInvoiceRequest(request, _openDevice);
    }

    [Fact]
    public void ValidateInvoiceRequest_ClosedFiscalDay_ThrowsZimraValidationExceptionRCPT001()
    {
        var closedDevice = new DeviceConfigEntity { DeviceId = 1, FiscalDayStatus = "FiscalDayClosed" };
        var request = CreateValidInvoice();

        var ex = Assert.Throws<FdmsGatewayApi.Exceptions.ZimraValidationException>(() => _validationService.ValidateInvoiceRequest(request, closedDevice));
        Assert.Equal("RCPT001", ex.ErrorCode);
    }

    [Fact]
    public void ValidateInvoiceRequest_MismatchReceiptTotalAndLines_ThrowsRCPT019()
    {
        var request = CreateValidInvoice();
        request.ReceiptTotal = 999.00m; // Does not match 100.00m line total

        var ex = Assert.Throws<FdmsGatewayApi.Exceptions.ZimraValidationException>(() => _validationService.ValidateInvoiceRequest(request, _openDevice));
        Assert.Equal("RCPT019", ex.ErrorCode);
    }

    [Fact]
    public void ValidateCreditNoteRequest_MissingNotes_ThrowsRCPT034()
    {
        var creditReq = new ErpCreditNoteRequestDto
        {
            InvoiceNo = "CN-001",
            Currency = "USD",
            ReceiptTotal = -50.00m,
            ReceiptNotes = null!, // Missing notes
            Lines = new List<ErpReceiptLineDto> { new() { ReceiptLineNo = 1, ReceiptLineName = "Return", ReceiptLineQuantity = 1, ReceiptLineTotal = -50.00m } },
            Taxes = new List<ErpReceiptTaxDto> { new() { TaxCode = "A", TaxPercent = 15.00m, TaxAmount = -6.52m, SalesAmountWithTax = -50.00m } },
            Payments = new List<ErpPaymentDto> { new() { PaymentAmount = -50.00m, PaymentCurrency = "USD" } }
        };

        var ex = Assert.Throws<FdmsGatewayApi.Exceptions.ZimraValidationException>(() => _validationService.ValidateCreditNoteRequest(creditReq, _openDevice, originalDoc: null));
        Assert.Equal("RCPT034", ex.ErrorCode);
    }

    [Fact]
    public void ValidateCreditNoteRequest_ExceedsTwelveMonths_ThrowsRCPT033()
    {
        var creditReq = new ErpCreditNoteRequestDto
        {
            InvoiceNo = "CN-002",
            Currency = "USD",
            ReceiptDate = DateTime.UtcNow,
            ReceiptTotal = -50.00m,
            ReceiptNotes = "Return goods",
            Lines = new List<ErpReceiptLineDto> { new() { ReceiptLineNo = 1, ReceiptLineName = "Return", ReceiptLineQuantity = 1, ReceiptLineTotal = -50.00m } },
            Taxes = new List<ErpReceiptTaxDto> { new() { TaxCode = "A", TaxPercent = 15.00m, TaxAmount = -6.52m, SalesAmountWithTax = -50.00m } },
            Payments = new List<ErpPaymentDto> { new() { PaymentAmount = -50.00m, PaymentCurrency = "USD" } }
        };

        var oldOriginalDoc = new FiscalDocumentEntity
        {
            ReceiptID = 100,
            TotalAmount = 100.00m,
            CreatedAt = DateTime.UtcNow.AddMonths(-14) // 14 months ago
        };

        var ex = Assert.Throws<FdmsGatewayApi.Exceptions.ZimraValidationException>(() => _validationService.ValidateCreditNoteRequest(creditReq, _openDevice, oldOriginalDoc));
        Assert.Equal("RCPT033", ex.ErrorCode);
    }

    [Fact]
    public void ValidateCreditNoteRequest_ExceedsOriginalAmount_ThrowsRCPT035()
    {
        var creditReq = new ErpCreditNoteRequestDto
        {
            InvoiceNo = "CN-003",
            Currency = "USD",
            ReceiptDate = DateTime.UtcNow,
            ReceiptTotal = -150.00m, // -150 > 100 original
            ReceiptNotes = "Full Return plus extra",
            Lines = new List<ErpReceiptLineDto> { new() { ReceiptLineNo = 1, ReceiptLineName = "Return", ReceiptLineQuantity = 1, ReceiptLineTotal = -150.00m } },
            Taxes = new List<ErpReceiptTaxDto> { new() { TaxCode = "A", TaxPercent = 15.00m, TaxAmount = -19.56m, SalesAmountWithTax = -150.00m } },
            Payments = new List<ErpPaymentDto> { new() { PaymentAmount = -150.00m, PaymentCurrency = "USD" } }
        };

        var originalDoc = new FiscalDocumentEntity
        {
            ReceiptID = 100,
            TotalAmount = 100.00m,
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        };

        var ex = Assert.Throws<FdmsGatewayApi.Exceptions.ZimraValidationException>(() => _validationService.ValidateCreditNoteRequest(creditReq, _openDevice, originalDoc));
        Assert.Equal("RCPT035", ex.ErrorCode);
    }


    [Fact]
    public void ValidateInvoiceRequest_Exceeds500Lines_ThrowsRCPT020()
    {
        var request = CreateValidInvoice();
        request.Lines = Enumerable.Range(1, 501).Select(i => new ErpReceiptLineDto
        {
            ReceiptLineNo = i,
            ReceiptLineName = $"Item {i}",
            ReceiptLineQuantity = 1,
            ReceiptLinePrice = 1.00m,
            ReceiptLineTotal = 1.00m
        }).ToList();
        request.ReceiptTotal = 501.00m;

        var ex = Assert.Throws<FdmsGatewayApi.Exceptions.ZimraValidationException>(() => _validationService.ValidateInvoiceRequest(request, _openDevice));
        Assert.Equal("RCPT020", ex.ErrorCode);
    }

    [Fact]
    public void ValidateInvoiceRequest_MissingBuyerTinWithVatNumber_ThrowsRCPT015()
    {
        var request = CreateValidInvoice();
        request.BuyerData = new BuyerDto
        {
            BuyerRegisterName = "Test Buyer",
            BuyerTIN = "", // Empty TIN
            VATNumber = "12345678"
        };

        var ex = Assert.Throws<FdmsGatewayApi.Exceptions.ZimraValidationException>(() => _validationService.ValidateInvoiceRequest(request, _openDevice));
        Assert.Equal("RCPT015", ex.ErrorCode);
    }

    [Fact]
    public void ValidateInvoiceRequest_TaxPercentMismatch_ThrowsRCPT014()
    {
        var request = CreateValidInvoice();
        request.Lines[0].TaxID = 1;
        request.Lines[0].TaxPercent = 15.00m;
        request.Taxes[0].TaxID = 1;
        request.Taxes[0].TaxPercent = 5.00m; // Mismatch with line

        var ex = Assert.Throws<FdmsGatewayApi.Exceptions.ZimraValidationException>(() => _validationService.ValidateInvoiceRequest(request, _openDevice));
        Assert.Equal("RCPT014", ex.ErrorCode);
    }

    [Fact]
    public void ValidateCreditNote_MissingReference_ThrowsRCPT030()
    {
        var creditReq = new ErpCreditNoteRequestDto
        {
            InvoiceNo = "CN-004",
            Currency = "USD",
            ReceiptNotes = "Return",
            ReceiptTotal = -50.00m,
            CreditDebitNoteRef = null!,
            Lines = new List<ErpReceiptLineDto> { new() { ReceiptLineNo = 1, ReceiptLineName = "Return", ReceiptLineQuantity = 1, ReceiptLineTotal = -50.00m } },
            Taxes = new List<ErpReceiptTaxDto> { new() { TaxCode = "A", TaxPercent = 15.00m, TaxAmount = -6.52m, SalesAmountWithTax = -50.00m } },
            Payments = new List<ErpPaymentDto> { new() { PaymentAmount = -50.00m, PaymentCurrency = "USD" } }
        };

        var ex = Assert.Throws<FdmsGatewayApi.Exceptions.ZimraValidationException>(() => _validationService.ValidateCreditNoteRequest(creditReq, _openDevice, originalDoc: null));
        Assert.Equal("RCPT030", ex.ErrorCode);
    }

    private static ErpInvoiceRequestDto CreateValidInvoice()
    {
        return new ErpInvoiceRequestDto
        {
            InvoiceNo = "INV-VAL-001",
            Currency = "USD",
            ReceiptTotal = 100.00m,
            ReceiptLinesTaxInclusive = true,
            Lines = new List<ErpReceiptLineDto>
            {
                new() { ReceiptLineNo = 1, ReceiptLineName = "Item A", ReceiptLineQuantity = 1, ReceiptLinePrice = 100.00m, ReceiptLineTotal = 100.00m }
            },
            Taxes = new List<ErpReceiptTaxDto>
            {
                new() { TaxCode = "A", TaxPercent = 15.00m, TaxAmount = 13.04m, SalesAmountWithTax = 100.00m }
            },
            Payments = new List<ErpPaymentDto>
            {
                new() { PaymentAmount = 100.00m, PaymentCurrency = "USD", MoneyTypeCode = "CASH" }
            }
        };
    }
}
