using ezZkvi.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ezZkvi.Services
{
    public class EmailService : IEmailService
    {
        private readonly EmailSettings _settings;

        private void ValidateEmailSettings()
        {
            if (string.IsNullOrWhiteSpace(_settings.SmtpServer) ||
                _settings.SmtpPort == 0 ||
                string.IsNullOrWhiteSpace(_settings.SenderEmail) ||
                string.IsNullOrWhiteSpace(_settings.Username) ||
                string.IsNullOrWhiteSpace(_settings.Password))
            {
                throw new InvalidOperationException(
                    "Email konfiguracija nije ispravno podešena. Provjerite SMTP podatke u appsettings.json ili Environment Variables."
                );
            }
        }

        public EmailService(IOptions<EmailSettings> settings)
        {
            _settings = settings.Value;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            ValidateEmailSettings();

            try
            {
                var message = new MimeMessage();

                message.From.Add(new MailboxAddress(_settings.SenderName, _settings.SenderEmail));
                message.To.Add(MailboxAddress.Parse(toEmail));
                message.Subject = subject;

                message.Body = new TextPart("html")
                {
                    Text = BuildEmailTemplate(subject, body)
                };

                using var client = new SmtpClient();

                await client.ConnectAsync(
                    _settings.SmtpServer,
                    _settings.SmtpPort,
                    SecureSocketOptions.StartTls
                );

                await client.AuthenticateAsync(_settings.Username, _settings.Password);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Email nije poslan. Provjerite Brevo SMTP konfiguraciju.",
                    ex
                );
            }
        }

        private string BuildEmailTemplate(string naslov, string poruka)
        {
            var safeNaslov = System.Net.WebUtility.HtmlEncode(naslov);
            var safePoruka = System.Net.WebUtility.HtmlEncode(poruka)
                .Replace("\n", "<br>");

            return $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta charset='UTF-8'>
                </head>
                <body style='font-family: Arial, sans-serif; background-color: #f4f4f4; padding: 20px;'>
                    <div style='max-width: 600px; margin: auto; background-color: white; border-radius: 8px; padding: 25px; border: 1px solid #ddd;'>
        
                        <h2 style='color: #2c3e50; margin-top: 0;'>
                            {safeNaslov}
                        </h2>

                        <p style='font-size: 15px; color: #333; line-height: 1.6;'>
                            {safePoruka}
                        </p>

                        <hr style='margin: 25px 0; border: none; border-top: 1px solid #ddd;' />

                        <p style='font-size: 14px; color: #555;'>
                            Srdačan pozdrav,<br>
                            <strong>eZkvi tim</strong>
                        </p>
                    </div>
                </body>
                </html>";
        }
    }
}