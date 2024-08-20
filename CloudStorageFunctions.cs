using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Elysian.Application.Exceptions;
using Elysian.Application.Interfaces;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using MediatR;
using Elysian.Application.Features.CloudStorage.Queries;
using Elysian.Application.Extensions;

namespace Elysian
{
    public class CloudStorageFunctions(ILogger<CloudStorageFunctions> logger, IMediator mediatr, IClaimsPrincipalAccessor claimsPrincipalAccessor)
    {
        [Function("GenerateSasToken")]
        public async Task<HttpResponseData> GenerateSasToken([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            if (!claimsPrincipalAccessor.IsAuthenticated)
            {
                throw new ForbiddenAccessException();
            }

            var fileName = req.Query["fileName"] ?? throw new CustomValidationException();

            var response = await mediatr.Send(new GenerateSasTokenQuery(fileName));


            return await req.WriteJsonResponseAsync(new
            {
                sasToken = response.SasToken,
                containerName = response.ContainerName,
                accountName = response.AccountName,
                blobName = response.BlobName,
                folderId = response.StorageId
            });
        }

    }
}
