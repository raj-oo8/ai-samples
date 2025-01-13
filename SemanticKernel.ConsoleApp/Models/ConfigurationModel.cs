namespace SemanticKernel.ConsoleApp.Models
{
    class ConfigurationModel
    {
        internal required string OpenAIModel { get; init; }
        internal required string OpenAIEndpoint { get; init; }
        internal required string OpenAIKey { get; init; }
        internal required string OpenAIEmbeddingModel { get; init; }
        internal required string OpenAIEmbeddingEndpoint { get; init; }
        internal required string OpenAIEmbeddingKey { get; init; }
        internal required string BingKey { get; init; }
    }
}