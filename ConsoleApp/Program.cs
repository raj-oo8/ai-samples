using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Data;
using Microsoft.SemanticKernel.Plugins.Core;
using Microsoft.SemanticKernel.Plugins.Web.Bing;

#pragma warning disable SKEXP0050

namespace ConsoleApp
{
    internal class Program
    {
        static async Task Main(string[] args)
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

                if (string.IsNullOrWhiteSpace(openAIApiKey) || string.IsNullOrWhiteSpace(openAIEndpoint) || string.IsNullOrWhiteSpace(openAIModelId))
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

                if (!string.IsNullOrWhiteSpace(bingApiKey))
                {
                    // Create a text search using Bing search
                    var textSearch = new BingTextSearch(bingApiKey);

                    // Build a text search plugin with Bing search and add to the kernel
                    var searchPlugin = textSearch.CreateWithSearch("SearchPlugin");
                    kernel.Plugins.Add(searchPlugin);
                }

                // Add a plugin (the LightsPlugin class is defined below)
                kernel.Plugins.AddFromType<TimePlugin>("Time");
                kernel.Plugins.AddFromType<MathPlugin>("Math");

                // Enable planning
                OpenAIPromptExecutionSettings openAIPromptExecutionSettings = new()
                {
                    FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
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
    }
}