// =============================================================================
// PEWO additions to ServicesConfiguration.cs
// Add these lines inside ConfigureServices(), after existing service registrations.
// =============================================================================

// Add to ServicesConfiguration.cs → ConfigureServices():

// -- PEWO: Database API client (URL from Key Vault via PewoDataServiceUrl) ---
services.AddHttpClient<IPewoDataServiceClient, PewoDataServiceClient>(client =>
{
    var url = Environment.GetEnvironmentVariable("PewoDataServiceUrl");
    if (!string.IsNullOrWhiteSpace(url))
        client.BaseAddress = new Uri(url.TrimEnd('/') + "/api/PewoData/");
});

// -- PEWO: Core services -------------------------------------------------------
services.AddTransient<IPewoJobDataService, PewoJobDataService>();
services.AddTransient<IPewoTotalsCheckService, PewoTotalsCheckService>();

// -- PEWO: Individual steps (transient — instantiated per run) ----------------
services.AddTransient<ReadBlobStep>();
services.AddTransient<ZipStep>();
services.AddTransient<SftpStep>();
services.AddTransient<ArchiveStep>();
services.AddTransient<EmailStep>();

// -- PEWO: Worker service ------------------------------------------------------
services.AddTransient<IPewoWorkerService, PewoWorkerService>();

// -- PEWO: BlobServiceClient (if not already registered for other uses) --------
// Uses Managed Identity in AKS. Requires Azure.Identity + Azure.Storage.Blobs packages.
services.AddSingleton(sp =>
{
    var accountUrl = Environment.GetEnvironmentVariable("BlobAccountUrl")
        ?? "https://<your-storage-account>.blob.core.windows.net";
    return new Azure.Storage.Blobs.BlobServiceClient(
        new Uri(accountUrl),
        new Azure.Identity.DefaultAzureCredential());
});

// =============================================================================
// appsettings.json — add these 2 entries to KeyVaultSettings:
//
//   "PewoApiKey":         "PewoApiKey",
//   "PewoDataServiceUrl": "PewoDataServiceUrl"
//
// These reference Key Vault secret names. The environment variable names
// (PewoApiKey, PewoDataServiceUrl) are resolved at runtime.
// =============================================================================
