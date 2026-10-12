using FdmsTestIntegration.Models;

namespace FdmsTestIntegration.Database;

public static class DbInitializer
{
    public static void Initialize(MockErpDbContext db, bool forceRecreate = true)
    {
        if (forceRecreate)
        {
            db.Database.EnsureDeleted();
        }
        db.Database.EnsureCreated();

        if (db.Documents.Any())
        {
            return; // DB already seeded
        }

        var sampleDocuments = new List<ErpDocument>
        {
            // -------------------------------------------------------------
            // 1. Standard USD Sales Invoice #1
            // -------------------------------------------------------------
            new ErpDocument
            {
                DocumentType = "FiscalInvoice",
                InvoiceNo = "INV-2026-001",
                Currency = "USD",
                ReceiptDate = DateTime.Now.AddHours(-3),
                BuyerName = "Harare Industrial Corp Ltd",
                BuyerTIN = "2000123456",
                PaymentMethod = "BankTransfer",
                ReceiptTotal = 5175.00m,
                TaxAmount = 675.00m,
                FiscalStatus = "Pending",
                Lines = new List<ErpLineItem>
                {
                    new ErpLineItem
                    {
                        LineNo = 1,
                        ItemCode = "GEN-50KVA",
                        ItemName = "Industrial Power Generator 50kVA",
                        Quantity = 1.0m,
                        UnitPrice = 5175.00m,
                        Total = 5175.00m,
                        TaxCode = "A",
                        TaxPercent = 15.0m,
                        TaxID = 1
                    }
                }
            },

            // -------------------------------------------------------------
            // 2. Standard USD Sales Invoice #2
            // -------------------------------------------------------------
            new ErpDocument
            {
                DocumentType = "FiscalInvoice",
                InvoiceNo = "INV-2026-002",
                Currency = "USD",
                ReceiptDate = DateTime.Now.AddHours(-2),
                BuyerName = "Bulawayo Enterprises Ltd",
                BuyerTIN = "2000987654",
                PaymentMethod = "Cash",
                ReceiptTotal = 1150.00m,
                TaxAmount = 150.00m,
                FiscalStatus = "Pending",
                Lines = new List<ErpLineItem>
                {
                    new ErpLineItem
                    {
                        LineNo = 1,
                        ItemCode = "OFF-DESK-01",
                        ItemName = "Executive Ergonomic Office Desk",
                        Quantity = 10.0m,
                        UnitPrice = 115.00m,
                        Total = 1150.00m,
                        TaxCode = "A",
                        TaxPercent = 15.0m,
                        TaxID = 1
                    }
                }
            },

            // -------------------------------------------------------------
            // 3. Multi-Currency ZiG Sales Invoice #3
            // -------------------------------------------------------------
            new ErpDocument
            {
                DocumentType = "FiscalInvoice",
                InvoiceNo = "INV-2026-003",
                Currency = "ZiG",
                ReceiptDate = DateTime.Now.AddHours(-1),
                BuyerName = "Mutare Builders Ltd",
                BuyerTIN = "2000555444",
                PaymentMethod = "MobileWallet",
                ReceiptTotal = 15000.00m,
                TaxAmount = 1956.52m,
                FiscalStatus = "Pending",
                Lines = new List<ErpLineItem>
                {
                    new ErpLineItem
                    {
                        LineNo = 1,
                        ItemCode = "CEM-50KG",
                        ItemName = "Portland Cement 50kg Bags",
                        Quantity = 100.0m,
                        UnitPrice = 150.00m,
                        Total = 15000.00m,
                        TaxCode = "A",
                        TaxPercent = 15.0m,
                        TaxID = 1
                    }
                }
            },

            // -------------------------------------------------------------
            // 4. Credit Note (CN) linked to INV-2026-001
            // -------------------------------------------------------------
            new ErpDocument
            {
                DocumentType = "CreditNote",
                InvoiceNo = "CN-2026-001",
                OriginalInvoiceNo = "INV-2026-001",
                ReceiptNotes = "Return of 1x damaged generator control panel accessory",
                Currency = "USD",
                ReceiptDate = DateTime.Now.AddMinutes(-30),
                BuyerName = "Harare Industrial Corp Ltd",
                BuyerTIN = "2000123456",
                PaymentMethod = "Credit",
                ReceiptTotal = -575.00m,
                TaxAmount = -75.00m,
                FiscalStatus = "Pending",
                Lines = new List<ErpLineItem>
                {
                    new ErpLineItem
                    {
                        LineNo = 1,
                        ItemCode = "GEN-CTRL-01",
                        ItemName = "Generator Control Panel Accessory Return",
                        Quantity = 1.0m,
                        UnitPrice = -575.00m,
                        Total = -575.00m,
                        TaxCode = "A",
                        TaxPercent = 15.0m,
                        TaxID = 1
                    }
                }
            },

            // -------------------------------------------------------------
            // 5. Debit Note (DN) linked to INV-2026-002
            // -------------------------------------------------------------
            new ErpDocument
            {
                DocumentType = "DebitNote",
                InvoiceNo = "DN-2026-001",
                OriginalInvoiceNo = "INV-2026-002",
                ReceiptNotes = "Express freight delivery surcharge increment",
                Currency = "USD",
                ReceiptDate = DateTime.Now.AddMinutes(-15),
                BuyerName = "Bulawayo Enterprises Ltd",
                BuyerTIN = "2000987654",
                PaymentMethod = "BankTransfer",
                ReceiptTotal = 230.00m,
                TaxAmount = 30.00m,
                FiscalStatus = "Pending",
                Lines = new List<ErpLineItem>
                {
                    new ErpLineItem
                    {
                        LineNo = 1,
                        ItemCode = "SUR-FREIGHT",
                        ItemName = "Express Heavy Delivery Freight Surcharge",
                        Quantity = 1.0m,
                        UnitPrice = 230.00m,
                        Total = 230.00m,
                        TaxCode = "A",
                        TaxPercent = 15.0m,
                        TaxID = 1
                    }
                }
            }
        };

        db.Documents.AddRange(sampleDocuments);
        db.SaveChanges();
    }
}
