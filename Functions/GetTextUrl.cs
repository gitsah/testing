﻿using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using MongoDB.Driver;
using Microsoft.Azure.Documents.Client;
using System.Linq;
using MongoDB.Driver.Linq;
using System.Net.Http;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.KeyVault.Models;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Http;


//function to get a texturl for a book
//@author francesco
namespace Functions
{
    public static class GetTextUrl
    {
        [FunctionName("getText")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous,
        "get",
        Route = "books/{bookId}/pages/{pageId}/languages/{languageCode}/text")]
        HttpRequest req,
        string bookid,
        string pageid,
        string languagecode,
        ILogger log,
        ExecutionContext context)
        {
            var status = (StatusCodeResult)new StatusCodeResult(200);
            string result = null;

            try
            {
                log.LogInformation("Http function to POST texturl");

                //get request body
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
                dynamic data = JsonConvert.DeserializeObject(requestBody);
                log.LogInformation($"data -> {data}");

                //get environment variables
                var config = new ConfigurationBuilder()
                        .SetBasePath(context.FunctionAppDirectory)
                        .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                        .AddEnvironmentVariables()
                        .Build();

                //access azure keyvault
                var serviceTokenProvider = new AzureServiceTokenProvider();
                log.LogInformation("serviceTokenProvider");
                var keyVaultClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(serviceTokenProvider.KeyVaultTokenCallback));
                log.LogInformation("keyVaultClient");

                SecretBundle secrets;
                String uri = String.Empty;
                String key = String.Empty;
                String database = String.Empty;
                String collection = String.Empty;

                try
                {
                    //storage account is the keyvault key
                    secrets = await keyVaultClient.GetSecretAsync($"{config["KEY_VAULT_URI"]}secrets/{config["COSMOS_NAME"]}/");
                    //parse json stored in keyvalut
                    JObject details = JObject.Parse(secrets.Value.ToString());
                    uri = (string)details["COSMOS_URI"];
                    key = (string)details["COSMOS_KEY"];
                    database = (string)details["COSMOS_DB"];
                    collection = (string)details["COSMOS_COLLECTION"];

                    log.LogInformation("Secret Values retrieved from KeyVault.");
                }
                catch (Exception kex)
                {
                    status = (StatusCodeResult)new StatusCodeResult(500); //server error
                }

                if (200 == status.StatusCode)
                {
                    //declare client
                    DocumentClient dbClient = new DocumentClient(new Uri(uri), key);
                    log.LogInformation("new DocumentClient");

                    try
                    {
                        var collectionUri = UriFactory.CreateDocumentCollectionUri(database, collection);
                        var query = "SELECT * FROM Books b WHERE b.id=\"" + bookid + "\"";
                        var crossPartition = new FeedOptions { EnableCrossPartitionQuery = true };
                        var documents = dbClient.CreateDocumentQuery(collectionUri, query, crossPartition).ToList();
                        log.LogInformation($"document retrieved -> {documents.Count().ToString()}");

                        //read from db
                        Book b = documents.ElementAt(0);
                        Page p = b.Pages.ElementAt(int.Parse(pageid) - 1);
                        for (int j = 0; j < p.Languages.Count(); j++)
                        {
                            if (p.Languages.ElementAt(j).language.Equals(languagecode))
                            {
                                //if nothing crashed then assign text_url to result
                                result = $"200 text_url={p.Languages.ElementAt(j).Text_Url}"; //read success
                                log.LogInformation($"url retrieved -> {p.Languages.ElementAt(j).Text_Url}");
                            }
                        }

                        status = (null != result ? new StatusCodeResult(200) : new StatusCodeResult(404));

                    }
                    catch (Exception wrt)
                    {
                        status = (StatusCodeResult)new StatusCodeResult(404); //read failed
                    }
                }
                else
                {
                    status = (StatusCodeResult)new StatusCodeResult(400);
                }
            }
            catch (Exception e)
            {
                status = (StatusCodeResult)new StatusCodeResult(400);
            }

            return (null != result ? new ObjectResult(result) : new ObjectResult(status.StatusCode));
        }
    }
}