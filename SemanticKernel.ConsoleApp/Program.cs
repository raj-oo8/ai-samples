using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Connectors.InMemory;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.SemanticKernel.Plugins.Core;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using System.ClientModel;
using System.Text.RegularExpressions;

#pragma warning disable SKEXP0050, SKEXP0010, SKEXP0001

namespace SemanticKernel.ConsoleApp
{
    public class Program
    {
        static ConfigurationModel? configurationModel;

        public static async Task Main()
        {
            Console.WriteLine("Starting...");

            try
            {
                var configurationBuilder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddUserSecrets<Program>();
                IConfiguration configuration = configurationBuilder.Build();
                configurationModel = InitializeConfiguation(configuration);

                // Create a kernel with Azure OpenAI chat completion
                var kernelBuilder = Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(configurationModel.OpenAIModel, configurationModel.OpenAIEndpoint, configurationModel.OpenAIKey);

                // Add enterprise components
                kernelBuilder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.Error));

                // Build the kernel
                Kernel kernel = kernelBuilder.Build();
                var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

                // Create an embedding generation service with the OpenAI API.
                var azureOpenAITextEmbeddingGenerationService = new AzureOpenAITextEmbeddingGenerationService(configurationModel.OpenAIEmbeddingModel, configurationModel.OpenAIEmbeddingEndpoint, configurationModel.OpenAIEmbeddingKey);
                // Create an InMemory vector store.
                var inMemoryVectorStore = new InMemoryVectorStore();
                // Get a collection of vectors from the InMemory vector store.
                var vectorStoreRecordCollection = inMemoryVectorStore.GetCollection<Guid, VectorModel>("VectorData");
                await vectorStoreRecordCollection.CreateCollectionIfNotExistsAsync().ConfigureAwait(false);

                // Add plugins
                kernel.Plugins.AddFromType<TimePlugin>("Time");
                kernel.Plugins.AddFromType<MathPlugin>("Math");
                kernel.Plugins.Add(await CreateVectorSearchAsync(azureOpenAITextEmbeddingGenerationService, vectorStoreRecordCollection));
                kernel.Plugins.Add(CreateBingSearch());

                // Enable planning
                OpenAIPromptExecutionSettings openAIPromptExecutionSettings = new()
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Required(),
                    MaxTokens = 75,
                    ChatSystemPrompt = "Answer in one single sentence with collocations"
                };

                // Create a history store the conversation
                var history = new ChatHistory();

