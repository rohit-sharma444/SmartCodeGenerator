using Microsoft.Office.Interop.Outlook;
using Microsoft.Office.Tools.Ribbon;
using System;
using System.Windows.Forms;

namespace EmailAnswering
{
    public partial class EmailAnsweringRibbon
    {
        private void EmailAnsweringRibbon_Load(object sender, RibbonUIEventArgs e)
        {
        }

        private void btnReply_Click(object sender, RibbonControlEventArgs e)
        {
            var originalEmail = EmailReplyAPI.ReadEmail(EmailExplorer.selection);
            if (!string.IsNullOrEmpty(originalEmail))
            {
                var replyEmail = EmailReplyAPI.GenerateReplyFromOpenAIAsync(originalEmail).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(replyEmail))
                    EmailReplyAPI.CreateReply(replyEmail);
                else
                    MessageBox.Show("Failed to generate a reply.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            else
                MessageBox.Show("No email content to generate a reply for.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }


        private void btnAppointment_Click(object sender, RibbonControlEventArgs e)
        {
            EmailExplorer.outlookApp.ItemSend += new ApplicationEvents_11_ItemSendEventHandler(ItemSendHandler);
        }

        private static void ItemSendHandler(object item, ref bool cancel)
        {
            if (item is MailItem mailItem)
            {
                if (mailItem.Subject.StartsWith("RE:", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Reply email detected. Attempting to create an appointment...");

                    // Check if the email body indicates a meeting request
                    if (EmailReplyAPI.IsMeeting(mailItem.Body))
                    {
                        if (EmailReplyAPI.TryParseDateTimeFromBody(mailItem.Body, out DateTime appointmentTime))
                            EmailReplyAPI.CreateMeeting(mailItem.Subject, mailItem.Body, appointmentTime, mailItem.Sender.Address);
                        else
                            Console.WriteLine("No valid date/time found in email body.");
                    }

                    //Check if the email body indicates an appointment request
                    if (EmailReplyAPI.IsAppointment(mailItem.Body))
                    {
                        if (EmailReplyAPI.TryParseDateTimeFromBody(mailItem.Body, out DateTime appointmentTime))
                            EmailReplyAPI.CreateAppointment(mailItem.Subject, mailItem.Body, appointmentTime);
                        else
                            Console.WriteLine("No valid date/time found in email body.");
                    }
                }
            }
        }
    }
}
