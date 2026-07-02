using Microsoft.Office.Interop.Outlook;
using System.Collections.Generic;

namespace EmailAnswering
{
    /// <summary>
    /// Provides functionality for interacting with the Microsoft Outlook Explorer, managing email-related operations,
    /// and configuring OpenAI integration for email tone suggestions.
    /// </summary>
    /// <remarks>The <see cref="EmailExplorer"/> class is a static utility class designed to facilitate
    /// operations  related to the Outlook Explorer, such as accessing the active selection, creating appointments,  and
    /// managing email tone suggestions. It also provides properties for configuring OpenAI API  integration, including
    /// the API key and model name.  This class is intended for use in scenarios where automation or customization of
    /// Outlook email  workflows is required. Ensure that the Outlook application is properly initialized before using 
    /// this class.</remarks>
    public static class EmailExplorer
    {
        public static Explorer explorer { get; set; }
        public static Selection selection { get; set; }
        public static Application outlookApp { get; set; }
        public static AppointmentItem appointment { get; set; }
        public static AppointmentItem meeting { get; set; }
        public static List<string> emailTones { get; set; }
        public static string OpenAIApiKey { get; set; }
        public static string OpenAiModelName { get; set; }


        /// <summary>
        /// Initializes the Outlook Explorer and related objects for use within the application.
        /// </summary>
        /// <remarks>This method sets up the active Outlook Explorer, initializes the application
        /// instance,  and prepares default settings such as the OpenAI model name, API key, and email tone options.  It
        /// also creates default appointment and meeting items for further use.</remarks>
        public static void InitializeExplorer()
        {
            explorer = Globals.ThisAddIn.Application.ActiveExplorer();
            outlookApp = new Application();
            explorer = Globals.ThisAddIn.Application.ActiveExplorer();
            selection = explorer.Selection;
            OpenAiModelName = "gpt-4.1"; // Default model name
            OpenAIApiKey = "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";
            emailTones = new List<string> { "Formal", "Friendly", "Professional", "Casual", "Apologetic", "Promotional" };
            appointment = (AppointmentItem)outlookApp.CreateItem(OlItemType.olAppointmentItem);
            meeting = (AppointmentItem)outlookApp.CreateItem(OlItemType.olAppointmentItem);
        }
    }
}
