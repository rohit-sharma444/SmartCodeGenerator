using Microsoft.Office.Interop.Outlook;
using Newtonsoft.Json;
using OllamaSharp;
using OllamaSharp.Models;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EmailAnswering
{
    /// <summary>
    /// Class for generating email replies using AI models.
    /// </summary>
    public static class EmailReplyAPI
    {
        /// <summary>
        /// Creates a reply to the currently selected email in the email explorer.
        /// </summary>
        /// <remarks>This method requires at least one email to be selected in the email explorer. If no
        /// email is selected,  or if the selected item is not a valid email (MailItem), an appropriate message will be
        /// displayed to the user. The reply email is displayed to the user for further editing before
        /// sending.</remarks>
        /// <param name="replyBody">The body content of the reply email. This text will be set as the reply's message body.</param>
        public static void CreateReply(string replyBody)
        {
            if (EmailExplorer.selection.Count > 0)
            {
                if (EmailExplorer.selection[1] is MailItem mailItem)
                {
                    // Create a reply to the selected email
                    MailItem reply = mailItem.Reply();
                    reply.Body = replyBody;
                    reply.Display();
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
        }


        /// <summary>
        /// Generates a professional reply template for the body of an email.
        /// </summary>
        /// <remarks>This method assumes that the email body is accessible via the <see
        /// cref="MailItem.Body"/> property. Ensure that the <paramref name="selection"/> contains a valid <see
        /// cref="MailItem"/> at index 1 before calling this method.</remarks>
        /// <param name="selection">The selection containing the email to process. Must include a <see cref="MailItem"/> at index 1.</param>
        /// <returns>A string containing the reply template with the email body inserted, or an empty string if the selection
        /// does not contain a valid <see cref="MailItem"/>.</returns>
        public static string ReadEmail(Selection selection)
        {
            string promptTemplate = "Write a professional reply to this email:\n\n{body}";
            //if (selection.Count > 0)
            //{
            if (selection[1] is MailItem mailItem)
            {
                string originalBody = mailItem.Body;
                return promptTemplate.Replace("{body}", originalBody);
            }
            return "";
        }


        /// <summary>
        /// Generates a reply to the given prompt using OpenAI's API.
        /// </summary>
        /// <param name="prompt"></param>
        /// <returns></returns>
        public static async Task<string> GenerateReplyFromOpenAIAsync(string prompt)
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", EmailExplorer.OpenAIApiKey);

                var requestBody = new
                {
                    model = EmailExplorer.OpenAiModelName,
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
                    dynamic result = JsonConvert.DeserializeObject<OpenAIResponse>(responseJson);
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
        public static async Task<string> GenerateReplyFromOllamaAIAsync(string prompt, string modelName = "llama4")
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


        /// <summary>
        /// Attempts to parse a <see cref="DateTime"/> value from the specified text input.
        /// </summary>
        /// <remarks>This method supports multiple date formats, including "MM/dd/yyyy", "dd-MMM-yyyy",
        /// and "yyyy-MM-dd", as well as natural language expressions like "today" and "tomorrow". If a natural language
        /// expression is detected, the time is assumed to be 10:00 AM.</remarks>
        /// <param name="text">The input text to search for a date or time pattern. This can include standard date formats (e.g.,
        /// "MM/dd/yyyy", "yyyy-MM-dd") or natural language expressions such as "today" or "tomorrow".</param>
        /// <param name="dateTime">When this method returns, contains the parsed <see cref="DateTime"/> value if the parsing was successful;
        /// otherwise, contains the default value of <see cref="DateTime"/>.</param>
        /// <returns><see langword="true"/> if a valid <see cref="DateTime"/> value was found and parsed successfully; otherwise,
        /// <see langword="false"/>.</returns>
        public static bool TryParseDateTimeFromBody(string text, out DateTime dateTime)
        {
            // Multiple date patterns
            string[] patterns =
            {
            @"\b\d{1,2}/\d{1,2}/\d{4}( \d{1,2}:\d{2}( ?[APap][Mm])?)?",
            @"\b\d{1,2}-[A-Za-z]{3}-\d{4}( \d{1,2}:\d{2}( ?[APap][Mm])?)?",
            @"\b\d{4}-\d{2}-\d{2}( \d{2}:\d{2})?",
            @"\b(?:tomorrow|today|next\s+\w+)"
            };

            foreach (string pattern in patterns)
            {
                Match match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    string found = match.Value.Trim();

                    // Handle natural language dates
                    if (found.Equals("tomorrow", StringComparison.OrdinalIgnoreCase))
                    {
                        dateTime = DateTime.Now.Date.AddDays(1).AddHours(10); // Assume 10 AM
                        return true;
                    }
                    else if (found.Equals("today", StringComparison.OrdinalIgnoreCase))
                    {
                        dateTime = DateTime.Now.Date.AddHours(10); // Assume 10 AM
                        return true;
                    }

                    if (DateTime.TryParse(found, out dateTime))
                    {
                        return true;
                    }
                }
            }
            dateTime = default;
            return false;
        }


        /// <summary>
        /// Creates a new appointment with the specified subject, body, and start time.
        /// </summary>
        /// <remarks>The appointment is automatically set to occur in the "Meeting Room / Online"
        /// location, with a reminder  configured to trigger 10 minutes before the start time. The appointment is saved
        /// immediately after creation.</remarks>
        /// <param name="subject">The subject of the appointment. This will be prefixed with "Meeting:".</param>
        /// <param name="body">The body content of the appointment, providing additional details.</param>
        /// <param name="startTime">The start time of the appointment. The appointment will last for 30 minutes.</param>
        public static void CreateAppointment(string subject, string body, DateTime startTime)
        {
            EmailExplorer.appointment.Subject = "Meeting: " + subject;
            EmailExplorer.appointment.Body = body;
            EmailExplorer.appointment.Start = startTime;
            EmailExplorer.appointment.End = startTime.AddMinutes(30);
            EmailExplorer.appointment.Location = "Meeting Room / Online";
            EmailExplorer.appointment.ReminderSet = true;
            EmailExplorer.appointment.ReminderMinutesBeforeStart = 10;

            EmailExplorer.appointment.Save();
        }


        /// <summary>
        /// Creates and schedules a meeting with the specified subject, body, and start time.
        /// </summary>
        /// <remarks>The meeting is configured with a default duration of 60 minutes and a reminder is
        /// set.  The meeting invitation is sent to the required attendees specified in the sender's email
        /// address.</remarks>
        /// <param name="subject">The subject of the meeting. This will be prefixed with "Meeting: " in the final meeting subject.</param>
        /// <param name="body">The body content of the meeting invitation.</param>
        /// <param name="startTime">The start time of the meeting. The meeting will be scheduled to start at this time.</param>
        public static void CreateMeeting(string subject, string body, DateTime startTime, string SenderEmailAddress)
        {
            EmailExplorer.meeting.MeetingStatus = OlMeetingStatus.olMeeting;
            EmailExplorer.meeting.Subject = "Meeting: " + subject;
            EmailExplorer.meeting.Body = body;
            EmailExplorer.meeting.Start = DateTime.Now.AddHours(2);
            EmailExplorer.meeting.Duration = 60;
            EmailExplorer.meeting.RequiredAttendees = SenderEmailAddress;
            EmailExplorer.meeting.ReminderSet = true;
            EmailExplorer.meeting.Save();
            EmailExplorer.meeting.Send();
        }


        /// <summary>
        /// Extracts the first date and time value found in the specified text, if any.
        /// </summary>
        /// <remarks>The method searches for date and time patterns in the input text using predefined
        /// formats, such as: <list type="bullet"> <item><description>MM/dd/yyyy with optional time (e.g., "07/06/2025
        /// 4:00 PM").</description></item> <item><description>dd-MMM-yyyy with optional time (e.g., "07-Jun-2025
        /// 16:00").</description></item> </list> If a match is found, it attempts to parse the matched value into a
        /// <see cref="DateTime"/> object. If parsing succeeds, the method returns the parsed <see cref="DateTime"/>;
        /// otherwise, it continues searching.</remarks>
        /// <param name="text">The input text to search for a date and time value. Cannot be null.</param>
        /// <returns>A <see cref="DateTime"/> object representing the first valid date and time found in the text,  or <see
        /// langword="null"/> if no valid date and time is found.</returns>
        public static DateTime? ExtractFirstDate(string text)
        {
            // Example pattern: 07/06/2025 4:00 PM or 07-Jun-2025 16:00
            string[] patterns =
            {
            @"\b\d{1,2}/\d{1,2}/\d{4}( \d{1,2}:\d{2}( ?[APap][Mm])?)?",
            @"\b\d{1,2}-[A-Za-z]{3}-\d{4}( \d{1,2}:\d{2}( ?[APap][Mm])?)?"
            };

            foreach (var pattern in patterns)
            {
                Match match = Regex.Match(text, pattern);
                if (match.Success && DateTime.TryParse(match.Value, out DateTime result))
                    return result;
            }
            return null;
        }


        /// <summary>
        /// Determines whether the specified text contains language indicative of a meeting or related activity.
        /// </summary>
        /// <remarks>This method uses regular expressions to identify common meeting-related terms and
        /// phrases in the provided text.  The analysis is case-insensitive and supports a variety of patterns,
        /// including explicit mentions of meetings  (e.g., "schedule a meeting") and contextual phrases (e.g., "join
        /// the call").</remarks>
        /// <param name="body">The text to analyze for meeting-related keywords or phrases.</param>
        /// <returns><see langword="true"/> if the text contains patterns commonly associated with meetings, such as "meeting,"
        /// "sync," or "schedule a call"; otherwise, <see langword="false"/>.</returns>
        public static bool IsMeeting(string body)
        {
            string[] patterns = {
                    @"\b(meet|meeting|sync|discussion)\b",
                    @"\blet'?s\s+(have|schedule|set up)\s+(a\s+)?(meeting|call|discussion)\b",
                    @"\b(join|attend|invite|calendar)\b",
                    @"\b(meeting|call|sync)\s+(at|on|in)\s+\w+"
                };

            return patterns.Any(pattern => Regex.IsMatch(body, pattern, RegexOptions.IgnoreCase));
        }


        /// <summary>
        /// Determines whether the specified text contains language indicative of an appointment or reminder.
        /// </summary>
        /// <remarks>This method uses predefined patterns to identify phrases such as "reminder,"
        /// "appointment," "follow-up," or similar terms that suggest an appointment or event. The matching is
        /// case-insensitive.</remarks>
        /// <param name="body">The text to analyze for appointment-related language. Cannot be null.</param>
        /// <returns><see langword="true"/> if the text contains patterns commonly associated with appointments, reminders, or
        /// similar events; otherwise, <see langword="false"/>.</returns>
        public static bool IsAppointment(string body)
        {
            string[] patterns = {
                @"\b(reminder|appointment|follow[- ]?up|block time|note)\b",
                @"\b(set|schedule)\s+(a\s+)?(reminder|note|appointment|event)\b",
                @"\b(call me|ping me|drop by|visit|see (me|you))\b"
            };

            return patterns.Any(pattern => Regex.IsMatch(body, pattern, RegexOptions.IgnoreCase));
        }
    }
}

public class OpenAIResponse
{
    public Choice[] choices { get; set; }
}

public class Choice
{
    public Message message { get; set; }
}

public class Message
{
    public string content { get; set; }
}