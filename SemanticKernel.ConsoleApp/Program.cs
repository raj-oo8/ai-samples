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

#pragma warning disable SKEXP0050, SKEXP0010, SKEXP0001

namespace SemanticKernel.ConsoleApp
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                var configurationBuilder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    .AddUserSecrets<Program>();
                IConfiguration configuration = configurationBuilder.Build();

                var openAIModelId = configuration["OPENAI_MODEL"];
                var openAIEndpoint = configuration["OPENAI_ENDPOINT"];
                var openAIApiKey = configuration["OPENAI_KEY"];
                var bingApiKey = configuration["BING_API_KEY"];
                var openAIEmbedKey = configuration["OPENAI_EMBED_KEY"];
                var openAIEmbedEndpoint = configuration["OPENAI_EMBED_ENDPOINT"];
                var openAIEmbedModelId = configuration["OPENAI_EMBED_MODEL"];

                if (string.IsNullOrWhiteSpace(openAIApiKey) ||
                    string.IsNullOrWhiteSpace(openAIEndpoint) ||
                    string.IsNullOrWhiteSpace(openAIModelId) ||
                    string.IsNullOrWhiteSpace(openAIEmbedEndpoint) ||
                    string.IsNullOrWhiteSpace(openAIEmbedKey) ||
                    string.IsNullOrWhiteSpace(openAIEmbedModelId))
                {
                    Console.WriteLine("One or more configuration values are missing. Please check your user secrets.");
                    Console.ReadKey();
                    return;
                }

                // Create a kernel with Azure OpenAI chat completion
                var builder = Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(openAIModelId, openAIEndpoint, openAIApiKey);

                // Add enterprise components
                builder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.Error));

                // Build the kernel
                Kernel kernel = builder.Build();
                var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

                // Create an embedding generation service.
                var textEmbeddingGeneration = new AzureOpenAITextEmbeddingGenerationService(
                        deploymentName: openAIEmbedModelId,
                        endpoint: openAIEmbedEndpoint,
                        apiKey: openAIEmbedKey);

                // Construct an InMemory vector store.
                var vectorStore = new InMemoryVectorStore();
                var collectionName = "records";

                // Delegate which will create a record.
                static DataModel CreateRecord(string text, ReadOnlyMemory<float> embedding)
                {
                    return new()
                    {
                        Key = Guid.NewGuid(),
                        Text = text,
                        Embedding = embedding
                    };
                }

                // Read lines from sample data text file
                Console.WriteLine("Reading sample data from file...");
                string sampleDataFilePath = "sampleData.txt";
                string[] lines = File.ReadAllLines(sampleDataFilePath);

                // Create a record collection from a list of strings using the provided delegate.
                var vectorizedSearch = await CreateCollectionFromListAsync<Guid, DataModel>(
                    vectorStore, collectionName, lines, textEmbeddingGeneration, CreateRecord);

                // Create a text search instance using the InMemory vector store.
                var vectorSearch = new VectorStoreTextSearch<DataModel>(vectorizedSearch, textEmbeddingGeneration);

                // Create a text search plugin with the InMemory vector store and add to the kernel.
                var vectorSearchPlugin = vectorSearch.CreateWithGetTextSearchResults("VectorSearchPlugin");
                kernel.Plugins.Add(vectorSearchPlugin);

                // Add a plugin (the LightsPlugin class is defined below)
                kernel.Plugins.AddFromType<TimePlugin>("Time");
                kernel.Plugins.AddFromType<MathPlugin>("Math");

                if (!string.IsNullOrWhiteSpace(bingApiKey))
                {
                    // Create a text search using Bing search
                    var webSearch = new BingTextSearch(bingApiKey);

                    // Build a text search plugin with Bing search and add to the kernel
                    var searchPlugin = webSearch.CreateWithSearch("SearchPlugin");
                    kernel.Plugins.Add(searchPlugin);
                }

                // Enable planning
                OpenAIPromptExecutionSettings openAIPromptExecutionSettings = new()
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Required(),
                    MaxTokens = 50,
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

                    // Add user input
                    history.AddUserMessage(userInput);

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
                while (userInput is not null);
            }
            catch (Exception exception)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(exception.Message);
                Console.ReadKey();
            }
        }

        internal delegate TRecord CreateRecord<TKey, TRecord>(string text, ReadOnlyMemory<float> vector) where TKey : notnull;

        internal static async Task<IVectorStoreRecordCollection<TKey, TRecord>> CreateCollectionFromListAsync<TKey, TRecord>(
            IVectorStore vectorStore,
            string collectionName,
            string[] entries,
            ITextEmbeddingGenerationService embeddingGenerationService,
            CreateRecord<TKey, TRecord> createRecord)
        where TKey : notnull
        {
            // Get and create collection if it doesn't exist.
            var collection = vectorStore.GetCollection<TKey, TRecord>(collectionName);
            await collection.CreateCollectionIfNotExistsAsync().ConfigureAwait(false);

            // Create records and generate embeddings for them.
            var tasks = entries.Select(entry => Task.Run(async () =>
            {
                var record = createRecord(entry, await embeddingGenerationService.GenerateEmbeddingAsync(entry).ConfigureAwait(false));
                await collection.UpsertAsync(record).ConfigureAwait(false);
            }));
            await Task.WhenAll(tasks).ConfigureAwait(false);

            return collection;
        }

        private sealed class DataModel
        {
            [VectorStoreRecordKey]
            [TextSearchResultName]
            public Guid Key { get; init; }

            [VectorStoreRecordData]
            [TextSearchResultValue]
            public required string Text { get; init; }

            [VectorStoreRecordVector(1536)]
            public ReadOnlyMemory<float> Embedding { get; init; }
        }
    }
}