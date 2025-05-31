using Azure;
using Azure.Identity;
using Azure.AI.Projects;
using Azure.AI.Agents.Persistent;
using Microsoft.Extensions.Configuration;

namespace AzureAIAgent.ConsoleApp
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
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddUserSecrets<Program>();
                IConfiguration configuration = configurationBuilder.Build();
                var azureAIProjectEndpoint = configuration["AzureAIProjectEndpoint"];
                var azureAIAgentId = configuration["AzureAIAgentId"];
                var azureAIThreadId = configuration["AzureAIThreadId"];

                if (string.IsNullOrWhiteSpace(azureAIProjectEndpoint) || string.IsNullOrWhiteSpace(azureAIAgentId) || string.IsNullOrWhiteSpace(azureAIThreadId))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    throw new InvalidOperationException("One or more configuration values are missing. Please check your user secrets.");
                }

                await RunAgentConversationLoop(azureAIProjectEndpoint, azureAIAgentId, azureAIThreadId);
            }
            catch (Exception exception)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(exception.Message);
                Console.ReadKey();
            }
        }

        private static async Task RunAgentConversationLoop(string endpoint, string agentId, string threadId)
        {
            AIProjectClient projectClient = new(new Uri(endpoint), new DefaultAzureCredential());
            PersistentAgentsClient agentsClient = projectClient.GetPersistentAgentsClient();
            PersistentAgent agent = agentsClient.Administration.GetAgent(agentId);
            PersistentAgentThread thread = agentsClient.Threads.GetThread(threadId);

            string? userInput;
            do
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("User > ");
                userInput = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(userInput))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("User input cannot be null or empty. Please try again.");
                    continue;
                }

                if (userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                // Send user message to agent
                PersistentThreadMessage messageResponse = agentsClient.Messages.CreateMessage(
                    thread.Id,
                    MessageRole.User,
                    userInput);

                ThreadRun run = agentsClient.Runs.CreateRun(
                    thread.Id,
                    agent.Id);

                // Poll until the run reaches a terminal status
                do
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                    run = agentsClient.Runs.GetRun(thread.Id, run.Id);
                }
                while (run.Status == RunStatus.Queued
                    || run.Status == RunStatus.InProgress);

                if (run.Status != RunStatus.Completed)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Run failed or was canceled: {run.LastError?.Message}");
                    continue;
                }

                // Get and display the latest agent message
                Pageable<PersistentThreadMessage> messages = agentsClient.Messages.GetMessages(
                    thread.Id, order: ListSortOrder.Descending);

                var latestAgentMessage = messages
                    .FirstOrDefault(m => m.Role == MessageRole.Agent);

                if (latestAgentMessage != null)
                {
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.Write("Assistant > ");
                    foreach (MessageContent contentItem in latestAgentMessage.ContentItems)
                    {
                        if (contentItem is MessageTextContent textItem)
                        {
                            Console.Write(textItem.Text);
                        }
                        else if (contentItem is MessageImageFileContent imageFileItem)
                        {
                            Console.Write($"<image from ID: {imageFileItem.FileId}>");
                        }
                    }
                    Console.WriteLine();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("No response from agent.");
                }
            }
            while (userInput is not null);
        }
    }
}