using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace StdbModule.Services;

using SpacetimeDB;
public class EmailService
{
    private readonly HttpClient _httpClient;
    private const string ResendApiEndpoint = "https://api.resend.com/emails";

    public EmailService()
    {
        _httpClient = new HttpClient();
        var apiKey = "re_jb7Ede2a_CcaWitzTd9z1UyjXZJyjFFkE";
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task SendVerificationEmailAsync(string email, string verificationCode, bool isRegistration = true)
    {
        try
        {
            var subject = isRegistration ? "Verify Your Email - Kulicha" : "Login Verification Code - Kulicha";
            var htmlContent = (verificationCode, isRegistration);

            var emailRequest = new
            {
                from = "you@domain.com",
                to = email,
                subject = subject,
                html = htmlContent
            };

            var json = JsonSerializer.Serialize(emailRequest);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(ResendApiEndpoint, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                Log.Error($"Failed to send verification email. Status: {response.StatusCode}, Error: {errorContent}");
                throw new Exception($"Failed to send verification email: {errorContent}");
            }

            Log.Info("Successfully sent verification email to {Email}", email);
        }
        catch (Exception ex)
        {
            Log.Error($"Error sending verification email to {email}" );
            throw;
        }
    }
}
