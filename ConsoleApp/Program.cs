using Azure;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace ConsoleApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Build configuration
            var builder = new ConfigurationBuilder()
                .AddUserSecrets<Program>();
            IConfiguration configuration = builder.Build();

            // Get connection string from secrets
            var connectionString = configuration["AZURE_AI_CONNECTION_STRING"];

            var credential = new DefaultAzureCredential();
            var client = new AgentsClient(connectionString, credential);

            // Step 1: Create an agent
            Response<Agent> agentResponse = await client.CreateAgentAsync(
                model: "gpt-4o",
                name: "Newy Copilot",
                instructions: "You are a general purpose AI chatbot. Answer every response with valid & credible citations to source.",
                tools: [new CodeInterpreterToolDefinition()]);
            Agent agent = agentResponse.Value;

            // Intermission: agent should now be listed

            Response<PageableList<Agent>> agentListResponse = await client.GetAgentsAsync();

            //// Step 2: Create a thread
            Response<AgentThread> threadResponse = await client.CreateThreadAsync();
            AgentThread thread = threadResponse.Value;

            // Get user name
            Console.Write("Enter your name: ");
            string? userName = Console.ReadLine();

            while (true)
            {
                Console.Write("You > ");
                string? userInput = Console.ReadLine();

                if (string.IsNullOrEmpty(userInput) || userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                // Step 3: Add a message to a thread
                Response<ThreadMessage> messageResponse = await client.CreateMessageAsync(
                    thread.Id,
                    MessageRole.User,
                    userInput);
                ThreadMessage message = messageResponse.Value;

                // Step 4: Run the agent
                Response<ThreadRun> runResponse = await client.CreateRunAsync(
                    thread.Id,
                    agent.Id,
                    additionalInstructions: $"Please address the user as {userName}.");
                ThreadRun run = runResponse.Value;

                do
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                    runResponse = await client.GetRunAsync(thread.Id, runResponse.Value.Id);
                }
                while (runResponse.Value.Status == RunStatus.Queued || runResponse.Value.Status == RunStatus.InProgress);

                Response<PageableList<ThreadMessage>> afterRunMessagesResponse = await client.GetMessagesAsync(thread.Id);
                IReadOnlyList<ThreadMessage> messages = afterRunMessagesResponse.Value.Data;

                // Note: messages iterate from newest to oldest, with the messages[0] being the most recent
                foreach (ThreadMessage threadMessage in messages)
                {
                    Console.Write($"{threadMessage.CreatedAt:yyyy-MM-dd HH:mm:ss} - {threadMessage.Role,10}: ");
                    foreach (MessageContent contentItem in threadMessage.ContentItems)
                    {
                        if (contentItem is MessageTextContent textItem)
                        {
                            Console.Write(textItem.Text);
                        }
                        else if (contentItem is MessageImageFileContent imageFileItem)
                        {
                            Console.Write($"<image from ID: {imageFileItem.FileId}>");
                        }
                        Console.WriteLine();
                    }
                    break;
                }
            }
        }
    }
}