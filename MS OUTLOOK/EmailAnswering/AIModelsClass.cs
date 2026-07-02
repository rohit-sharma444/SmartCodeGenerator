using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Net.Http.Json;


namespace EmailAnswering
{
    /// <summary>
    /// Class for AI models used in email answering applications.
    /// </summary>
    public static class AIModelsClass
    {
        /// <summary>
        /// Gets the name of the Ollama AI models to be used for email answering.
        /// </summary>
        /// <returns></returns>
        public static async Task<List<string>> GetOllamaModelNamesAsync()
        {
            var result = new List<string>();
            try
            {
                using HttpClient client = new HttpClient();
                client.BaseAddress = new Uri(GetOllamaModelServerUrl());

                var response = await client.GetAsync("/api/tags");
                response.EnsureSuccessStatusCode();

                var tagResponse = await response.Content.ReadFromJsonAsync<OllamaTagResponse>();

                result.AddRange(
                from model in tagResponse.Models
                select model.Name);
            }
            catch (Exception)
            {
                return new List<string>();
            }
            return result;
        }

        /// <summary>
        /// Gets the URL of the Ollama AI model server.
        /// </summary>
        /// <returns>Server URL as a string.</returns>
        public static string GetOllamaModelServerUrl() => "http://localhost:11434";

        /// <summary>
        /// Pulls an Ollama model by name from the Ollama server.
        /// </summary>
        /// <param name="modelName">The name of the model to pull.</param>
        /// <returns>True if the pull was successful, false otherwise.</returns>
        public static async Task<bool> PullOllamaModelAsync(string modelName)
        {
            try
            {
                using HttpClient client = new HttpClient();
                client.BaseAddress = new Uri(GetOllamaModelServerUrl());

                var response = await client.PostAsJsonAsync("/api/pull", new { name = modelName });
                response.EnsureSuccessStatusCode();

                // Optionally, you can check the response content for more details
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error pulling Ollama model '{modelName}': {ex.Message}");
                return false;
            }
        }
    }

    public class OllamaTagResponse
    {
        public List<OllamaModel> Models { get; set; }
    }

    public class OllamaModel
    {
        public string Name { get; set; }
        public string Digest { get; set; }
        public string Size { get; set; }
        public DateTime ModifiedAt { get; set; }
    }
}
