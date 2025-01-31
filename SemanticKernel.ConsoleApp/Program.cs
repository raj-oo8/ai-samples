using Azure;
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
using SemanticKernel.ConsoleApp.Jobs;
using SemanticKernel.ConsoleApp.Models;
using SemanticKernel.ConsoleApp.Plugins;
using System.ClientModel;
using System.Net.Sockets;

#pragma warning disable SKEXP0050, SKEXP0010, SKEXP0001

namespace SemanticKernel.ConsoleApp
{
    public partial class Program
    {
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
                ConfigurationModel configurationModel = InitializeConfiguation(configuration);

                // Create a kernel with AI services
                var kernelBuilder = Kernel.CreateBuilder().AddAzureOpenAIChatCompletion(
                    configurationModel.OpenAIModel,
                    configurationModel.OpenAIEndpoint,
                    configurationModel.OpenAIKey);

                // Add enterprise components
                kernelBuilder.Services.AddLogging(services => services.AddConsole().SetMinimumLevel(LogLevel.Error));

                // Initialize embedding services
                //var (azureOpenAITextEmbeddingGenerationService, textVectorStoreRecordCollection) = await InitializeTextEmbeddingServices(configurationModel);
                var (azureOpenAIVectorEmbeddingGenerationService, vectorStoreRecordCollection) = await InitializeVectorEmbeddingServices(configurationModel);

                // Build the kernel and retrieve services
                Kernel kernel = kernelBuilder.Build();
                var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

                // Add the plugins to the kernel
                kernel.Plugins.AddFromType<TimePlugin>("Time");
                kernel.Plugins.AddFromType<MathPlugin>("Math");

                //var fullTextSearchPlugin = new FullTextSearchPlugin(
                //    azureOpenAITextEmbeddingGenerationService,
                //    textVectorStoreRecordCollection);
                //kernel.Plugins.Add(fullTextSearchPlugin.GetTextSearchAsync());

                var vectorSearchPlugin = new VectorSearchPlugin(
                    azureOpenAIVectorEmbeddingGenerationService,
                    vectorStoreRecordCollection);
                kernel.Plugins.AddFromObject(vectorSearchPlugin);

                kernel.Plugins.Add(CreateBingSearchPlugin(configurationModel));

                // Enable planning
                OpenAIPromptExecutionSettings openAIPromptExecutionSettings = new()
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                    MaxTokens = 200,
                    ChatSystemPrompt = "You are assistant providing info in a single sentence for relevant context provided."
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

                    // Add user input to history
                    history.AddUserMessage(userInput);

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

        // Create a plugin for Bing search
        static KernelPlugin CreateBingSearchPlugin(ConfigurationModel configurationModel)
        {
            // Create a text search using Bing search
            var webSearch = new BingTextSearch(configurationModel.BingKey);

            // Build a text search plugin with Bing search and add to the kernel
            return webSearch.CreateWithGetTextSearchResults("InternetSearch", "Search internet for News, Information, Weather, Finance etc.");
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
            var azureAISearchEndpoint = configuration["AZUREAI_SEARCH_ENDPOINT"];
            var azureAISearchKey = configuration["AZUREAI_SEARCH_KEY"];

            if (string.IsNullOrWhiteSpace(openAIKey) ||
                string.IsNullOrWhiteSpace(openAIEndpoint) ||
                string.IsNullOrWhiteSpace(openAIModelId) ||
                string.IsNullOrWhiteSpace(openAIEmbeddingEndpoint) ||
                string.IsNullOrWhiteSpace(bingKey) ||
                string.IsNullOrWhiteSpace(openAIEmbeddingKey) ||
                string.IsNullOrWhiteSpace(openAIEmbeddingModel) ||
                string.IsNullOrWhiteSpace(azureAISearchEndpoint) ||
                string.IsNullOrWhiteSpace(azureAISearchKey))
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
                BingKey = bingKey,
                AzureAISearchEndpoint = azureAISearchEndpoint,
                AzureAISearchKey = azureAISearchKey
            };
        }

        static async Task<(ITextEmbeddingGenerationService, IVectorStoreRecordCollection<Guid, VectorModel>)> InitializeTextEmbeddingServices
            (ConfigurationModel configurationModel)
        {
            // Create an embedding generation service with the OpenAI API.
            ITextEmbeddingGenerationService azureOpenAITextEmbeddingGenerationService = new AzureOpenAITextEmbeddingGenerationService(
                configurationModel.OpenAIEmbeddingModel,
                configurationModel.OpenAIEmbeddingEndpoint,
                configurationModel.OpenAIEmbeddingKey);

            // Create an InMemory vector store.
            var inMemoryVectorStore = new InMemoryVectorStore();
            // Get a collection of vectors from the InMemory vector store.
            var vectorStoreRecordCollection = inMemoryVectorStore.GetCollection<Guid, VectorModel>("VectorData");
            await vectorStoreRecordCollection.CreateCollectionIfNotExistsAsync().ConfigureAwait(false);

            var embeddingsJob = new EmbeddingsJob(
                azureOpenAITextEmbeddingGenerationService,
                vectorStoreRecordCollection);
            vectorStoreRecordCollection = await embeddingsJob.GenerateEmbeddings();

            return (azureOpenAITextEmbeddingGenerationService, vectorStoreRecordCollection);
        }

        static async Task<(IEmbeddingGenerationService<string, float>, IVectorStoreRecordCollection<Guid, VectorModel>)> InitializeVectorEmbeddingServices
            (ConfigurationModel configurationModel)
        {
            // Create an embedding generation service with the OpenAI API.
            IEmbeddingGenerationService<string, float> azureOpenAIVectorEmbeddingGenerationService = new AzureOpenAITextEmbeddingGenerationService(
                configurationModel.OpenAIEmbeddingModel,
                configurationModel.OpenAIEmbeddingEndpoint,
                configurationModel.OpenAIEmbeddingKey);

            // Create an InMemory vector store.
            var inMemoryVectorStore = new InMemoryVectorStore();
            // Get a collection of vectors from the InMemory vector store.
            var vectorStoreRecordCollection = inMemoryVectorStore.GetCollection<Guid, VectorModel>("VectorData");
            await vectorStoreRecordCollection.CreateCollectionIfNotExistsAsync().ConfigureAwait(false);

            var embeddingsJob = new EmbeddingsJob(
                azureOpenAIVectorEmbeddingGenerationService,
                vectorStoreRecordCollection);
            vectorStoreRecordCollection = await embeddingsJob.GenerateEmbeddings();

            return (azureOpenAIVectorEmbeddingGenerationService, vectorStoreRecordCollection);
        }
    }
}