                // Initiate a back-and-forth chat
                string? userInput;
                do
                {
                    // Collect user input
                    Console.WriteLine();
                    Console.WriteLine();
                    Console.Write("User > ");
                    userInput = Console.ReadLine();

                    if (string.IsNullOrEmpty(userInput))
                    {
                        Console.WriteLine("User input cannot be null or empty. Please try again.");
                        continue;
                    }

                    // Generate embedding for the user input
                    var userInputEmbedding = await azureOpenAITextEmbeddingGenerationService.GenerateEmbeddingAsync(userInput);
                    // Perform vector search using the generated embedding
                    var searchResult = await vectorStoreRecordCollection.VectorizedSearchAsync(userInputEmbedding);

                    // Stringify the search results
                    var searchResultsText = string.Empty;
                    await foreach (var record in searchResult.Results)
                    {
                        searchResultsText += record.Record.Text;
                    }

                    // Add user input
                    history.AddUserMessage(userInput);
                    history.AddAssistantMessage(searchResultsText);

                    try
                    {
                        // Get the response from the AI
                        var result = chatCompletionService.GetStreamingChatMessageContentsAsync(
                            history,
                            executionSettings: openAIPromptExecutionSettings,
                            kernel: kernel);

                        // Print the results
                        Console.Write("Assistant > ");
                        var response = string.Empty;
                        await foreach (var item in result)
                        {
                            response += item;
                            Console.Write(item);
                        }

                        // Add the message from the agent to the chat history
                        history.AddAssistantMessage(response);

                        if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.ReadKey();
                            break;
                        }
                    }
                    catch (ClientResultException clientException) when (clientException.ToString().Contains("429"))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine(clientException.Message);
                        Console.ResetColor();
                        Console.ReadKey();
                    }
                }
                while (userInput is not null);
            }
            catch (Exception exception)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(exception.Message);
                Console.ReadKey();
            }
        }

        static KernelPlugin CreateBingSearch()
        {
            // Create a text search using Bing search
            var webSearch = new BingTextSearch(configurationModel.BingKey);

            // Build a text search plugin with Bing search and add to the kernel
            return webSearch.CreateWithGetTextSearchResults("SearchPlugin");
        }

        static async Task<KernelPlugin> CreateVectorSearchAsync
            (AzureOpenAITextEmbeddingGenerationService azureOpenAITextEmbeddingGenerationService,
            IVectorStoreRecordCollection<Guid, VectorModel> vectorStoreRecordCollection)
        {
            string sampleDataFilePath = "sampleData.txt";
            if (!File.Exists(sampleDataFilePath))
            {
                throw new InvalidOperationException($"Sample data file '{sampleDataFilePath}' not found.");
            }
            string[] lines = File.ReadAllLines(sampleDataFilePath);
            string[] preprocessedLines = lines.Select(PreprocessText).ToArray();

            // Generate embeddings for each line and add to the InMemory vector store.
            var tasks = preprocessedLines.Select(async item =>
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

            // Create a text search plugin with the InMemory vector store and add to the kernel.
            var searchResult = new VectorStoreTextSearch<VectorModel>(vectorStoreRecordCollection, azureOpenAITextEmbeddingGenerationService);

            // Return the search result
            return searchResult.CreateWithGetTextSearchResults("VectorSearchPlugin");
        }

        // Function to preprocess text
        static string PreprocessText(string text)
        {
            // Remove special characters and extra whitespace
            text = Regex.Replace(text, @"[^\w\s]", string.Empty);
            text = Regex.Replace(text, @"\s+", " ");

            // Convert to lowercase
            text = text.ToLower();

            return text;
        }

        static ConfigurationModel InitializeConfiguation(IConfiguration configuration)
        {
            var openAIModelId = configuration["OPENAI_MODEL"];
            var openAIEndpoint = configuration["OPENAI_ENDPOINT"];
            var openAIKey = configuration["OPENAI_KEY"];
            var bingKey = configuration["BING_KEY"];
            var openAIEmbeddingKey = configuration["OPENAI_EMBEDDING_KEY"];
            var openAIEmbeddingEndpoint = configuration["OPENAI_EMBEDDING_ENDPOINT"];
            var openAIEmbeddingModel = configuration["OPENAI_EMBEDDING_MODEL"];

            if (string.IsNullOrWhiteSpace(openAIKey) ||
                string.IsNullOrWhiteSpace(openAIEndpoint) ||
                string.IsNullOrWhiteSpace(openAIModelId) ||
                string.IsNullOrWhiteSpace(openAIEmbeddingEndpoint) ||
                string.IsNullOrWhiteSpace(bingKey) ||
                string.IsNullOrWhiteSpace(openAIEmbeddingKey) ||
                string.IsNullOrWhiteSpace(openAIEmbeddingModel))
            {
                throw new InvalidOperationException("One or more configuration values are missing. Please check your user secrets.");
            }

            return new ConfigurationModel
            {
                OpenAIModel = openAIModelId,
                OpenAIEndpoint = openAIEndpoint,
                OpenAIKey = openAIKey,
                OpenAIEmbeddingModel = openAIEmbeddingModel,
                OpenAIEmbeddingEndpoint = openAIEmbeddingEndpoint,
                OpenAIEmbeddingKey = openAIEmbeddingKey,
                BingKey = bingKey
            };
        }

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

        class VectorModel
        {
            [VectorStoreRecordKey]
            [TextSearchResultName]
            public Guid Key { get; init; }

            [VectorStoreRecordData]
            [TextSearchResultValue]
            public required string Text { get; init; }

            [VectorStoreRecordVector(3072)]
            public ReadOnlyMemory<float> Embedding { get; init; }
        }
    }
}