using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace PROJECTBUILDER
{
/// <summary>
/// Represents Email.
/// </summary>
    public static class Email
    {
        public static void SendEmail()
        {
             try
            {
                MailMessage newMail = new MailMessage();
                // use the Gmail SMTP Host
                SmtpClient client = new SmtpClient("smtp.gmail.com");

                // Follow the RFS 5321 Email Standard
                newMail.From = new MailAddress("rohit.sharma@codis.co.uk", "Rohit Sharma");

                newMail.To.Add("rohitsharma444@gmail.com");// declare the email subject

                newMail.Subject = "My First Email"; // use HTML for the email body

                newMail.IsBodyHtml = true;
                
                newMail.Body = "<h1> This is my first Templated Email in C# </h1>";

                // enable SSL for encryption across channels
                client.EnableSsl = true; 

                // Port 465 for SSL communication
                client.Port = 25;
                 
                // Provide authentication information with Gmail SMTP server to authenticate your sender account
                client.Credentials = new System.Net.NetworkCredential("rohitsharma444@gmail.com", "");
                
                System.Net.ServicePointManager.Expect100Continue = false;

                client.Send(newMail); // Send the constructed mail
                Console.WriteLine("Email Sent");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error -" +ex);
            }
        }
    }
}
