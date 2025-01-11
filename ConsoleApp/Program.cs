using Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
//using Microsoft.SemanticKernel.Plugins.Web.Bing;

namespace ConsoleApp
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var configurationBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddUserSecrets<Program>();
            IConfiguration configuration = configurationBuilder.Build();

            var openAIModelId = configuration["OPENAI_MODEL"];
            var openAIEndpoint = configuration["OPENAI_ENDPOINT"];
            var openAIApiKey = configuration["OPENAI_KEY"];
            //var bingApiKey = configuration["BING_API_KEY"];

            if (string.IsNullOrEmpty(openAIApiKey) || string.IsNullOrEmpty(openAIEndpoint) || string.IsNullOrEmpty(openAIModelId))
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

            // Create a text search using Bing search
            //var textSearch = new BingTextSearch(apiKey: bingApiKey);

            // Build a text search plugin with Bing search and add to the kernel
            //var searchPlugin = textSearch.CreateWithSearch("SearchPlugin");
            //kernel.Plugins.Add(searchPlugin);

            // Enable planning
            OpenAIPromptExecutionSettings openAIPromptExecutionSettings = new()
            {
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                MaxTokens = 100,
                StopSequences = new[] { ".", "!", "?" }          
            };

            // Create a history store the conversation
            var history = new ChatHistory();

            // Add a system prompt for persona instructions
            history.AddSystemMessage("Answer in one single sentence with collocations");

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
                var response = chatCompletionService.GetStreamingChatMessageContentsAsync(
                    chatHistory: history,
                    executionSettings: openAIPromptExecutionSettings,
                    kernel: kernel);

                // Print the results
                Console.Write("Assistant > ");
                var result = string.Empty;
                await foreach (var chunk in response)
                {
                    result += chunk;
                    Console.Write(chunk);
                }

                // Add the message from the agent to the chat history
                history.AddAssistantMessage(result);

                if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    Console.ReadKey();
                    break;
                }
            }
            while (userInput is not null);
        }
    }
}