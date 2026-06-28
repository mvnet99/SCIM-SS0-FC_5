// ─────────────────────────────────────────────────────────────────────────────
// FILE: API/Configurations/ServicesConfiguration_PEWO_additions.cs
//
// Paste these 4 lines into API/Configurations/ServicesConfiguration.cs
// inside the existing AddServices() method body.
//
// NOTE: No BackgroundService — triggering is done via Container App Job (prod)
//       or Postman/PowerShell (local dev). POST /api/Pewo/worker/run is the entry point.
//
// ALSO add to Domain.csproj:
//   <PackageReference Include="Cronos" Version="0.8.4" />
// ─────────────────────────────────────────────────────────────────────────────

// services.AddTransient<IPewoWorkerService,  PewoWorkerService>();
// services.AddTransient<IPewoStepService,    PewoStepService>();
// services.AddTransient<IPewoJobDataService, PewoJobDataService>();
// services.AddTransient<IPewoLogService,     PewoLogService>();
