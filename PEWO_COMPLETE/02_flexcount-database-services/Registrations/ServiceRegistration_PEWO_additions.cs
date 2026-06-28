// ─────────────────────────────────────────────────────────────────────────────
// FILE 1: Add to WS.FC.Dapper.Application/Configurations/ServiceRegistration.cs
//         inside the AddApplication() method body.
// ─────────────────────────────────────────────────────────────────────────────

// services.AddTransient<IPewoDataService, PewoDataService>();


// ─────────────────────────────────────────────────────────────────────────────
// FILE 2: Add to WS.FC.DatabaseService.Wrapper/Configuration/ServiceRegistration.cs
//         inside the AddDatabaseServiceDaprClients() method body.
// ─────────────────────────────────────────────────────────────────────────────

// services.ConfigureServiceUrl<IPewoDataServiceClient, PewoDataServiceClient>(dataServiceBaseUrl, "PewoData");


// ─────────────────────────────────────────────────────────────────────────────
// NOTE: MediatR handlers in WS.FC.Dapper.Application.Handlers.Pewo are
// auto-discovered by the existing assembly scan — no additional registration.
// ─────────────────────────────────────────────────────────────────────────────
