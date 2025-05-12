using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents.AzureAI;
using Microsoft.SemanticKernel.ChatCompletion;
using System.ClientModel;

#pragma warning disable SKEXP0110

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
                var azureAIProjectConnectionString = configuration["AzureAIProjectConnectionString"];
                var azureAIAgentId = configuration["AzureAIAgentId"];

                if (string.IsNullOrWhiteSpace(azureAIProjectConnectionString) || string.IsNullOrWhiteSpace(azureAIAgentId))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    throw new InvalidOperationException("One or more configuration values are missing. Please check your user secrets.");
                }

                AIProjectClient client = AzureAIAgent.CreateAzureAIClient(azureAIProjectConnectionString, new AzureCliCredential());
                AgentsClient agentsClient = client.GetAgentsClient();

                Azure.AI.Projects.Agent definition = await agentsClient.GetAgentAsync(azureAIAgentId);
                AzureAIAgent agent = new(definition, agentsClient);

                // Initiate a back-and-forth chat
                string? userInput;
                do
                {
                    // Collect user input
                    Console.WriteLine();
                    Console.WriteLine();
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.Write("User > ");
                    userInput = Console.ReadLine();

                    if (string.IsNullOrEmpty(userInput))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("User input cannot be null or empty. Please try again.");
                        continue;
                    }

                    Microsoft.SemanticKernel.Agents.AgentThread agentThread = new AzureAIAgentThread(agent.Client);

                    try
                    {
                        ChatMessageContent message = new(AuthorRole.User, userInput);
                        // Print the results
                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.Write("Assistant > ");
                        await foreach (StreamingChatMessageContent response in agent.InvokeStreamingAsync(message, agentThread))
                        {
                            Console.Write(response.Content);
                        }

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
                    finally
                    {
                        await agentThread.DeleteAsync();
                        await agent.Client.DeleteAgentAsync(agent.Id);
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