// =============================================================================
// PEWO additions to ServicesConfiguration.cs — CORRECTED
// Add these lines inside ConfigureServices(), after existing registrations.
//
// NOTE: IPewoTotalsCheckService is NOT registered here.
// PewoWorkerService injects the existing ITotalsValidationService directly.
// ITotalsValidationService is already registered: services.AddTransient<ITotalsValidationService, TotalsValidationService>()
// =============================================================================

// -- PEWO: IHttpContextAccessor (needed by PewoWorkerService for ValidateNgen) -
// Add this if not already present (check Startup.cs):
services.AddHttpContextAccessor();

// -- PEWO: Database API client ------------------------------------------------
services.AddHttpClient<IPewoDataServiceClient, PewoDataServiceClient>(client =>
{
    var url = Environment.GetEnvironmentVariable("PewoDataServiceUrl");
    if (!string.IsNullOrWhiteSpace(url))
        client.BaseAddress = new Uri(url.TrimEnd('/') + "/api/PewoData/");
});

// -- PEWO: Job data service ---------------------------------------------------
services.AddTransient<IPewoJobDataService, PewoJobDataService>();

// -- PEWO: Individual step classes (transient — one per run) ------------------
services.AddTransient<ReadBlobStep>();
services.AddTransient<ZipStep>();
services.AddTransient<SftpStep>();
services.AddTransient<ArchiveStep>();
services.AddTransient<EmailStep>();

// -- PEWO: Worker (injects existing ITotalsValidationService — no new service) -
services.AddTransient<IPewoWorkerService, PewoWorkerService>();

// -- BlobServiceClient (Managed Identity) — add only if not already registered -
services.AddSingleton(sp =>
{
    var accountUrl = Environment.GetEnvironmentVariable("BlobAccountUrl")
        ?? "https://<your-storage-account>.blob.core.windows.net";
    return new Azure.Storage.Blobs.BlobServiceClient(
        new Uri(accountUrl),
        new Azure.Identity.DefaultAzureCredential());
});

// =============================================================================
// appsettings.json — add to KeyVaultSettings:
//   "PewoApiKey":         "PewoApiKey",
//   "PewoDataServiceUrl": "PewoDataServiceUrl"
// =============================================================================
