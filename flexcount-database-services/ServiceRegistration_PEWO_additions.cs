// =============================================================================
// PEWO additions for flexcount-database-services
// =============================================================================
//
// 1. In WS.FC.Dapper.Application/Configurations/ServiceRegistration.cs
//    add inside AddApplication():
//
//    services.AddTransient<IPewoDataService, PewoDataService>();
//
//
// 2. In WS.FC.DatabaseService.Wrapper/Configuration/ServiceRegistration.cs
//    add inside AddDatabaseServiceDaprClients():
//
//    services.ConfigureServiceUrl<IPewoDataServiceClient, PewoDataServiceClient>(
//        dataServiceBaseUrl, "PewoData");
//
//
// 3. MediatR handler registration (auto-scanned if using Assembly scanning).
//    If handlers are registered explicitly, add:
//
//    services.AddTransient<IRequestHandler<GetDueJobsQuery,           List<DueJobDto>>,            GetDueJobsHandler>();
//    services.AddTransient<IRequestHandler<GetRunResumeQuery,         List<RunResumeStepDto>>,      GetRunResumeHandler>();
//    services.AddTransient<IRequestHandler<AcquireScheduleLockCommand, bool>,                       AcquireScheduleLockHandler>();
//    services.AddTransient<IRequestHandler<ReleaseScheduleLockCommand>,                             ReleaseScheduleLockHandler>();
//    services.AddTransient<IRequestHandler<CreateWorkflowRunCommand>,                               CreateWorkflowRunHandler>();
//    services.AddTransient<IRequestHandler<UpsertStepRunCommand>,                                   UpsertStepRunHandler>();
//    services.AddTransient<IRequestHandler<SetRunTerminalStatusCommand>,                            SetRunTerminalStatusHandler>();
//    services.AddTransient<IRequestHandler<ResetRunForRetryCommand>,                                ResetRunForRetryHandler>();
// =============================================================================
