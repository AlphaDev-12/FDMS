using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Services.Implementation;
using Xunit;

namespace FdmsGatewayApi.Tests;

public class FdmsSignatureVerificationTests
{
    private readonly FdmsCryptoService _cryptoService = new();

    [Fact]
    public void ComputeReceiptHash_FiscalInvoice_MatchesZimraSpecificationTestVector1()
    {
        // Arrange (ZIMRA Section 13.2.1 Example 1)
        int deviceId = 321;
        string receiptType = "FISCALINVOICE";
        string currency = "ZWL";
        int receiptGlobalNo = 432;
        DateTime receiptDate = DateTime.Parse("2019-09-19T15:43:12");
        decimal receiptTotal = 9450.00m;

        var taxes = new List<ErpReceiptTaxDto>
        {
            new() { TaxID = 1, TaxCode = "A", TaxPercent = null, TaxAmount = 0.00m, SalesAmountWithTax = 2500.00m },
            new() { TaxID = 2, TaxCode = "B", TaxPercent = 0.00m, TaxAmount = 0.00m, SalesAmountWithTax = 3500.00m },
            new() { TaxID = 3, TaxCode = "C", TaxPercent = 15.00m, TaxAmount = 150.00m, SalesAmountWithTax = 1150.00m },
            new() { TaxID = 3, TaxCode = "D", TaxPercent = 15.00m, TaxAmount = 300.00m, SalesAmountWithTax = 2300.00m }
        };

        string previousReceiptHash = "hNVJXP/ACOiE8McD3pKsDlqBXpuaUqQOfPnMyfZWI9k=";
        string expectedBase64Hash = "eHAAOVqxrRVRliVOsik4sONXJ10nckqCWIhPyoMyguM=";

        // Act
        string calculatedHash = _cryptoService.ComputeReceiptHash(
            deviceId,
            receiptType,
            currency,
            receiptGlobalNo,
            receiptDate,
            receiptTotal,
            taxes,
            previousReceiptHash);

        // Assert
        Assert.Equal(expectedBase64Hash, calculatedHash);
    }

    [Fact]
    public void ComputeReceiptHash_CreditNote_MatchesZimraSpecificationTestVector()
    {
        // Arrange (ZIMRA Section 13.2.1 CreditNote Example 1)
        int deviceId = 321;
        string receiptType = "CREDITNOTE";
        string currency = "ZWL";
        int receiptGlobalNo = 432;
        DateTime receiptDate = DateTime.Parse("2020-09-19T15:43:12");
        decimal receiptTotal = -9450.00m;

        var taxes = new List<ErpReceiptTaxDto>
        {
            new() { TaxID = 1, TaxCode = "A", TaxPercent = null, TaxAmount = 0.00m, SalesAmountWithTax = -2500.00m },
            new() { TaxID = 2, TaxCode = "B", TaxPercent = 0.00m, TaxAmount = 0.00m, SalesAmountWithTax = -3500.00m },
            new() { TaxID = 3, TaxCode = "C", TaxPercent = 15.00m, TaxAmount = -150.00m, SalesAmountWithTax = -1150.00m },
            new() { TaxID = 3, TaxCode = "D", TaxPercent = 15.00m, TaxAmount = -300.00m, SalesAmountWithTax = -2300.00m }
        };

        string previousReceiptHash = "hNVJXP/ACOiE8McD3pKsDlqBXpuaUqQOfPnMyfZWI9k=";
        string expectedBase64Hash = "vtYlyJzJGgZvxcdiuckVSTqxn4iIFHJj6ub/zHkvA00=";

        // Act
        string calculatedHash = _cryptoService.ComputeReceiptHash(
            deviceId,
            receiptType,
            currency,
            receiptGlobalNo,
            receiptDate,
            receiptTotal,
            taxes,
            previousReceiptHash);

        // Assert
        Assert.Equal(expectedBase64Hash, calculatedHash);
    }

    [Fact]
    public void ComputeFiscalDayHash_MatchesZimraSpecificationTestVector()
    {
        // Arrange (ZIMRA Section 13.3.1 Page 76 Example)
        int deviceId = 321;
        int fiscalDayNo = 84;
        DateTime fiscalDayDate = DateTime.Parse("2019-09-23");

        var counters = new List<FiscalDayCounterDto>
        {
            new() { FiscalCounterType = "SaleByTax", FiscalCounterCurrency = "ZWL", FiscalCounterTaxPercent = null, FiscalCounterValue = 23000.00m },
            new() { FiscalCounterType = "SaleByTax", FiscalCounterCurrency = "ZWL", FiscalCounterTaxPercent = 0.00m, FiscalCounterValue = 12000.00m },
            new() { FiscalCounterType = "SaleByTax", FiscalCounterCurrency = "USD", FiscalCounterTaxPercent = 14.50m, FiscalCounterValue = 25.00m },
            new() { FiscalCounterType = "SaleByTax", FiscalCounterCurrency = "ZWL", FiscalCounterTaxPercent = 15.00m, FiscalCounterValue = 12.00m },
            new() { FiscalCounterType = "SaleTaxByTax", FiscalCounterCurrency = "USD", FiscalCounterTaxPercent = 15.00m, FiscalCounterValue = 2.50m },
            new() { FiscalCounterType = "SaleTaxByTax", FiscalCounterCurrency = "ZWL", FiscalCounterTaxPercent = 15.00m, FiscalCounterValue = 2300.00m },
            new() { FiscalCounterType = "BalanceByMoneyType", FiscalCounterCurrency = "USD", FiscalCounterMoneyType = "LCASH", FiscalCounterValue = 37.00m },
            new() { FiscalCounterType = "BalanceByMoneyType", FiscalCounterCurrency = "ZWL", FiscalCounterMoneyType = "CASH", FiscalCounterValue = 20000.00m },
            new() { FiscalCounterType = "BalanceByMoneyType", FiscalCounterCurrency = "ZWL", FiscalCounterMoneyType = "CARD", FiscalCounterValue = 15000.00m }
        };

        string expectedBase64Hash = "fyEALiA5B2vROobv73Z0YH+IXOfi7zjYqbwbnZs0ZNE=";

        // Act
        string calculatedHash = _cryptoService.ComputeFiscalDayHash(deviceId, fiscalDayNo, fiscalDayDate, counters);

        // Assert
        Assert.Equal(expectedBase64Hash, calculatedHash);
    }

    [Fact]
    public void KeyGenerationAndSigning_GeneratesValidEcdsaSignature()
    {
        // Arrange
        int deviceId = 42;
        string deviceSerialNo = "SN0001";

        var (privateKeyPem, csrPem) = _cryptoService.GenerateKeyPairAndCsr(deviceId, deviceSerialNo);

        Assert.Contains("-----BEGIN CERTIFICATE REQUEST-----", csrPem);
        Assert.Contains("-----BEGIN EC PRIVATE KEY-----", privateKeyPem);

        string sampleHash = "zDxEalWUpwX2BcsYxRUAEfY/13OaCrTwDt01So3a6uU=";

        // Act
        var sigData = _cryptoService.SignReceipt(sampleHash, privateKeyPem);

        // Assert
        Assert.Equal(sampleHash, sigData.Hash);
        Assert.False(string.IsNullOrEmpty(sigData.Signature));
    }
}
