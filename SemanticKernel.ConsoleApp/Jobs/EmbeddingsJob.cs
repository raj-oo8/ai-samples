using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Embeddings;
using SemanticKernel.ConsoleApp.Models;
using System.Text.RegularExpressions;

#pragma warning disable SKEXP0010, SKEXP0001

namespace SemanticKernel.ConsoleApp.Jobs
{
    partial class EmbeddingsJob
    {
        [GeneratedRegex(@"[^\w\s]")]
        private static partial Regex SpecialCharactersRegex();

        [GeneratedRegex(@"(\r?\n){2,}")]
        private static partial Regex LineBreaksRegex();

        readonly IEmbeddingGenerationService<string, float> _embeddingService;
        readonly IVectorStoreRecordCollection<Guid, VectorModel> _vectorStoreRecordCollection;

        internal EmbeddingsJob(IEmbeddingGenerationService<string, float> embeddingService,
            IVectorStoreRecordCollection<Guid, VectorModel> vectorStoreRecordCollection)
        {
            _embeddingService = embeddingService;
            _vectorStoreRecordCollection = vectorStoreRecordCollection;
        }

        internal async Task<IVectorStoreRecordCollection<Guid, VectorModel>> GenerateEmbeddings()
        {
            string sampleDataFilePath = "Data\\CallTranscripts.txt";
            if (!File.Exists(sampleDataFilePath))
            {
                throw new InvalidOperationException($"Sample data file '{sampleDataFilePath}' not found.");
            }

            // Read the entire file content into a single string
            string text = File.ReadAllText(sampleDataFilePath);

            // Replace all special characters with whitespace
            text = SpecialCharactersRegex().Replace(text, " ");

            // Split the text into chunks based on line breaks and blank lines
            string[] chunks = LineBreaksRegex().Split(text);

            // Remove any empty or whitespace-only chunks
            chunks = chunks.Where(chunk => !string.IsNullOrWhiteSpace(chunk)).ToArray();

            // Generate embeddings for each chunk and add to the vector store
            var tasks = chunks.Select(async item =>
            {
                ReadOnlyMemory<float> embedding = await _embeddingService.GenerateEmbeddingAsync(item);

                // Create a record and upsert with the already generated embedding.
                await _vectorStoreRecordCollection.UpsertAsync(new VectorModel
                {
                    Key = Guid.NewGuid(),
                    Text = item,
                    Embedding = embedding
                });
            });

            await Task.WhenAll(tasks);

            return _vectorStoreRecordCollection;
        }
    }
}
