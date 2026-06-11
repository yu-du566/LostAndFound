using MailKit.Net.Smtp;
using MimeKit;

namespace LostAndFound.Services
{
    /// <summary>
    /// 邮件通知服务 — 关键事件发生时通过SMTP发送邮件
    /// </summary>
    public class EmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ILogger<EmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// 发送邮件（异步，不阻塞主请求）
        /// </summary>
        public async Task SendAsync(string toEmail, string subject, string body)
        {
            var smtpSection = _config.GetSection("Smtp");
            var server = smtpSection["Server"];
            var port = int.Parse(smtpSection["Port"] ?? "587");
            var username = smtpSection["Username"];
            var password = smtpSection["Password"];
            var fromEmail = smtpSection["FromEmail"];
            var fromName = smtpSection["FromName"] ?? "校园失物招领系统";

            if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(username))
            {
                _logger.LogWarning("SMTP 未配置，跳过邮件发送。收件人: {To}, 主题: {Subject}", toEmail, subject);
                return;
            }

            try
            {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(fromName, fromEmail));
                message.To.Add(new MailboxAddress("", toEmail));
                message.Subject = subject;

                var builder = new BodyBuilder
                {
                    HtmlBody = $@"
                    <div style='max-width:600px;margin:0 auto;font-family:Microsoft YaHei,sans-serif;'>
                        <div style='background:#4A90D9;padding:20px;text-align:center;border-radius:8px 8px 0 0;'>
                            <h2 style='color:#fff;margin:0;'>📋 校园失物招领系统</h2>
                        </div>
                        <div style='background:#fff;padding:24px;border:1px solid #e0e0e0;border-top:none;border-radius:0 0 8px 8px;'>
                            {body}
                            <hr style='border:none;border-top:1px solid #eee;margin:20px 0;'/>
                            <p style='color:#999;font-size:12px;text-align:center;'>
                                此邮件由系统自动发送，请勿回复。<br/>
                                发送时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}
                            </p>
                        </div>
                    </div>"
                };
                message.Body = builder.ToMessageBody();

                using var client = new SmtpClient();
                await client.ConnectAsync(server, port, MailKit.Security.SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(username, password);
                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                _logger.LogInformation("邮件发送成功 → {To}, 主题: {Subject}", toEmail, subject);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "邮件发送失败 → {To}, 主题: {Subject}", toEmail, subject);
            }
        }

        /// <summary>
        /// 向所有拥有邮箱的用户群发邮件
        /// </summary>
        public async Task SendToAllAsync(List<(string Email, string RealName)> recipients, string subject, string body)
        {
            foreach (var (email, name) in recipients)
            {
                if (!string.IsNullOrWhiteSpace(email))
                {
                    await SendAsync(email, subject, body);
                }
            }
        }
    }
}
