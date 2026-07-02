using API.Helpers;
using Domain.ApiModels;
using Domain.Constants;
using Domain.Exceptions;
using Domain.Helpers;
using Domain.Helpers.Interfaces;
using Domain.Services.Interfaces;
using Domain.Services.Interfaces.Pewo;
using iTextSharp.text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SendGrid;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using WS.FC.Dapper.Shared.DTOs;
using WS.FC.DatabaseService.Wrapper.Interfaces;

namespace Domain.Services
{
    public class EventService : IEventService
    {
        private readonly ILogger<EventService>         _logger;
        private readonly IEncryptionHelper             _iEncryptionHelper;
        private readonly IEventServiceClient           _iEventServiceClient;
        private readonly IConfigurationService         _configurationService;
        private readonly IDownloadReportsService       _downloadReportsService;
        private readonly ISendEmailService             _iSendEmailService;
        private readonly IOutputElementServiceClient   _outputElementServiceClient;
        private readonly IFtpHelper                    _ftpHelper;
        private readonly IAzureBlobStorageHelper       _azureBlobStorageHelper;
        private readonly IPewoJobDataService           _pewoJobDataService;   // PEWO hook

        public EventService(
            ILogger<EventService>       logger,
            IEncryptionHelper           iEncryptionHelper,
            IEventServiceClient         eventServiceClient,
            IConfigurationService       configurationService,
            IDownloadReportsService     downloadReportsService,
            ISendEmailService           sendEmailService,
            IOutputElementServiceClient outputElementServiceClient,
            IFtpHelper                  ftpHelper,
            IAzureBlobStorageHelper     azureBlobStorageHelper,
            IPewoJobDataService         pewoJobDataService         // PEWO hook
        )
        {
            _logger                     = logger;
            _iEncryptionHelper          = iEncryptionHelper;
            _configurationService       = configurationService;
            _downloadReportsService     = downloadReportsService;
            _iSendEmailService          = sendEmailService;
            _outputElementServiceClient = outputElementServiceClient;
            _iEventServiceClient        = eventServiceClient;
            _ftpHelper                  = ftpHelper;
            _azureBlobStorageHelper     = azureBlobStorageHelper;
            _pewoJobDataService         = pewoJobDataService;
        }

        public async Task<EventDetails> GetEventDetails(int eventId, int eventUserId)
        {
            var data = await _iEventServiceClient.GetEventDetails(eventId, eventUserId, CancellationToken.None);

            if (data != null)
            {
                EventDetails eventDetails = new EventDetails();
                eventDetails.EventId        = data.IdEvent;
                eventDetails.StoreNumber    = data.StoreNumber;
                eventDetails.Status         = data.Status;
                eventDetails.Name           = data.Name;
                eventDetails.CounterPassword = _iEncryptionHelper.Decrypt(data.CounterPassword);
                eventDetails.AuditorPassword = _iEncryptionHelper.Decrypt(data.AuditorPassword);
                eventDetails.ManagerPassword = data.SupervisorPassword;
                eventDetails.Logo            = data.Logo;
                eventDetails.Address         = string.Concat(data.Address1, ",", data.Address2, ",", data.Address3, ",", data.City, ",", data.State, ",", data.Country, ",", data.PostalCode);
                eventDetails.InventoryDate   = (DateTime)data.ScheduledDateTime;
                eventDetails.IsRedirected    = data.IsRedirected;
                eventDetails.TimeZone        = data.TimeZone;
                eventDetails.StdTimeZone     = data.StdTimeZone;
                return eventDetails;
            }
            else
            {
                throw new EventNotFoundException(MessageConstants.EventNotFound, eventId);
            }
        }

