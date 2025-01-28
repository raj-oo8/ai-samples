using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using SemanticKernel.ConsoleApp.Models;
using System.ComponentModel;

#pragma warning disable SKEXP0050, SKEXP0010, SKEXP0001

namespace SemanticKernel.ConsoleApp.Plugins
{
    public class VectorSearchPlugin
    {
        private readonly IEmbeddingGenerationService<string, float> _embeddingService;
        private readonly IVectorStoreRecordCollection<Guid, VectorModel> _vectorStoreRecordCollection;

        public VectorSearchPlugin(IEmbeddingGenerationService<string, float> embeddingService, IVectorStoreRecordCollection<Guid, VectorModel> vectorStoreRecordCollection)
        {
            _embeddingService = embeddingService;
            _vectorStoreRecordCollection = vectorStoreRecordCollection;
        }

        [KernelFunction("EcoGroceries")]
        [Description("\"Call center transcripts from EcoGroceries by multiple agents with multiple customers.")]
        public async Task<string> SearchAsync(string userInput)
        {
            if(string.IsNullOrWhiteSpace(userInput))
            {
                return string.Empty;
            }

            // Generate embedding for the user input
            var userInputEmbedding = await _embeddingService.GenerateEmbeddingAsync(userInput);
            // Perform vector search using the generated embedding
            var searchResult = await _vectorStoreRecordCollection.VectorizedSearchAsync(userInputEmbedding);

            // Stringify the search results
            var searchResultsText = string.Empty;
            await foreach (var record in searchResult.Results)
            {
                searchResultsText += record.Record.Text + "\n";
            }

            return searchResultsText;
        }
    }
}
