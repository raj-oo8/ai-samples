using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using SemanticKernel.ConsoleApp.Models;
using System.ComponentModel;

#pragma warning disable SKEXP0050, SKEXP0010, SKEXP0001

namespace SemanticKernel.ConsoleApp.Plugins
{
    public class AzureAISearchPlugin
    {
        private readonly IVectorStoreRecordCollection<Guid, VectorModel> _vectorStoreRecordCollection;
        private readonly SearchIndexClient _searchClient;

        public AzureAISearchPlugin(SearchIndexClient searchClient, IVectorStoreRecordCollection<Guid, VectorModel> vectorStoreRecordCollection)
        {
            _vectorStoreRecordCollection = vectorStoreRecordCollection;
            _searchClient = searchClient;
        }

        [KernelFunction("EcoGroceries")]
        [Description("\"Call center transcripts from EcoGroceries by multiple agents with multiple customers.")]
        public async Task<string> SearchAsync(string userInput)
        {
            if(string.IsNullOrWhiteSpace(userInput))
            {
                return string.Empty;
            }

            // Stringify the search results
            var searchResultsText = string.Empty;

            return searchResultsText;
        }
    }
}
