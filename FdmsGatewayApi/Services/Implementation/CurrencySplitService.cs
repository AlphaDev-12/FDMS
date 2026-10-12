using FdmsGatewayApi.Models.Dtos;
using FdmsGatewayApi.Services.Interfaces;

namespace FdmsGatewayApi.Services.Implementation;

public class CurrencySplitService : ICurrencySplitService
{
    public List<ErpInvoiceRequestDto> SplitIfNeeded(ErpInvoiceRequestDto request)
    {
        if (request.Payments == null || request.Payments.Count == 0)
        {
            return new List<ErpInvoiceRequestDto> { request };
        }

        // Check if any payment specifies a currency different from request.Currency
        var distinctCurrencies = request.Payments
            .Select(p => string.IsNullOrWhiteSpace(p.PaymentCurrency) ? request.Currency : p.PaymentCurrency.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        // If all payments are in a single currency, return original
        if (distinctCurrencies.Count <= 1)
        {
            // Normalize payments currency if needed
            foreach (var p in request.Payments)
            {
                if (string.IsNullOrWhiteSpace(p.PaymentCurrency))
                {
                    p.PaymentCurrency = request.Currency;
                }
            }
            return new List<ErpInvoiceRequestDto> { request };
        }

        // Multi-currency split required
        var result = new List<ErpInvoiceRequestDto>();
        decimal grandTotalPayments = request.Payments.Sum(p => p.PaymentAmount);

        if (grandTotalPayments <= 0)
        {
            return new List<ErpInvoiceRequestDto> { request };
        }

        foreach (var currency in distinctCurrencies)
        {
            var currencyPayments = request.Payments
                .Where(p => (string.IsNullOrWhiteSpace(p.PaymentCurrency) ? request.Currency : p.PaymentCurrency.Trim().ToUpperInvariant()) == currency)
                .ToList();

            decimal currencyTotalPaid = currencyPayments.Sum(p => p.PaymentAmount);
            decimal ratio = currencyTotalPaid / grandTotalPayments;

            var splitInvoice = new ErpInvoiceRequestDto
            {
                InvoiceNo = $"{request.InvoiceNo}-{currency}",
                Currency = currency,
                ReceiptDate = request.ReceiptDate,
                ReceiptLinesTaxInclusive = request.ReceiptLinesTaxInclusive,
                ReceiptPrintForm = request.ReceiptPrintForm,
                Username = request.Username,
                UserNameSurname = request.UserNameSurname,
                BuyerData = request.BuyerData,
                Payments = currencyPayments,
                ReceiptTotal = Math.Round(currencyTotalPaid, 2),
                Lines = new List<ErpReceiptLineDto>(),
                Taxes = new List<ErpReceiptTaxDto>()
            };

            // Scale lines proportionally
            int lineNo = 1;
            foreach (var line in request.Lines)
            {
                var scaledLine = new ErpReceiptLineDto
                {
                    ReceiptLineType = line.ReceiptLineType,
                    ReceiptLineNo = lineNo++,
                    ReceiptLineHSCode = line.ReceiptLineHSCode,
                    ReceiptLineName = $"{line.ReceiptLineName} [{currency}]",
                    ReceiptLinePrice = line.ReceiptLinePrice.HasValue ? Math.Round(line.ReceiptLinePrice.Value * ratio, 6) : null,
                    ReceiptLineQuantity = line.ReceiptLineQuantity,
                    ReceiptLineTotal = Math.Round(line.ReceiptLineTotal * ratio, 2),
                    TaxCode = line.TaxCode,
                    TaxPercent = line.TaxPercent,
                    TaxID = line.TaxID
                };
                splitInvoice.Lines.Add(scaledLine);
            }

            // Scale taxes proportionally
            foreach (var tax in request.Taxes)
            {
                var scaledTax = new ErpReceiptTaxDto
                {
                    TaxCode = tax.TaxCode,
                    TaxPercent = tax.TaxPercent,
                    TaxID = tax.TaxID,
                    TaxAmount = Math.Round(tax.TaxAmount * ratio, 2),
                    SalesAmountWithTax = Math.Round(tax.SalesAmountWithTax * ratio, 2)
                };
                splitInvoice.Taxes.Add(scaledTax);
            }

            result.Add(splitInvoice);
        }

        return result;
    }
}