        public async Task DeleteInventoryData(DeleteInventoryData deleteInventoryData)
        {
            var eventDetails = await _iEventServiceClient.GetEventAndConfigControlDetails(deleteInventoryData.EventId, CancellationToken.None);
            await _iEventServiceClient.DeleteAllData(deleteInventoryData.EventId, deleteInventoryData.IsClearLocationRanges, deleteInventoryData.IsClearDeviceCountData, CancellationToken.None);

            var eventDetailsDictionary = new Dictionary<string, bool>
                {
                    { WISAppConstants.IsClearLocationRanges, false },
                    { WISAppConstants.IsClearDeviceCountData, false },
                    { WISAppConstants.IsForceValidationFileReDownload, deleteInventoryData.IsForceValidationFileReDownload }
                };
            string eventDetailsJson = JsonConvert.SerializeObject(eventDetailsDictionary);

            var eventLogs = new EventLogsDto
            {
                EventDetails      = eventDetailsJson,
                Is_Deleted        = true,
                Reason            = deleteInventoryData.Reason == WISAppConstants.Other ? deleteInventoryData.ReasonText : deleteInventoryData.Reason,
                IdCustomer        = eventDetails.IdCustomer,
                IdEvent           = deleteInventoryData.EventId,
                LogDateTime       = DateTime.UtcNow,
                Event             = eventDetails.EventGuid.ToString(),
                CreatedDate       = DateTime.UtcNow,
                LastUpdatedDate   = DateTime.UtcNow,
                CreatedBy         = eventDetails.CreatedBy.ToString(),
                UpdatedBy         = eventDetails.UpdatedBy.ToString()
            };
            await _iEventServiceClient.CreateEventLogDetails(eventLogs, CancellationToken.None);
        }

        public async Task ValidateEvent(int eventId, string password)
        {
            var data = await _iEventServiceClient.GetEventById(eventId, CancellationToken.None);

            if (data == null)
                throw new EventNotFoundException(MessageConstants.EventNotFound, eventId);

            if (data.SupervisorPassword != password)
                throw new UnauthorizedAccessException(MessageConstants.InValidPassword);
        }

