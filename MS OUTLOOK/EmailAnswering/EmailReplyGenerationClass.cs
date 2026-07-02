using Microsoft.Office.Interop.Outlook;
using System.Windows.Forms;
using OllamaSharp;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using System;
using OllamaSharp.Models;

namespace EmailAnswering
{
    /// <summary>
    /// Class for generating email replies using AI models.
    /// </summary>
    public static class EmailReplyGenerationClass
    {
        private static readonly string OpenAIApiKey = "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";
        private static readonly Explorer explorer = Globals.ThisAddIn.Application.ActiveExplorer();
        private static readonly Selection selection = explorer.Selection;
        private static string openAiModelName = "gpt-4.1";

        /// <summary>
        /// Reads the selected email in Outlook and prepares a prompt for AI reply generation.
        /// </summary>
        /// <param name="promptTemplate">
        /// Optional prompt template. Use "{body}" as a placeholder for the email body.
        /// Default: "Write a professional reply to this email:\n\n{body}"
        /// </param>
        /// <returns>Prompt with email body</returns>
        public static string ReadEmail(string promptTemplate = "Write a professional reply to this email:\n\n{body}")
        {
            if (selection.Count > 0)
            {
                if (selection[1] is MailItem mailItem)
                {
                    string originalBody = mailItem.Body;
                    return promptTemplate.Replace("{body}", originalBody);
                }
                else
                {
                    MessageBox.Show("Selected item is not a MailItem.");
                }
            }
            else
            {
                MessageBox.Show("No email selected.");
            }
            return "";
        }

        /// <summary>
        /// Sets the OpenAI model name to use for reply generation.
        /// </summary>
        /// <param name="modelName">The model name (e.g., "gpt-4.1").</param>
        public static void SetOpenAiModelName(string modelName)
        {
            if (!string.IsNullOrWhiteSpace(modelName))
                openAiModelName = modelName;
        }

        /// <summary>
        /// Generates a reply to the given prompt using OpenAI's API.
        /// </summary>
        /// <param name="prompt"></param>
        /// <returns></returns>
        public static async Task<string> GenerateReplyOpenAIAsync(string prompt)
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", OpenAIApiKey);

                var requestBody = new
                {
                    model = openAiModelName,
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    },
                    max_tokens = 300
                };

                string json = JsonConvert.SerializeObject(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response;
                try
                {
                    response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
                }
                catch (HttpRequestException ex)
                {
                    LogException(ex);
                    MessageBox.Show("Network error: " + ex.Message);
                    return null;
                }

                if (response.IsSuccessStatusCode)
                {
                    string responseJson = await response.Content.ReadAsStringAsync();
                    dynamic result = JsonConvert.DeserializeObject(responseJson);
                    return result.choices[0].message.content;
                }
                else
                {
                    var ex = new System.Exception("API Error: " + response.ReasonPhrase);
                    LogException(ex);
                    MessageBox.Show("API Error: " + response.ReasonPhrase);
                    return null;
                }
            }
        }


        /// <summary>
        /// Generates a reply to the given prompt using Ollama AI.
        /// </summary>
        /// <param name="prompt"></param>
        /// <param name="modelName">The Ollama model name to use.</param>
        /// <returns></returns>
        public static async Task<string> GenerateReplyOllamaAIAsync(string prompt, string modelName = "llama4")
        {
            try
            {
                var ollamaClient = new OllamaApiClient(new Uri(AIModelsClass.GetOllamaModelServerUrl()), modelName);
                var generateRequest = new GenerateRequest { Prompt = prompt };

                StringBuilder responseBuilder = new StringBuilder();
                await foreach (var result in ollamaClient.GenerateAsync(generateRequest))
                {
                    if (result != null && !string.IsNullOrEmpty(result.Response))
                    {
                        responseBuilder.Append(result.Response);
                    }
                }

                return responseBuilder.ToString();
            }
            catch (JsonException ex)
            {
                LogException(ex);
                MessageBox.Show("JSON parsing error: " + ex.Message);
                return null;
            }
            catch (HttpRequestException ex)
            {
                LogException(ex);
                MessageBox.Show("Network error: " + ex.Message);
                return null;
            }
            catch (TaskCanceledException tcEx)
            {
                LogException(tcEx);
                MessageBox.Show("Request was cancelled: " + tcEx.Message);
                return null;
            }
            catch (System.Exception ex)
            {
                LogException(ex);
                MessageBox.Show("Error: " + ex.Message);
                return null;
            }
        }

        private static void LogException(System.Exception ex)
        {
            // Simple file logging for demonstration; adjust path and method as needed
            string logPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EmailAnswering", "error.log");
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(logPath));
            System.IO.File.AppendAllText(logPath, $"{DateTime.Now}: {ex}\n");
        }
    }
}
