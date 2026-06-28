// ─────────────────────────────────────────────────────────────────────────────
// FILE: Domain/Helpers/AzureBlobStorageHelper_PEWO_addition.cs
//
// NO CHANGES REQUIRED to IAzureBlobStorageHelper or AzureBlobStorageHelper.
//
// PewoStepService uses existing methods only:
//   DownloadFileBlob(containerName, blobName) → MemoryStream
//     Note: interface names params (blobName, fileName) but impl treats
//           first param as container, second as blob path. PEWO calls it correctly.
//   UploadFileAsync(containerName, stream, fileName) → Task
//
// PewoStepService performs blob LISTING inline using BlobContainerClient
// directly from _configuration[WISAppConstants.VaultBlobKey] — same pattern
// as TotalsValidationService.getLatestNgenFilesFromBlob(). This avoids any
// interface addition.
//
// CRITICAL WARNING — DO NOT USE:
//   DeleteBlob(string blobName) — deletes the ENTIRE CONTAINER, not a single blob.
//   PEWO does not delete anything. If deletion is ever needed, use:
//   DeleteBLOBFile(string blobName, string containerName) — deletes a single blob.
// ─────────────────────────────────────────────────────────────────────────────
