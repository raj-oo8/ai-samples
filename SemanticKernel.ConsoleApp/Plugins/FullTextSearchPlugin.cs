using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Embeddings;
using SemanticKernel.ConsoleApp.Models;

#pragma warning disable SKEXP0010, SKEXP0001

namespace SemanticKernel.ConsoleApp.Plugins
{
    class FullTextSearchPlugin
    {
        readonly ITextEmbeddingGenerationService _embeddingService;
        readonly IVectorStoreRecordCollection<Guid, VectorModel> _vectorStoreRecordCollection;

        internal FullTextSearchPlugin(
            ITextEmbeddingGenerationService embeddingService, 
            IVectorStoreRecordCollection<Guid, VectorModel> vectorStoreRecordCollection)
        {
            _embeddingService = embeddingService;
            _vectorStoreRecordCollection = vectorStoreRecordCollection;
        }

        internal KernelPlugin GetTextSearchAsync()
        {
            // Create a text search plugin with the InMemory vector store and add to the kernel.
            var searchResult = new VectorStoreTextSearch<VectorModel>(
                _vectorStoreRecordCollection,
                _embeddingService);

            // Create a vector store plugin with the InMemory vector store and add to the kernel
            return searchResult.CreateWithGetTextSearchResults(
                "EcoGroceries", 
                "Call center transcripts from EcoGroceries by multiple agents with multiple customers.");
        }
    }
}
