// Import packages
#pragma warning disable SKEXP0050, SKEXP0010, SKEXP0001

namespace SemanticKernel.ConsoleApp.Models
{
    class ConfigurationModel
    {
        public required string OpenAIModel { get; init; }
        public required string OpenAIEndpoint { get; init; }
        public required string OpenAIKey { get; init; }
        public required string OpenAIEmbeddingModel { get; init; }
        public required string OpenAIEmbeddingEndpoint { get; init; }
        public required string OpenAIEmbeddingKey { get; init; }
        public required string BingKey { get; init; }
    }
}