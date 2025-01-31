using System.Threading.Tasks;
using System.Collections.Generic;
using AutomationForm.Models;
using System;
using Azure.Data.Tables;
using System.Linq;
using Azure.Storage.Blobs;
using System.IO;
using Azure.Storage.Blobs.Models;

namespace AutomationForm.Services
{
    public class AppFileService : ITableStorageService<AppFile>
    {
        private readonly TableClient client;
        private readonly BlobContainerClient blobContainerClient;
        public AppFileService(TableStorageService tableStorageService, IDatabaseSettings settings)
        {
            client = tableStorageService.GetTableClient(settings.AppFileCollectionName).Result;
            blobContainerClient = tableStorageService.GetBlobClient(settings.AppFileBlobCollectionName).Result;
        }

        public async Task<List<AppFile>> GetNAsync(int n)
        {
            List<AppFile> files = new List<AppFile>();
            await foreach (BlobItem blobItem in blobContainerClient.GetBlobsAsync())
            {
                files.Add(new AppFile() { Id = blobItem.Name, Content = blobItem.Properties.ContentHash });
            }
            return files;
        }

        public async Task<List<AppFile>> GetAllAsync()
        {
            List<AppFile> files = new List<AppFile>();
            await foreach (BlobItem blobItem in blobContainerClient.GetBlobsAsync())
            {
                files.Add(new AppFile() { Id = blobItem.Name, Content = blobItem.Properties.ContentHash });
            }
            return files;
        }

        public async Task<List<AppFile>> GetAllAsync(string partitionKey)
        {
            List<AppFile> files = new List<AppFile>();
            await foreach (BlobItem blobItem in blobContainerClient.GetBlobsAsync())
            {
                files.Add(new AppFile() { Id = blobItem.Name, Content = blobItem.Properties.ContentHash });
            }
            return files;
        }

        public async Task<AppFile> GetByIdAsync(string rowKey, string partitionKey)
        {
            BlobClient blobClient = blobContainerClient.GetBlobClient(rowKey);
            using var memoryStream = new MemoryStream();
            await blobClient.DownloadToAsync(memoryStream);
            return new AppFile() { Id = rowKey, Content = memoryStream.ToArray() };
        }

        public Task<AppFile> GetDefault()
        {
            return null;
        }

        public Task CreateAsync(AppFile file)
        {
            BlobClient blobClient = blobContainerClient.GetBlobClient(file.Id);
            blobClient.UploadAsync(new BinaryData(file.Content));
            AppFileEntity fileEntity = new AppFileEntity(file.Id, blobClient.Uri.ToString());
            return client.AddEntityAsync(fileEntity);
        }

        public Task UpdateAsync(AppFile file)
        {
            BlobClient blobClient = blobContainerClient.GetBlobClient(file.Id);
            blobClient. UploadAsync(new BinaryData(file.Content));
            AppFileEntity fileEntity = new AppFileEntity(file.Id, blobClient.Uri.ToString());
            return client.UpsertEntityAsync(fileEntity, TableUpdateMode.Merge);
        }
        
        public Task DeleteAsync(string rowKey, string partitionKey)
        {
            BlobClient blobClient = blobContainerClient.GetBlobClient(rowKey);
            blobClient.DeleteAsync();
            return client.DeleteEntityAsync(partitionKey, rowKey);
        }
    }
}