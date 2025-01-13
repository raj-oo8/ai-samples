using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Connectors.InMemory;
using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Embeddings;
using SemanticKernel.ConsoleApp.Models;
using System.Text.RegularExpressions;

#pragma warning disable SKEXP0010, SKEXP0001

namespace SemanticKernel.ConsoleApp.Stores
{
    class MemoryVectorStore
    {
        internal async static Task<VectorStoreTextSearch<VectorModel>> GetTextSearchAsync(ConfigurationModel configurationModel)
        {            
            // Create an InMemory vector store.
            var inMemoryVectorStore = new InMemoryVectorStore();
            
            // Get a collection of vectors from the InMemory vector store.
            var vectorStoreRecordCollection = inMemoryVectorStore.GetCollection<Guid, VectorModel>("VectorData");
            await vectorStoreRecordCollection.CreateCollectionIfNotExistsAsync().ConfigureAwait(false);

            // Create an embedding generation service with the OpenAI API.
            var azureOpenAITextEmbeddingGenerationService = new AzureOpenAITextEmbeddingGenerationService(
                configurationModel.OpenAIEmbeddingModel,
                configurationModel.OpenAIEmbeddingEndpoint,
                configurationModel.OpenAIEmbeddingKey);

            await GenerateEmbeddings(azureOpenAITextEmbeddingGenerationService, vectorStoreRecordCollection);

            // Create a text search plugin with the InMemory vector store and add to the kernel.
            var searchResult = new VectorStoreTextSearch<VectorModel>(
                vectorStoreRecordCollection, 
                azureOpenAITextEmbeddingGenerationService);
            return searchResult;
        }

        static async Task<IVectorStoreRecordCollection<Guid, VectorModel>> GenerateEmbeddings( 
            AzureOpenAITextEmbeddingGenerationService azureOpenAITextEmbeddingGenerationService,
            IVectorStoreRecordCollection<Guid, VectorModel> vectorStoreRecordCollection)
        {
            string sampleDataFilePath = "Data\\CallTranscripts.txt";
            if (!File.Exists(sampleDataFilePath))
            {
                throw new InvalidOperationException($"Sample data file '{sampleDataFilePath}' not found.");
            }

            // Read the entire file content into a single string
            string text = File.ReadAllText(sampleDataFilePath);

            // Replace all special characters with whitespace
            text = Regex.Replace(text, @"[^\w\s]", " ");

            // Split the text into chunks based on line breaks and blank lines
            string[] chunks = Regex.Split(text, @"(\r?\n){2,}");

            // Remove any empty or whitespace-only chunks
            chunks = chunks.Where(chunk => !string.IsNullOrWhiteSpace(chunk)).ToArray();

            // Generate embeddings for each chunk and add to the vector store
            var tasks = chunks.Select(async item =>
            {
                ReadOnlyMemory<float> embedding = await azureOpenAITextEmbeddingGenerationService.GenerateEmbeddingAsync(item);

                // Create a record and upsert with the already generated embedding.
                await vectorStoreRecordCollection.UpsertAsync(new VectorModel
                {
                    Key = Guid.NewGuid(),
                    Text = item,
                    Embedding = embedding
                });
            });

            await Task.WhenAll(tasks);

            return vectorStoreRecordCollection;
        }
    }
}
