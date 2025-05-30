﻿namespace SemanticKernel.ConsoleApp.Models
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
        internal required string AzureAISearchKey { get; init; }
        internal required string AzureAISearchEndpoint { get; init; }
        internal required string AzureAIInferenceKey { get; init; }
        internal required string AzureAIInferenceEndpoint { get; init; }
        internal required string AzureAIInferenceModel { get; init; }
    }
}