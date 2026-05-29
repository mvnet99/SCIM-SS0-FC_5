// -----------------------------------------------------------------------------------------
// ADD THIS LINE to ServicesConfiguration.cs inside ConfigureServices()
// Place it with the other Application Services registrations (alphabetical by interface name)
// -----------------------------------------------------------------------------------------

services.AddTransient<INexGenService, NexGenService>();


// -----------------------------------------------------------------------------------------
// ADD THIS to appsettings.json → KeyVaultSettings section
// This tells startup to load the NexGen API key from Key Vault
// -----------------------------------------------------------------------------------------

// In appsettings.json, add to KeyVaultSettings:
// "NexGenApiKey": "NexGenApiKey"

// Then in Azure Key Vault, create a secret named "NexGenApiKey" with a strong random value.
// Logic Apps will pass this same value as the X-Api-Key header.
// -----------------------------------------------------------------------------------------