        public async Task<string> GetInventryWipeStatus(int eventId)
        {
            try
            {
                return await _iEventServiceClient.GetInventoryWipeStatusAsync(eventId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError("RunScheduleEvents Exception occured: {ex.Message} \n More Information: {ex.InnerException?.Message}, \n Stacktrace: {ex}", ex.Message, ex.InnerException?.Message, ex);
                throw;
            }
        }

        public async Task CloseInventory(int eventId, int eventUserId, string folderPath, CancellationToken cancellationToken, string host)
        {
            var eventDetails = await _iEventServiceClient.GetEventCondition(x => x.IdEvent == eventId, cancellationToken);
            var fileMetaData = await _iEventServiceClient.GetFileMetadata(eventId, cancellationToken);
            await SendEmailReports(eventDetails, fileMetaData, folderPath, host);
            await SendEmailOutputs(eventDetails, fileMetaData, folderPath, host);
            await SendEmailOutputBundels(eventDetails, folderPath, host);

            if (cancellationToken.IsCancellationRequested)
                return;

            eventDetails.Status            = (Enum.GetName(typeof(Enums.Enums.EventStatus), Enums.Enums.EventStatus.Closed)).ToString();
            eventDetails.LastUpdatedDate   = DateTime.UtcNow;
            eventDetails.UpdatedBy         = eventUserId;
            eventDetails.ScheduledCloseTime = DateTime.UtcNow;

            await _iEventServiceClient.Update(eventDetails, cancellationToken);

            // ── PEWO hook — prime ON_EVENT_CLOSE workflows ────────────────────
            // Must be after Update so event is officially Closed before PEWO runs.
            // Fix 10: EventGuid null guard — skip PEWO if EventGuid unavailable.
            //         A run without EventGuid cannot locate source files in blob storage.
            // Fix 17: Cancellation token guard — log explicitly if PEWO skipped due to cancellation.
            //         Event close itself is unaffected either way.

            if (eventDetails.EventGuid == null || eventDetails.EventGuid == Guid.Empty)
            {
                _logger.LogWarning(
                    "[PEWO] Skipping event-close prime — EventGuid is null or empty. EventId={Id}. " +
                    "PEWO cannot locate source files without EventGuid. Investigate event data integrity.",
                    eventId);
            }
            else if (cancellationToken.IsCancellationRequested)
            {
                // Fix 17: Log explicitly — PEWO skipped due to cancellation, not silently dropped
                _logger.LogWarning(
                    "[PEWO] Skipping event-close prime — cancellation requested after event Update. " +
                    "EventId={Id} EventGuid={Guid}. PEWO will not run for this event. " +
                    "If PEWO should run, re-trigger via POST /api/Pewo/event-close.",
                    eventId, eventDetails.EventGuid);
            }
            else
            {
                try
                {
                    await _pewoJobDataService.CreateRunOnEventCloseAsync(
                        idCustomer:          eventDetails.IdCustomer,
                        idEvent:             eventDetails.IdEvent,
                        storeNo:             eventDetails.StoreNumber ?? string.Empty,
                        storeName:           eventDetails.IdStoreNavigation?.Name,
                        eventDate:           eventDetails.ScheduledDateTime ?? DateTime.UtcNow,
                        workflowTypeCode:    null,        // null = all ON_EVENT_CLOSE workflows for customer
                        eventGuid:           eventDetails.EventGuid,
                        idStore:             eventDetails.IdStore,
                        eventStatus:         WISAppConstants.EventCloseType,
                        eventScheduledDate:  eventDetails.ScheduledDateTime,
                        cancellationToken:   cancellationToken);

                    _logger.LogInformation(
                        "[PEWO] event-close prime succeeded. EventId={Id} EventGuid={Guid}",
                        eventId, eventDetails.EventGuid);
                }
                catch (Exception ex)
                {
                    // Never block event close if PEWO fails — log and continue
                    _logger.LogError(ex,
                        "[PEWO] event-close prime failed. EventId={Id} EventGuid={Guid}. " +
                        "Event close succeeded. PEWO can be re-triggered via POST /api/Pewo/event-close.",
                        eventId, eventDetails.EventGuid);
                }
            }
            // ─────────────────────────────────────────────────────────────────
        }

        public async Task UnlockInventory(int eventId, int eventUserId)
        {
            var eventDetails = await _iEventServiceClient.GetEventCondition(x => x.IdEvent == eventId, CancellationToken.None);
            bool fullstoreenabled = false;
            bool groupenabled = false;
            var configuration = await _iEventServiceClient.GetEventAndConfigControlDetails(eventId, CancellationToken.None);
            if (configuration != null)
            {
                var configurationData = JsonConvert.DeserializeObject<Dictionary<string, object>>(configuration.IdConfigNavigation.Configuration);

                if (configurationData != null && configurationData.Count > 0)
                {
                    var fullStoreJsonString = configurationData[WISAppConstants.FullStoreVariances].ToString();
                    if (!string.IsNullOrEmpty(fullStoreJsonString))
                    {
                        var fullstore = new VarianceConfig();
                        fullstore = JsonConvert.DeserializeObject<VarianceConfig>(fullStoreJsonString);
                        if (fullstore != null) { fullstoreenabled = fullstore.Enabled; }
                    }
                    var groupJsonString = configurationData[WISAppConstants.GroupedVariances].ToString();
                    if (!string.IsNullOrEmpty(groupJsonString))
                    {
                        var group = new VarianceConfig();
                        group = JsonConvert.DeserializeObject<VarianceConfig>(groupJsonString);
                        if (group != null) { groupenabled = group.Enabled; }
                    }
                }
            }
            if (fullstoreenabled || groupenabled)
                eventDetails.Status = WISAppConstants.EventSoftClose;
            else
                eventDetails.Status = WISAppConstants.EventStatusInProgress;

            eventDetails.LastUpdatedDate = DateTime.Now;
            await UpdateEventStatus(eventId, eventUserId, WISAppConstants.EventInProgress);
        }

        public async Task UpdateEventStatus(int eventId, int eventUserId, string eventStatus)
        {
            var eventDetails = await _iEventServiceClient.GetByCondition(x => x.IdEvent == eventId, CancellationToken.None);
            eventDetails.Status          = eventStatus;
            eventDetails.LastUpdatedDate = DateTime.UtcNow;
            eventDetails.UpdatedBy       = eventUserId;
            await _iEventServiceClient.Update(eventDetails, CancellationToken.None);
        }

        public async Task LockInventory(int eventId, int eventUserId)
        {
            var eventDetails = await _iEventServiceClient.GetEventCondition(x => x.IdEvent == eventId, CancellationToken.None);
            await UpdateEventStatus(eventId, eventUserId, WISAppConstants.EventLocked);
        }

        public async Task SendEmailOutputBundels(EventDto eventDetails, string folderPath, string host)
        {
            var outputBundlesFiles = await _configurationService.DownloadOutputBundlesFromConfigFile(eventDetails.IdEvent);
            await _iEventServiceClient.Update(eventDetails, CancellationToken.None);
            bool IsSendReportsorBundles = await SendReportsorBundles(eventDetails.IdEvent);
            if (!IsSendReportsorBundles) { return; }
            var download = await _downloadReportsService.DownloadFiles(eventDetails.IdEvent, host, eventDetails.IdStoreNavigation.SiteId);
            foreach (var item in outputBundlesFiles.Select(l => new { l.Destination.Emails, l.Destination.FtpDirectory, l.FileName, l.Name }))
            {
                if (item?.Emails != null && item?.Emails.Count > 0)
                {
                    await _iSendEmailService.SendEmail(item?.Emails, item.FileName, item.FileName, WISAppConstants.OutputsBundles, eventDetails.IdStoreNavigation.SiteId, eventDetails.IdCustomerNavigation.Name, (DateTime)eventDetails.ScheduledDateTime, folderPath, download.FirstOrDefault(x => x.Key == item.Name).Value, item.FtpDirectory, eventDetails.IdCustomer, true, host);
                }
            }
        }

        public async Task SendEmailOutputs(EventDto eventDetails, List<FileGenerationMetadataDTO> fileMetadataDTO, string folderPath, string host)
        {
            bool IsSendReportsorBundles = await SendReportsorBundles(eventDetails.IdEvent);
            if (!IsSendReportsorBundles) return;

            var outputFilesList = await _outputElementServiceClient.GetAllOutputsByCondition(x => x.IdEvent == eventDetails.IdEvent, CancellationToken.None);
            var outputsData = await _configurationService.DownloadReportsFromConfigFile(eventDetails.IdEvent, WISAppConstants.Outputs);

            List<string> emailList = [];
            List<string> notifyList = [];
            string ftpDirectory = string.Empty;
            foreach (var outputFiles in outputFilesList)
            {
                emailList.Clear();
                notifyList.Clear();
                ftpDirectory = string.Empty;
                var configOutputFileData = outputsData.FirstOrDefault(x => x.FileName == outputFiles.FileName);
                if (configOutputFileData != null)
                {
                    emailList    = configOutputFileData.Destination?.Email ?? emailList;
                    notifyList   = configOutputFileData.Notification?.Email ?? notifyList;
                    var ftpName  = configOutputFileData.Destination?.FtpDirectory;
                    ftpDirectory = (string.IsNullOrEmpty(ftpName) || ftpName.ToLower() == WISAppConstants.None) ? string.Empty : ftpName;
                }
                var fileUrl  = outputFilesList.Where(x => x.FileName == outputFiles.FileName).Select(x => !string.IsNullOrEmpty(x.TextFilePath) ? x.TextFilePath : x.ExcelFilePath).FirstOrDefault();
                var fileName = GetFileName(fileUrl);

                if (!string.IsNullOrEmpty(fileName))
                {
                    fileName = HttpUtility.UrlDecode(fileName);

                    Response emailResult   = null;
                    bool ftpResult         = true;
                    bool sendMailResult    = true;
                    byte[] downloadFile    = null;
                    string filePath        = eventDetails.EventGuid + WISAppConstants.Slash + fileName;

                    if (emailList != null && emailList.Count > 0)
                    {
                        try
                        {
                            emailResult = await _iSendEmailService.SendEmail(emailList, filePath, fileName, WISAppConstants.OutputFilesContainer, eventDetails.IdStoreNavigation.SiteId, eventDetails.IdCustomerNavigation.Name, (DateTime)eventDetails.ScheduledDateTime, folderPath, null, [ftpDirectory], eventDetails.IdCustomer, false, host);
                            if (emailResult != null)
                                _logger.LogInformation($"CloseInventory: Output File {fileName} sent to Mail for event {eventDetails.IdEvent}. Status: {emailResult.IsSuccessStatusCode}");
                        }
                        catch (Exception ex)
                        {
                            sendMailResult = false;
                            _logger.LogError($"CloseInventory: Exception occured while sending file {fileName} to e-mail: {ex.Message} \n More Information: {ex.InnerException?.Message}, \n Stacktrace: {ex}", ex.Message, ex.InnerException?.Message, ex);
                        }
                    }

                    if (ftpDirectory != "" && (sendMailResult) && IsSendReportsorBundles)
                    {
                        try
                        {
                            if (downloadFile is null)
                                downloadFile = await _azureBlobStorageHelper.DownloadReportsFile(filePath, WISAppConstants.OutputFilesContainer);
                            ftpResult = await _ftpHelper.SendFilesToFTP(downloadFile, fileName, ftpDirectory, eventDetails.IdCustomer);
                            _logger.LogInformation($"CloseInventory: Output File {fileName} sent to FTP for event {eventDetails.IdEvent}. Status: {ftpResult}");
                        }
                        catch (Exception ex)
                        {
                            ftpResult = false;
                            _logger.LogError($"CloseInventory: Exception occured while saving file {fileName} to FTP: {ex.Message} \n More Information: {ex.InnerException?.Message}, \n Stacktrace: {ex}", ex.Message, ex.InnerException?.Message, ex);
                        }
                    }

                    if (notifyList != null && notifyList.Count > 0 && (sendMailResult) && (ftpResult) && IsSendReportsorBundles)
                    {
                        try
                        {
                            var fileInfo = fileMetadataDTO.FirstOrDefault(fm => fm.IdElement == outputFiles.IdOutputElement && fm.ElementType == WISAppConstants.OutputFile) ?? new FileGenerationMetadataDTO();
                            if (fileInfo != null)
                                await _iSendEmailService.SendNotification(notifyList, fileName, fileName, folderPath, eventDetails.IdStoreNavigation.SiteId, fileInfo.RecordCount, 0, fileInfo.TotalQuantity, fileInfo.ExtensionValue, null, false);
                            else
                                await _iSendEmailService.SendNotification(notifyList, fileName, fileName, folderPath, eventDetails.IdStoreNavigation.SiteId, 0, 0, 0, 0, null, false);
                            _logger.LogInformation($"CloseInventory: Output File {fileName} info is notified to Mail for event {eventDetails.IdEvent}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"CloseInventory: Exception occured while notifying output file {fileName} to e-mail: {ex.Message} \n More Information: {ex.InnerException?.Message}, \n Stacktrace: {ex}", ex.Message, ex.InnerException?.Message, ex);
                        }
                    }

                    if (!sendMailResult)
                        throw new Exception("Error occured while Closing the Inventory. Failed to send data via e-mail.");
                    else if (!ftpResult)
                        throw new Exception("Error occured while Closing the Inventory. Failed to send data via FTP.");
                }
            }
        }

        public async Task SendEmailReports(EventDto eventDetails, List<FileGenerationMetadataDTO> fileMetadataDTO, string folderPath, string host)
        {
            bool IsSendReportsorBundles = await SendReportsorBundles(eventDetails.IdEvent);
            if (!IsSendReportsorBundles) return;
            var reportList  = await _configurationService.DownloadReportsFromConfigurationFile(eventDetails.IdEvent);
            var reportsData = await _configurationService.DownloadReportsFromConfigFile(eventDetails.IdEvent, WISAppConstants.Reports);
            List<string> emailList  = [];
            List<string> notifyList = [];
            string ftpDirectory     = string.Empty;
            foreach (var report in reportList)
            {
                emailList.Clear();
                notifyList.Clear();
                ftpDirectory = string.Empty;
                var configReportFileData = reportsData.FirstOrDefault(x => x.FileName == report.FileName);
                if (configReportFileData != null)
                {
                    emailList    = configReportFileData.Destination?.Email ?? emailList;
                    notifyList   = configReportFileData.Notification?.Email ?? notifyList;
                    var ftpName  = configReportFileData.Destination?.FtpDirectory;
                    ftpDirectory = (string.IsNullOrEmpty(ftpName) || ftpName.ToLower() == WISAppConstants.None) ? string.Empty : ftpName;
                }
                var fileUrl  = reportList.Where(x => x.FileName == report.FileName).Select(x => x.PdfFilePath).FirstOrDefault();
                var fileName = GetFileName(fileUrl);
                if (!string.IsNullOrEmpty(fileName))
                {
                    fileName = HttpUtility.UrlDecode(fileName);

                    Response emailResult   = null;
                    bool sendMailResult    = true;
                    bool ftpResult         = true;
                    byte[] downloadFile    = null;
                    string fileExtension   = Path.GetExtension(fileName);
                    string filePath        = eventDetails.EventGuid + WISAppConstants.Slash + fileName;

                    if (emailList != null && emailList.Count > 0)
                    {
                        try
                        {
                            emailResult = null;
                            if (!string.IsNullOrEmpty(fileExtension) && fileExtension.Equals(WISAppConstants.PDF, StringComparison.OrdinalIgnoreCase))
                                emailResult = await _iSendEmailService.SendEmail(emailList, filePath, fileName, WISAppConstants.ReportFilesContainer, eventDetails.IdStoreNavigation.SiteId, eventDetails.IdCustomerNavigation.Name, (DateTime)eventDetails.ScheduledDateTime, folderPath, null, [ftpDirectory], eventDetails.IdCustomer, false, host);
                            else
                            {
                                fileName    = Path.GetFileNameWithoutExtension(fileName) + WISAppConstants.PDF;
                                emailResult = await _iSendEmailService.SendEmail(emailList, filePath, fileName, WISAppConstants.ReportFilesContainer, eventDetails.IdStoreNavigation.SiteId, eventDetails.IdCustomerNavigation.Name, (DateTime)eventDetails.ScheduledDateTime, folderPath, null, [ftpDirectory], eventDetails.IdCustomer, false, host);
                            }
                            if (emailResult != null)
                                _logger.LogInformation($"CloseInventory: Report File {fileName} sent to Mail for event {eventDetails.IdEvent}. Status: {emailResult.IsSuccessStatusCode}");
                        }
                        catch (Exception ex)
                        {
                            sendMailResult = false;
                            _logger.LogError($"CloseInventory: Exception occured while sending file {fileName} to e-mail: {ex.Message} \n More Information: {ex.InnerException?.Message}, \n Stacktrace: {ex}", ex.Message, ex.InnerException?.Message, ex);
                        }
                    }

                    if (ftpDirectory != "" && (sendMailResult))
                    {
                        try
                        {
                            if (downloadFile is null)
                                downloadFile = await _azureBlobStorageHelper.DownloadReportsFile(filePath, WISAppConstants.ReportFilesContainer);
                            ftpResult = await _ftpHelper.SendFilesToFTP(downloadFile, fileName, ftpDirectory, eventDetails.IdCustomer);
                            _logger.LogInformation($"CloseInventory: Report File {fileName} sent to FTP for event {eventDetails.IdEvent}. Status: {ftpResult}");
                        }
                        catch (Exception ex)
                        {
                            ftpResult = false;
                            _logger.LogError($"CloseInventory Exception occured while saving file {fileName} to FTP: {ex.Message} \n More Information: {ex.InnerException?.Message}, \n Stacktrace: {ex}", ex.Message, ex.InnerException?.Message, ex);
                        }
                    }

                    if (notifyList != null && notifyList.Count > 0 && (sendMailResult) && (ftpResult))
                    {
                        try
                        {
                            var fileInfo = fileMetadataDTO.FirstOrDefault(fm => fm.IdElement == report.IdReportElement && fm.ElementType == WISAppConstants.ReportFile) ?? new FileGenerationMetadataDTO();
                            if (fileInfo != null)
                                await _iSendEmailService.SendNotification(notifyList, fileName, fileName, folderPath, eventDetails.IdStoreNavigation.SiteId, fileInfo.RecordCount, 0, fileInfo.TotalQuantity, fileInfo.ExtensionValue, null, false);
                            else
                                await _iSendEmailService.SendNotification(notifyList, fileName, fileName, folderPath, eventDetails.IdStoreNavigation.SiteId, 0, 0, 0, 0, null, false);
                            _logger.LogInformation($"CloseInventory: Report File {fileName} info is notified to Mail for event {eventDetails.IdEvent}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"CloseInventory: Exception occured while notifying report file {fileName} to e-mail: {ex.Message} \n More Information: {ex.InnerException?.Message}, \n Stacktrace: {ex}", ex.Message, ex.InnerException?.Message, ex);
                        }
                    }

                    if (!sendMailResult)
                        throw new Exception("Error occured while Closing the Inventory. Failed to send data via e-mail.");
                    else if (!ftpResult)
                        throw new Exception("Error occured while Closing the Inventory. Failed to send data via FTP.");
                }
            }
        }

        public string GetFileName(string fileUrl)
        {
            if (!string.IsNullOrEmpty(fileUrl))
            {
                int lastSlashIndex = fileUrl.LastIndexOf('/');
                if (lastSlashIndex >= 0)
                    return fileUrl.Substring(lastSlashIndex + 1);
            }
            return string.Empty;
        }

        public async Task CheckEventLockStatus(int eventId)
        {
            string[] lockedStatus = { WISAppConstants.EventLocked, WISAppConstants.EventLockInProgress, WISAppConstants.EventSoftClose };
            var eventInfo = await _iEventServiceClient.GetByCondition(e => e.IdEvent == eventId, CancellationToken.None);
            if (lockedStatus.Contains(eventInfo.Status))
                throw new EventLockedException($"Event is in {eventInfo.Status}", eventInfo);
        }

        public async Task<bool> SendReportsorBundles(int eventId)
        {
            var eventAncillaryDetails = await _iEventServiceClient.GetEventAncillaryDetails(eventId, CancellationToken.None);
            if (eventAncillaryDetails != null && (eventAncillaryDetails.LookupCode.IdLookup > 0 && eventAncillaryDetails.LookupCode.LookupCode == WISAppConstants.TestEventAncillary && eventAncillaryDetails.IsSendFinalReports.HasValue && !eventAncillaryDetails.IsSendFinalReports.Value))
                return false;
            else
                return true;
        }
    }
}
