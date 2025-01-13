using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Plugins.Core;
using Microsoft.SemanticKernel.Plugins.Web.Bing;
using SemanticKernel.ConsoleApp.Models;
using SemanticKernel.ConsoleApp.Stores;
using System.ClientModel;

#pragma warning disable SKEXP0050

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

                // Build the kernel and retrieve services
                Kernel kernel = kernelBuilder.Build();
                var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

                // Add the plugin to the kernel
                kernel.Plugins.AddFromType<TimePlugin>("Time");
                kernel.Plugins.AddFromType<MathPlugin>("Math");
                kernel.Plugins.Add(await CreateVectorStorePluginAsync(configurationModel));
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

        static async Task<KernelPlugin> CreateVectorStorePluginAsync(ConfigurationModel configurationModel)
        {
            // Create and add embeddings to the vector store
            var vectorSearch = await MemoryVectorStore.GetTextSearchAsync(configurationModel);

            // Create a vector store plugin with the InMemory vector store and add to the kernel
            return vectorSearch.CreateWithGetTextSearchResults("EcoGroceries", "Call center transcripts from EcoGroceries by multiple agents with multiple customers.");
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
    }
}