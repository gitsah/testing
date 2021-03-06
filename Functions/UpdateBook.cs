using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Generic;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs.Host;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.KeyVault.Models;
using Newtonsoft.Json.Linq;
using System.Net;
using Microsoft.Azure.Documents;

namespace Functions
{
    public static class UpdateBook
    {
        /* UpdateBook - Updates an existing book. */
        [FunctionName("UpdateBook")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public static async Task<IActionResult> Edit(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "books/{bookid}")]HttpRequest req,
        ILogger log, ExecutionContext context, string bookid)
        {
            log.LogInformation("C# HTTP trigger function processed a request to update a book");
            //set configuration
            var config = new ConfigurationBuilder()
                        .SetBasePath(context.FunctionAppDirectory)
                        .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
                        .AddEnvironmentVariables()
                        .Build();

            var serviceTokenProvider = new AzureServiceTokenProvider();
            var keyVaultClient = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(serviceTokenProvider.KeyVaultTokenCallback));

            // variables for cosmos db
            SecretBundle secrets;
            String uri = String.Empty;
            String key = String.Empty;
            String database = String.Empty;
            String collection = String.Empty;

            try
            {
                secrets = await keyVaultClient.GetSecretAsync($"{config["KEY_VAULT_URI"]}secrets/{config["COSMOS_NAME"]}/");
                JObject details = JObject.Parse(secrets.Value.ToString());
                uri = (string)details["COSMOS_URI"];
                key = (string)details["COSMOS_KEY"];
                database = (string)details["COSMOS_DB"];
                collection = (string)details["COSMOS_COLLECTION"];
            }

            catch (KeyVaultErrorException ex)
            {
                return new ForbidResult("Unable to access secrets in vault!");
            }

            DocumentClient client = new DocumentClient(new Uri(uri), key);
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var book = JsonConvert.DeserializeObject<Book>(requestBody);
            if (!book.Id.Equals(bookid))
            { 
                return new BadRequestObjectResult("Book ids don't match."); // to prevent user from changing book id
            }
            log.LogInformation("Book passed to function: " + book.ToString());
            log.LogInformation("Attempting to retrieve book from database - bookid: " + bookid);
            var option = new FeedOptions { EnableCrossPartitionQuery = true };
            Uri collectionUri = UriFactory.CreateDocumentCollectionUri(database, collection);
            Document document = client.CreateDocumentQuery<Document>(collectionUri, option).Where(b => b.Id == bookid)
                            .AsEnumerable().FirstOrDefault();
            if (document == null)
            {
                return new NotFoundResult();
            }
            Book originalBook = (dynamic)document;
            if (!originalBook.Title.Equals(book.Title)) {
                return new BadRequestObjectResult("The book title cannot be updated.");
            }
            // originalBook.Title = book.Title;
            originalBook.Description = book.Description;
            originalBook.Author = book.Author;
            originalBook.Cover_Image = book.Cover_Image;
            await client.ReplaceDocumentAsync(document.SelfLink, originalBook);
            return new OkObjectResult("Book was updated successfully.");
        }
    }
}
