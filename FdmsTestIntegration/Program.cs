using FdmsTestIntegration.Database;
using FdmsTestIntegration.Services;
using Microsoft.EntityFrameworkCore;

namespace FdmsTestIntegration;

class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        PrintHeader();

        string gatewayUrl = "http://localhost:5045";
        string apiKey = "fdms-admin-key-secure-prod-placeholder";

        bool autoRun = args.Any(a => a.Equals("--auto", StringComparison.OrdinalIgnoreCase) || a.Equals("-a", StringComparison.OrdinalIgnoreCase));

        using var db = new MockErpDbContext();

        if (autoRun)
        {
            Console.WriteLine("\n[AUTOMATED TEST RUN MODE DETECTED]");
            await ExecuteFullTestWorkflowAsync(db, gatewayUrl, apiKey);
            return;
        }

        while (true)
        {
            Console.WriteLine("\n=======================================================");
            Console.WriteLine("           FDMS PARTNER INTEGRATION TEST HARNESS        ");
            Console.WriteLine("=======================================================");
            Console.WriteLine(" 1. Reset & Seed Mock ERP Database (Invoices, CN, DN)");
            Console.WriteLine(" 2. View Current Mock ERP Documents & Fiscal Status");
            Console.WriteLine(" 3. Execute FDMS Integration Sync Engine");
            Console.WriteLine(" 4. Run Full Automated Integration Test Run (Seed + Sync)");
            Console.WriteLine(" 5. Exit");
            Console.Write("\nSelect option [1-5]: ");

            string? choice = Console.ReadLine()?.Trim();
            Console.WriteLine();

            switch (choice)
            {
                case "1":
                    DbInitializer.Initialize(db, forceRecreate: true);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("✔ Mock ERP Database successfully initialized & seeded with 5 test documents!");
                    Console.ResetColor();
                    DisplayDatabaseSummary(db);
                    break;

                case "2":
                    DisplayDatabaseSummary(db);
                    break;

                case "3":
                    await RunSyncEngineAsync(db, gatewayUrl, apiKey);
                    break;

                case "4":
                    await ExecuteFullTestWorkflowAsync(db, gatewayUrl, apiKey);
                    break;

                case "5":
                    Console.WriteLine("Exiting FDMS Integration Test Harness. Goodbye!");
                    return;

                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Invalid selection. Please choose an option from 1 to 5.");
                    Console.ResetColor();
                    break;
            }
        }
    }

    private static async Task ExecuteFullTestWorkflowAsync(MockErpDbContext db, string gatewayUrl, string apiKey)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n🚀 STEP 1: Seeding Mock ERP Database with pre-filled test transactions...");
        Console.ResetColor();
        DbInitializer.Initialize(db, forceRecreate: true);
        DisplayDatabaseSummary(db);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n🚀 STEP 2: Executing FDMS Gateway Partner Integration Engine...");
        Console.ResetColor();
        await RunSyncEngineAsync(db, gatewayUrl, apiKey);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n🚀 STEP 3: Verifying Final Fiscal State in Mock ERP Database...");
        Console.ResetColor();
        DisplayDatabaseSummary(db);
    }

    private static async Task RunSyncEngineAsync(MockErpDbContext db, string gatewayUrl, string apiKey)
    {
        var apiClient = new FdmsApiClient(gatewayUrl, apiKey);
        var engine = new IntegrationSyncEngine(db, apiClient);

        Console.WriteLine($"[Sync Engine] Connecting to FDMS Gateway API at '{gatewayUrl}'...");
        var result = await engine.RunSyncWorkflowAsync();

        if (result.Success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\n✔ SUCCESS: {result.Message}");
            Console.WriteLine($"   • Invoices Fiscalized    : {result.ProcessedInvoices}");
            Console.WriteLine($"   • Credit Notes Fiscalized: {result.ProcessedCreditNotes}");
            Console.WriteLine($"   • Debit Notes Fiscalized : {result.ProcessedDebitNotes}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"\n⚠ NOTICE / WARNING: {result.Message}");
            Console.ResetColor();
            foreach (var err in result.Errors)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"   ✖ {err}");
                Console.ResetColor();
            }
        }
    }

    private static void DisplayDatabaseSummary(MockErpDbContext db)
    {
        var docs = db.Documents.Include(d => d.Lines).OrderBy(d => d.Id).ToList();

        if (!docs.Any())
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("No documents found in Mock ERP Database. Select Option 1 to seed sample data.");
            Console.ResetColor();
            return;
        }

        Console.WriteLine("-------------------------------------------------------------------------------------------------------------------------");
        Console.WriteLine(string.Format("{0,-12} | {1,-14} | {2,-13} | {3,-10} | {4,-12} | {5,-12} | {6,-10} | {7,-20}",
            "Type", "Invoice No", "Original Ref", "Currency", "Amount", "Fiscal State", "Receipt ID", "QR Code / Verification"));
        Console.WriteLine("-------------------------------------------------------------------------------------------------------------------------");

        foreach (var d in docs)
        {
            ConsoleColor stateColor = d.FiscalStatus switch
            {
                "Fiscalized" => ConsoleColor.Green,
                "Pending" => ConsoleColor.Yellow,
                "Failed" => ConsoleColor.Red,
                _ => ConsoleColor.White
            };

            string origRef = string.IsNullOrEmpty(d.OriginalInvoiceNo) ? "-" : d.OriginalInvoiceNo;
            string receiptIdStr = d.ReceiptID.HasValue ? d.ReceiptID.Value.ToString() : "-";
            string qrStr = string.IsNullOrEmpty(d.QrCodeUrl) ? "-" : (d.QrCodeUrl.Length > 20 ? d.QrCodeUrl.Substring(0, 20) + "..." : d.QrCodeUrl);

            Console.Write(string.Format("{0,-12} | {1,-14} | {2,-13} | {3,-10} | {4,-12:C2} | ",
                d.DocumentType, d.InvoiceNo, origRef, d.Currency, d.ReceiptTotal));

            Console.ForegroundColor = stateColor;
            Console.Write(string.Format("{0,-12}", d.FiscalStatus));
            Console.ResetColor();

            Console.WriteLine(string.Format(" | {0,-10} | {1,-20}", receiptIdStr, qrStr));
        }

        Console.WriteLine("-------------------------------------------------------------------------------------------------------------------------\n");
    }

    private static void PrintHeader()
    {
        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine(@"
    ========================================================================
       ZIMRA FDMS FISCAL GATEWAY — ERP PARTNER TEST INTEGRATION HARNESS     
    ========================================================================
        ");
        Console.ResetColor();
    }
}
